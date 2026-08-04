"""Verso Python host.

Runs the user's Python interpreter as a subprocess of the Verso kernel and speaks a
small newline-delimited JSON protocol over a loopback socket. This module establishes
the connection, owns the persistent cell namespace, and executes cells with live output
streaming, interactive input, and interrupt support. It depends only on the standard
library and targets CPython 3.8 and newer.
"""

import argparse
import ast
import asyncio
import builtins
import importlib
import inspect
import io
import json
import linecache
import os
import platform
import queue
import shlex
import signal
import socket
import subprocess
import sys
import threading
import time
import traceback
import _thread
from collections import deque

# Siblings of this script, extracted alongside it. Imported before the working directory
# joins sys.path so a file in the notebook's folder cannot stand in for either.
import _versohost_comm as comm_support
import _versohost_display as display_support
import _versohost_intel as intel_support
import _versohost_scan as scan_support
import _versohost_vars as variable_support

PROTOCOL_VERSION = 1
MAX_FRAME_BYTES = 64 * 1024 * 1024
_RECV_CHUNK = 65536

# How often the watchdog looks for the process that launched this one. The socket is the
# ordinary way this process learns that process is gone, so this is a backstop for the one case
# the socket cannot cover, and it does not need to be quick.
PARENT_WATCH_INTERVAL_SECONDS = 2.0

# Exit code used when the watchdog ends this process. Nothing reads it, because the process that
# would have is by definition gone, but it separates this exit from a clean shutdown in a log.
EXIT_PARENT_GONE = 3

# Request id reserved for events raised on the host's own behalf rather than in response
# to a request, such as a failure while replaying bootstrap configuration.
UNSOLICITED_REQUEST_ID = 0

# Cells compile under a synthetic file name so tracebacks point at the user's code and the
# harness frames that wrap it can be identified and dropped. Each execution gets its own
# name: a function defined in one cell and called from another must still report the source
# it was actually defined in.
CELL_FILENAME_PREFIX = "<cell"
CELL_FILENAME = "<cell>"
CELL_SOURCE_LIMIT = 200

# Sources of cells that have run, replayed as context for completions and hover. Bounded for
# the same reason the traceback cache is: a long session must not grow without limit, and the
# live namespace already carries everything those cells defined.
CELL_HISTORY_LIMIT = CELL_SOURCE_LIMIT

# Buffered output is emitted on this cadence, or earlier when a flush is requested or the
# buffer grows past the threshold, so long-running cells show progress as it happens.
FLUSH_INTERVAL_SECONDS = 0.05
FLUSH_THRESHOLD_CHARS = 8192

# A single write can be arbitrarily large, and one that does not fit in a frame would be lost
# entirely. Oversized text is split across frames instead, well under the frame limit so that
# escaping and multi-byte characters cannot push a piece over it.
MAX_STREAM_TEXT_CHARS = 1024 * 1024

# Blocking waits poll at this interval so a KeyboardInterrupt is observed promptly.
WAIT_POLL_SECONDS = 0.05

_registered_sources = deque()

# Requests answered off the main thread, because the main thread may be inside a cell.
INTELLISENSE_REQUESTS = ("complete", "hover", "diagnostics")


class SocketClosed(Exception):
    """Raised when the peer closes the connection mid-frame."""


class ProtocolError(Exception):
    """Raised when a frame violates the wire contract."""


class FrameChannel:
    """Reads and writes newline-delimited JSON frames over a socket."""

    def __init__(self, sock):
        self._sock = sock
        self._buffer = bytearray()
        self._send_lock = threading.Lock()

    def read(self):
        """Read one message. Return a dict, or None at a clean end of stream."""
        while True:
            newline = self._buffer.find(b"\n")
            if newline != -1:
                line = bytes(self._buffer[:newline])
                del self._buffer[: newline + 1]
                if len(line) > MAX_FRAME_BYTES:
                    raise ProtocolError("incoming frame exceeds the size limit")
                return json.loads(line.decode("utf-8"))

            if len(self._buffer) > MAX_FRAME_BYTES:
                raise ProtocolError("incoming frame exceeds the size limit")

            chunk = self._sock.recv(_RECV_CHUNK)
            if not chunk:
                if self._buffer:
                    raise SocketClosed("connection closed mid-frame")
                return None
            self._buffer.extend(chunk)

    def write(self, message):
        """Serialize and send one message as a single frame."""
        data = json.dumps(message, ensure_ascii=False).encode("utf-8")
        if len(data) > MAX_FRAME_BYTES:
            raise ProtocolError("outgoing frame exceeds the size limit")
        frame = data + b"\n"
        with self._send_lock:
            self._sock.sendall(frame)


def _split_stream_text(text):
    """
    Yield ``text`` in pieces small enough to each fit in one frame. Text that already fits is
    yielded whole, which is every ordinary case.
    """
    if len(text) <= MAX_STREAM_TEXT_CHARS:
        yield text
        return

    for start in range(0, len(text), MAX_STREAM_TEXT_CHARS):
        yield text[start:start + MAX_STREAM_TEXT_CHARS]


class OutputRouter:
    """
    Collects text written to the redirected standard streams and emits it as ``stream``
    events. Both streams share one ordered buffer so their relative order survives, and
    contiguous runs of the same stream coalesce into a single event.
    """

    def __init__(self, channel):
        self._channel = channel
        self._lock = threading.Lock()
        self._segments = []
        self._pending = 0
        self._request_id = UNSOLICITED_REQUEST_ID
        self._stop = threading.Event()
        self._thread = None

    def start(self):
        self._thread = threading.Thread(
            target=self._run, name="verso-host-flusher", daemon=True)
        self._thread.start()

    def stop(self):
        self._stop.set()

    def set_request(self, request_id):
        """Attribute subsequent output to a request, after flushing what came before it."""
        self.flush()
        with self._lock:
            self._request_id = request_id

    def write(self, name, text):
        if not text:
            return
        with self._lock:
            if self._segments and self._segments[-1][0] == name:
                self._segments[-1][1] += text
            else:
                self._segments.append([name, text])
            self._pending += len(text)
            crossed_threshold = self._pending >= FLUSH_THRESHOLD_CHARS

        if crossed_threshold:
            self.flush()

    def flush(self):
        with self._lock:
            if not self._segments:
                return
            segments = self._segments
            request_id = self._request_id
            self._segments = []
            self._pending = 0

        # Emitted outside the lock: a send can block, and user code writing from another
        # thread must not be held up behind it.
        for name, text in segments:
            for piece in _split_stream_text(text):
                try:
                    self._channel.write({
                        "type": "stream",
                        "req_id": request_id,
                        "name": name,
                        "text": piece,
                    })
                except (OSError, ProtocolError, ValueError):
                    # The connection is gone; there is nowhere left to report output.
                    return

    def write_error(self, name, text, error_name):
        """
        Emit text that reports a failure rather than ordinary stream output. Anything already
        buffered is flushed first so the failure still appears after the output that preceded it.
        """
        self.flush()

        with self._lock:
            request_id = self._request_id

        for piece in _split_stream_text(text):
            try:
                self._channel.write({
                    "type": "stream",
                    "req_id": request_id,
                    "name": name,
                    "text": piece,
                    "error": error_name,
                })
            except (OSError, ProtocolError, ValueError):
                # The connection is gone; there is nowhere left to report output.
                return

    def _run(self):
        while not self._stop.wait(FLUSH_INTERVAL_SECONDS):
            self.flush()


class StreamWriter:
    """A minimal text file object standing in for ``sys.stdout`` or ``sys.stderr``."""

    def __init__(self, router, name):
        self._router = router
        self._name = name
        self.encoding = "utf-8"
        self.errors = "strict"

    def write(self, text):
        if not isinstance(text, str):
            raise TypeError("write() argument must be str, not " + type(text).__name__)
        self._router.write(self._name, text)
        return len(text)

    def writelines(self, lines):
        for line in lines:
            self.write(line)

    def flush(self):
        self._router.flush()

    def close(self):
        self.flush()

    def isatty(self):
        return False

    def readable(self):
        return False

    def writable(self):
        return True

    def seekable(self):
        return False

    def fileno(self):
        # The real descriptor belongs to the raw pipe, which the managing process reads
        # separately; handing it out here would bypass request correlation.
        raise io.UnsupportedOperation("fileno")

    @property
    def closed(self):
        return False


class InputBridge:
    """Turns a blocking ``input()`` call into an ``input_request`` round trip."""

    def __init__(self, channel, router):
        self._channel = channel
        self._router = router
        self._lock = threading.Lock()
        self._arrived = threading.Event()
        self._value = None

    def request(self, prompt, password, request_id):
        with self._lock:
            self._value = None
            self._arrived.clear()

        # Anything already printed belongs above the prompt.
        self._router.flush()
        self._channel.write({
            "type": "input_request",
            "req_id": request_id,
            "prompt": prompt,
            "password": password,
        })

        # Polled rather than blocking indefinitely so an interrupt arriving while the cell
        # waits for input is observed instead of being swallowed by the wait.
        while not self._arrived.wait(WAIT_POLL_SECONDS):
            pass

        with self._lock:
            value = self._value

        if value is None:
            raise EOFError("no input was provided")
        return value

    def resolve(self, message):
        with self._lock:
            value = message.get("value")
            self._value = value if isinstance(value, str) else None
        self._arrived.set()

    def abandon(self):
        """Release a pending request, for example when the connection drops."""
        with self._lock:
            self._value = None
        self._arrived.set()


def new_scope():
    """Create the module-like namespace that every cell executes in."""
    return {
        "__name__": "__main__",
        "__doc__": None,
        "__package__": None,
        "__loader__": None,
        "__spec__": None,
        "__builtins__": builtins,
    }


def cell_filename(request_id):
    return "<cell-%d>" % request_id


def register_source(filename, source):
    """
    Publish a cell's text so tracebacks and inspection show the offending lines. Old
    entries are evicted to keep a long session from growing the cache without bound.
    """
    linecache.cache[filename] = (len(source), None, source.splitlines(True), filename)
    _registered_sources.append(filename)
    while len(_registered_sources) > CELL_SOURCE_LIMIT:
        linecache.cache.pop(_registered_sources.popleft(), None)


def split_cell(source, filename=CELL_FILENAME):
    """
    Compile a cell into an optional statement body and an optional trailing expression.

    The trailing expression is what the cell displays. Splitting on the parsed tree rather
    than on the text handles multi-line expressions, trailing comments, and decorated or
    nested constructs that a line-based heuristic gets wrong.
    """
    tree = ast.parse(source, filename=filename, mode="exec")

    body = tree.body
    tail = None
    if body and isinstance(body[-1], ast.Expr):
        tail = body[-1]
        body = body[:-1]

    statements = None
    if body:
        module = ast.Module(body=body, type_ignores=[])
        statements = compile(
            module, filename, "exec", flags=ast.PyCF_ALLOW_TOP_LEVEL_AWAIT)

    expression = None
    if tail is not None:
        wrapped = ast.Expression(body=tail.value)
        expression = compile(
            wrapped, filename, "eval", flags=ast.PyCF_ALLOW_TOP_LEVEL_AWAIT)

    return statements, expression


def suppresses_result(source):
    """A trailing semicolon hides the cell's result, matching notebook convention."""
    return source.rstrip().endswith(";")


def is_cell_frame(tb):
    return tb.tb_frame.f_code.co_filename.startswith(CELL_FILENAME_PREFIX)


def user_traceback(tb):
    """
    Drop the harness frames that precede the user's own code so the traceback starts at
    the cell. Frames from the event loop sit between the harness and a coroutine cell, so
    the scan runs until the first cell frame rather than skipping a fixed number.
    """
    scan = tb
    while scan is not None:
        if is_cell_frame(scan):
            return scan
        scan = scan.tb_next

    # Nothing in the chain belongs to a cell (an exception raised entirely inside a
    # callback, say); showing the full traceback beats showing none of it.
    return tb


def error_payload(exc):
    formatted = traceback.format_exception(
        type(exc), exc, user_traceback(exc.__traceback__))
    payload = {
        "name": type(exc).__name__,
        "message": str(exc),
        "traceback": "".join(formatted),
    }

    # A dynamic import is invisible to a pre-execution scan, so the failure itself is the
    # only place the missing module can be named. Narrower than ImportError deliberately: a
    # plain ImportError means the module was found and the name inside it was not, which no
    # install fixes.
    if isinstance(exc, ModuleNotFoundError):
        missing = getattr(exc, "name", None)
        if isinstance(missing, str) and missing:
            payload["missing_module"] = missing

    return payload


def syntax_error_payload(exc):
    # format_exception_only already renders the source line and caret for a SyntaxError,
    # and there is no user frame to report because nothing ran.
    return {
        "name": type(exc).__name__,
        "message": str(exc),
        "traceback": "".join(traceback.format_exception_only(type(exc), exc)),
    }


def _is_shorthand(line):
    stripped = line.strip()
    return stripped.startswith("!") or stripped.startswith("%pip")


def transform_escapes(source):
    """
    Rewrite the notebook shorthands a cell may carry: a line starting with ``!`` runs as a
    shell command, and ``%pip`` runs pip against this interpreter.

    Source that already parses is returned untouched. That single check is what keeps a
    ``!`` line inside a string literal from being rewritten, because such a cell is valid
    Python and never reaches the substitution below.
    """
    lines = source.splitlines(True)

    # Checked before parsing, so an ordinary cell pays nothing for a feature it is not using.
    if not any(_is_shorthand(line) for line in lines):
        return source

    try:
        ast.parse(source)
    except SyntaxError:
        pass
    except Exception:
        return source
    else:
        return source

    rewritten = []
    changed = False

    for line in lines:
        body = line.rstrip("\r\n")
        ending = line[len(body):]
        stripped = body.strip()
        indent = body[: len(body) - len(body.lstrip())]

        if stripped.startswith("!"):
            # Indentation is preserved so the command still composes inside a loop or an if.
            rewritten.append("%s__verso_shell__(%r)%s" % (indent, stripped[1:].strip(), ending))
            changed = True
        elif stripped.startswith("%pip"):
            rewritten.append("%s__verso_pip__(%r)%s" % (indent, stripped[4:].strip(), ending))
            changed = True
        else:
            rewritten.append(line)

    return "".join(rewritten) if changed else source


class HostSession:
    """Owns the persistent namespace and executes cells against it."""

    def __init__(self, channel):
        self.channel = channel
        self.router = OutputRouter(channel)
        self.input_bridge = InputBridge(channel, self.router)
        self.scope = new_scope()
        self.request_id = UNSOLICITED_REQUEST_ID
        self.shell_escapes = True
        self.publish_limit_bytes = variable_support.DEFAULT_LIMIT_BYTES

        # Read from the editor-assistance thread while the main thread runs a cell, which is
        # exactly when an answer would be both slow to produce and stale on arrival.
        self.executing = False
        self.history = deque(maxlen=CELL_HISTORY_LIMIT)

        # Created up front and installed as this thread's loop, so a cell that schedules work
        # without awaiting it (asyncio.ensure_future, get_event_loop) uses the same loop a later
        # cell awaits on. Creating it lazily would strand those tasks on a loop that never runs.
        self._loop = asyncio.new_event_loop()
        asyncio.set_event_loop(self._loop)

    # --- setup ---

    def install(self):
        """Redirect the standard streams and replace the interactive input builtins."""
        self.router.start()
        sys.stdout = StreamWriter(self.router, "stdout")
        sys.stderr = StreamWriter(self.router, "stderr")

        display_support.install(self._emit_display)

        # Before any user code runs, because a widget opens its comm while it is being built.
        # Installed later, every widget constructed in the meantime would already hold the
        # stand-in that discards what it is given, and nothing would put that right.
        comm_support.install(self.channel.write, self._report_comm_refusal)

        builtins.input = self._input
        builtins.display = display_support.display
        builtins.__verso_shell__ = self._shell
        builtins.__verso_pip__ = self._pip

        try:
            import getpass
            getpass.getpass = self._getpass
        except ImportError:
            pass

        # An exception raised on a thread the cell started never reaches the code that reports
        # the cell's result, so the interpreter's default hook would print it to standard error
        # and it would read as ordinary text. Reported as a failure instead.
        threading.excepthook = self._thread_excepthook

        # Windows delivers a console control event rather than SIGINT; the handler turns it
        # into the same KeyboardInterrupt the Unix default handler raises.
        sigbreak = getattr(signal, "SIGBREAK", None)
        if sigbreak is not None:
            try:
                signal.signal(sigbreak, _raise_keyboard_interrupt)
            except (ValueError, OSError):
                pass  # no console attached; the interrupt message path still applies

    def _report_comm_refusal(self, text):
        """
        Report a widget message the host would not send. Written as a failure rather than as
        ordinary output: nothing else in the notebook will show that the browser and Python have
        stopped agreeing about what a widget holds.
        """
        self.router.write_error("stderr", text + "\n", "WidgetMessageRefused")

    def handle_comm(self, message):
        """Hand an inbound comm message to the widget it names, on this thread."""
        try:
            comm_support.dispatch(message)
        except Exception:
            # A widget's own handler failing is the widget's problem to report, and the loop
            # that called this has messages after this one to get to.
            pass

    def _thread_excepthook(self, args):
        """Report an unhandled background-thread exception as a failure rather than as text."""
        # SystemExit is how a thread is asked to stop, not a fault, and the default hook
        # ignores it for the same reason.
        if args.exc_type is SystemExit:
            return

        formatted = traceback.format_exception(
            args.exc_type, args.exc_value, args.exc_traceback)
        thread_name = getattr(args.thread, "name", None) or "a background thread"
        name = args.exc_type.__name__ if args.exc_type is not None else "ThreadError"

        self.router.write_error(
            "stderr",
            "Exception on {}:\n{}".format(thread_name, "".join(formatted)),
            name)

    def apply_bootstrap(self, config):
        """Import the configured modules and run startup code in the persistent scope."""
        config = config or {}

        if "enable_shell_escapes" in config:
            self.shell_escapes = bool(config.get("enable_shell_escapes"))

        limit = config.get("variable_publish_limit_bytes")
        if isinstance(limit, int) and limit > 0:
            self.publish_limit_bytes = limit

        intel_support.configure(config.get("jedi_tools_path"))
        display_support.configure_assets(config.get("widget_asset_source"))

        # The first editor request would otherwise pay for importing the analysis library, which
        # takes long enough to notice. Warmed on a thread of its own so startup does not wait for
        # it, and a request that arrives meanwhile waits for this attempt rather than starting over.
        threading.Thread(
            target=intel_support.load_jedi, name="verso-host-intel-warmup", daemon=True).start()

        for name in config.get("default_imports") or []:
            if not isinstance(name, str) or not name:
                continue
            try:
                importlib.import_module(name)
            except Exception:
                continue  # an unavailable module is skipped, as it was in 1.x
            root = name.split(".")[0]
            module = sys.modules.get(root)
            if module is not None:
                self.scope[root] = module

        startup = config.get("startup_code")
        if not startup:
            return

        try:
            exec(compile(startup, "<startup>", "exec"), self.scope)
        except BaseException:
            self.router.write(
                "stderr", "Startup code failed:\n" + traceback.format_exc())
            self.router.flush()

    # --- execution ---

    def execute(self, message):
        request_id = message.get("id", UNSOLICITED_REQUEST_ID)
        source = message.get("code") or ""

        self.request_id = request_id
        self.router.set_request(request_id)

        # Whether a widget shown by this cell can be given a channel. Carried per cell rather
        # than agreed once, because the surface an output is going to is the managing side's
        # business and can differ between one cell and the next.
        display_support.set_live_widgets(message.get("live_widgets"))

        # A package installed moments ago by a magic command in this same cell is only
        # importable once the finders forget what they saw on the path before it existed.
        importlib.invalidate_caches()

        variable_support.apply_inject(self.scope, message.get("inject"))

        status = "ok"
        result = None
        error = None

        if self.shell_escapes:
            source = transform_escapes(source)

        filename = cell_filename(request_id)
        register_source(filename, source)

        self.executing = True
        try:
            try:
                statements, expression = split_cell(source, filename)
            except SyntaxError as exc:
                status = "error"
                error = syntax_error_payload(exc)
            else:
                try:
                    if statements is not None:
                        self._run(statements)
                    if expression is not None:
                        value = self._run(expression)
                        if not suppresses_result(source):
                            result = display_support.render_value(value)
                except KeyboardInterrupt:
                    status = "cancelled"
                except SystemExit as exc:
                    # A cell calling sys.exit() ends the cell, not the interpreter.
                    status = "error"
                    error = error_payload(exc)
                except BaseException as exc:
                    status = "error"
                    error = error_payload(exc)

                # Kept as context for later completions and hover. A cell that failed to
                # parse is left out, because its text would spoil every answer after it, and
                # so is a cancelled one, which ran only in part.
                if status != "cancelled":
                    self.history.append(source)
        finally:
            self.executing = False

        # Figures the cell drew but never showed belong to it, so they are emitted before
        # the result and while the request is still the one being answered.
        if status != "cancelled":
            try:
                display_support.flush_figures()
            except Exception:
                pass

        variables = {}
        oversized = []
        if status != "cancelled" and message.get("publish"):
            try:
                variables, oversized = variable_support.collect(
                    self.scope, self.publish_limit_bytes)
            except Exception:
                variables, oversized = {}, []

        self.router.set_request(UNSOLICITED_REQUEST_ID)
        self.request_id = UNSOLICITED_REQUEST_ID

        self.channel.write({
            "type": "execute_result",
            "req_id": request_id,
            "status": status,
            "result": result,
            "error": error,
            "variables": variables,
            "oversized": oversized,
        })

    def _run(self, code):
        if code.co_flags & inspect.CO_COROUTINE:
            return self._event_loop().run_until_complete(eval(code, self.scope))
        return eval(code, self.scope)

    def _event_loop(self):
        """One loop for the process lifetime, so tasks a cell starts stay alive for later cells."""
        if self._loop is None or self._loop.is_closed():
            self._loop = asyncio.new_event_loop()
            asyncio.set_event_loop(self._loop)
        return self._loop

    # --- editor assistance ---

    def answer_intellisense(self, message):
        """
        Answer a completion, hover, or diagnostics request. This runs on a thread of its own
        and must never raise: an answer that cannot be produced is an empty one.
        """
        request_id = message.get("id", UNSOLICITED_REQUEST_ID)
        kind = message.get("type")
        code = message.get("code") or ""
        cursor = message.get("cursor")
        if not isinstance(cursor, int):
            cursor = 0

        # A cell in flight is in the middle of changing the namespace these answers are built
        # from, and waiting for it would answer a cursor the user has long since left. An empty
        # result is both the honest reply and the prompt one.
        busy = self.executing
        history = [] if message.get("history") is False else list(self.history)

        if kind == "complete":
            completions = []
            if not busy:
                try:
                    completions = intel_support.complete(history, code, cursor, self.scope)
                except Exception:
                    completions = []
            self._write_reply("complete_reply", request_id, "completions", completions)
        elif kind == "hover":
            info = None
            if not busy:
                try:
                    info = intel_support.hover(history, code, cursor, self.scope)
                except Exception:
                    info = None
            self._write_reply("hover_reply", request_id, "hover", info)
        elif kind == "diagnostics":
            found = []
            if not busy:
                try:
                    found = intel_support.diagnostics(history, code)
                except Exception:
                    found = []
            self._write_reply("diagnostics_reply", request_id, "diagnostics", found)

    # --- package discovery ---

    def scan_imports(self, message):
        """
        Report the modules this cell imports that the environment cannot resolve, and which
        of the declared requirements are not installed.

        Answered on the main thread, which is where the import path and the site directories
        this reply describes are the ones the cell is about to run against. Never raises: a
        scan that cannot be produced is an empty one, and the cell runs either way.
        """
        request_id = message.get("id", UNSOLICITED_REQUEST_ID)

        # A package installed by a magic command moments ago is only discoverable once the
        # finders forget what the path looked like before it existed.
        try:
            importlib.invalidate_caches()
        except Exception:
            pass

        try:
            missing = scan_support.scan(message.get("code") or "")
        except Exception:
            missing = []

        try:
            pending = scan_support.unsatisfied(message.get("requirements") or [])
        except Exception:
            pending = []

        try:
            self.channel.write({
                "type": "scan_reply",
                "req_id": request_id,
                "missing": missing,
                "unsatisfied": pending,
            })
        except Exception:
            pass  # the connection has gone; the request's own deadline covers it

    def widget_snapshot(self, message):
        """
        Answer with a current page for one widget, so a save records what a reader is looking
        at rather than what the cell drew.

        Answered on the main thread for the same reason a comm message is applied there: a
        widget's state is only coherent between the statements that change it. The cost is
        that a request arriving during a cell waits for the cell, which the asking side bounds
        and answers by keeping the page it already had.
        """
        request_id = message.get("id", UNSOLICITED_REQUEST_ID)

        try:
            document = display_support.snapshot(message.get("widget_id"))
        except Exception:
            document = None

        self._write_reply("widget_snapshot_reply", request_id, "data", document)

    def _write_reply(self, reply_type, request_id, field, payload):
        try:
            self.channel.write({"type": reply_type, "req_id": request_id, field: payload})
        except Exception:
            pass  # the connection has gone; the request's own deadline covers it

    # --- builtins handed to user code ---

    def _input(self, prompt=""):
        # The prompt travels with the request rather than being written to stdout: the
        # front end renders it on the input control itself.
        return self.input_bridge.request("" if prompt is None else str(prompt), False, self.request_id)

    def _getpass(self, prompt="Password: ", stream=None):
        return self.input_bridge.request(str(prompt), True, self.request_id)

    def _emit_display(self, mime, data, widget_id=None):
        # Output written before this call belongs above it.
        self.router.flush()
        frame = {
            "type": "display",
            "req_id": self.request_id,
            "mime": mime,
            "data": data,
        }

        # Only a widget asking to be live has one, and it is what a later request for a fresh
        # page names, so it travels with the payload rather than being looked up afterwards.
        if widget_id is not None:
            frame["widget_id"] = widget_id

        self.channel.write(frame)

    def _shell(self, command):
        """Run a shell command for a ``!`` line, streaming its output into the cell."""
        command = (command or "").strip()
        if not command:
            return

        self._stream_process(command, shell=True)

    def _pip(self, arguments):
        """Run pip for a ``%pip`` line against the interpreter this host is running."""
        sys.stdout.write(
            "%pip runs pip in this notebook's interpreter. #!pip is the Verso equivalent.\n")

        try:
            argv = shlex.split(arguments or "")
        except ValueError:
            argv = (arguments or "").split()

        self._stream_process([sys.executable, "-m", "pip"] + argv, shell=False)

    def _stream_process(self, command, shell):
        process = subprocess.Popen(
            command,
            shell=shell,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            universal_newlines=True)

        try:
            for line in process.stdout:
                sys.stdout.write(line)
            code = process.wait()
        except KeyboardInterrupt:
            # The cell is being cancelled, so the child goes with it.
            process.kill()
            process.wait()
            raise
        finally:
            if process.stdout is not None:
                process.stdout.close()

        if code != 0:
            sys.stderr.write("Command exited with code %d.\n" % code)

    # --- teardown ---

    def close(self):
        self.router.stop()
        self.router.flush()
        if self._loop is not None:
            try:
                self._loop.close()
            except Exception:
                pass


def _raise_keyboard_interrupt(signum, frame):
    raise KeyboardInterrupt()


def _parse_endpoint(value):
    host, _, port = value.rpartition(":")
    if not host or not port:
        raise ValueError("expected host:port for --connect")
    return host, int(port)


def _has_jedi():
    """
    Whether the richer analysis is already installed in this interpreter's own environment.
    Checked with the finders rather than an import, which would cost startup time for a
    question the managing side only needs answered to decide whether to provision.
    """
    try:
        import importlib.util
        return importlib.util.find_spec("jedi") is not None
    except Exception:
        return False


def _hello_payload(token):
    capabilities = [
        "execute", "stream", "display", "input", "interrupt",
        "complete", "hover", "diagnostics", "scan_imports", "comm",
    ]
    if _has_jedi():
        capabilities.append("jedi")

    return {
        "type": "hello",
        "token": token,
        "protocol": PROTOCOL_VERSION,
        "python": {
            "version": platform.python_version(),
            "executable": sys.executable,
            "implementation": platform.python_implementation(),
        },
        "capabilities": capabilities,
    }


def _run(channel, session):
    """Feed inbound messages onto a queue and dispatch them on the main thread."""
    inbox = queue.Queue()
    intel_inbox = queue.Queue()

    def reader():
        try:
            while True:
                message = channel.read()
                if message is None:
                    break

                kind = message.get("type")

                # Handled here rather than on the queue: when either arrives the main
                # thread is inside user code, not reading the queue.
                if kind == "input_reply":
                    session.input_bridge.resolve(message)
                    continue
                if kind == "interrupt":
                    _thread.interrupt_main()
                    continue

                # Editor requests go to a thread of their own for the same reason, and not to
                # this one, because producing an answer takes long enough to hold up the next
                # interrupt if it ran here.
                if kind in INTELLISENSE_REQUESTS:
                    intel_inbox.put(message)
                    continue

                inbox.put(message)
        except (OSError, SocketClosed, ProtocolError, ValueError):
            pass
        finally:
            session.input_bridge.abandon()
            intel_inbox.put(None)  # sentinel: stream ended
            inbox.put(None)

    def intellisense():
        while True:
            message = intel_inbox.get()
            if message is None:
                break
            session.answer_intellisense(message)

    reader_thread = threading.Thread(target=reader, name="verso-host-reader", daemon=True)
    reader_thread.start()

    intel_thread = threading.Thread(
        target=intellisense, name="verso-host-intellisense", daemon=True)
    intel_thread.start()

    while True:
        try:
            message = inbox.get()
        except KeyboardInterrupt:
            # An interrupt that arrived between cells has nothing to cancel.
            continue

        if message is None:
            break

        kind = message.get("type")
        if kind == "shutdown":
            break
        if kind == "execute":
            session.execute(message)
        elif kind == "scan_imports":
            # Deliberately here rather than on the assistance thread: that thread answers
            # empty while a cell runs, which is the opposite of what a scan needs, and a
            # scan is always sequenced immediately before its own cell's execute, so this
            # thread is between cells when it arrives.
            session.scan_imports(message)
        elif kind == "comm_msg":
            # Here rather than on the reader thread, which handles only the two kinds that run
            # no user code of their own. Setting a trait runs every observer the author
            # registered, so it belongs on the thread the author's own cell runs on. The cost is
            # that a message arriving mid-cell waits for the cell, which is the same ordering a
            # Jupyter kernel gives it.
            session.handle_comm(message)
        elif kind == "widget_snapshot":
            # Here for the same reason a comm message is: it reads widget state, which is only
            # settled between cells. A save that arrives mid-cell waits, and the asking side
            # gives up rather than holding the save open.
            session.widget_snapshot(message)
        # Other message types are handled by later layers; ignore them here.


def watch_parent(original_parent_id, interval=PARENT_WATCH_INTERVAL_SECONDS,
                 get_parent_id=os.getppid, sleep=time.sleep, exit_process=os._exit):
    """
    Poll for the process that launched this one and leave when it is no longer there.

    Reparenting is what makes its departure observable: once the launching process exits, this
    one is handed to another parent, so a parent id that no longer matches the one recorded at
    startup means the session's owner is gone. Leaving is deliberately abrupt, because the main
    thread may be many frames into user code that will never return, which is the whole reason
    this runs on a thread of its own, so there is nothing to unwind and nobody left to tell.
    """
    while True:
        sleep(interval)

        try:
            current = get_parent_id()
        except OSError:
            # Being unable to ask is not evidence of an answer either way.
            continue

        if current != original_parent_id:
            exit_process(EXIT_PARENT_GONE)
            return


def _expected_parent_id():
    """
    The pid this process should be a child of.

    The kernel passes its own, and that is what makes the watch reliable: reading the parent id
    here can already be too late, because a kernel that dies during startup has been replaced by
    the time this process asks, and a watch comparing against what it read then would spend the
    session watching a pid that was never its owner. The live reading remains the fallback, for a
    host started by hand with no kernel to have been told by.
    """
    try:
        given = int(os.environ.get("VERSO_PYHOST_PARENT", ""))
    except ValueError:
        return os.getppid()

    return given if given > 0 else os.getppid()


def start_parent_watchdog(interval=PARENT_WATCH_INTERVAL_SECONDS, platform_name=os.name,
                          expected_parent_id=None):
    """
    Watch for the launching process going away, reporting the thread that does the watching.

    The ordinary way this process ends is the socket: when the kernel exits, the reader sees end
    of stream and the dispatch loop finishes. That path needs the main thread to come back to the
    queue, which a cell running an unbounded loop never does, so a kernel lost while such a cell
    is running would otherwise leave this process spinning against a closed socket with nobody
    able to interrupt it.

    Elsewhere the same case is covered from the outside, by a job object that ends this process
    when the kernel's handle to it closes. It has to be, because a parent id is not updated when
    the parent exits there, so the check this starts could never notice.
    """
    if platform_name != "posix":
        return None

    if expected_parent_id is None:
        expected_parent_id = _expected_parent_id()

    thread = threading.Thread(
        target=watch_parent,
        args=(expected_parent_id, interval),
        name="verso-host-parent-watch",
        daemon=True)
    thread.start()
    return thread


def main(argv=None):
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--connect", required=True)
    args, _ = parser.parse_known_args(argv)

    token = os.environ.get("VERSO_PYHOST_TOKEN", "")
    host, port = _parse_endpoint(args.connect)

    # Started before the connection rather than after the handshake, so the window between
    # launching and having a socket to notice anything on is covered as well.
    start_parent_watchdog()

    # The process runs in the notebook's own directory, so putting it on the path lets a
    # cell import a module sitting next to the notebook. It goes after this script's own
    # directory, which stays first, so a user file cannot stand in for a host module.
    working_directory = os.getcwd()
    if working_directory not in sys.path:
        sys.path.insert(1, working_directory)

    sock = socket.create_connection((host, port))
    try:
        sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        channel = FrameChannel(sock)

        # Authenticate and exchange bootstrap configuration before entering the loop.
        channel.write(_hello_payload(token))
        hello_ok = channel.read()
        if hello_ok is None or hello_ok.get("type") != "hello_ok":
            # The peer rejected the handshake or dropped the connection.
            return 0

        session = HostSession(channel)
        session.install()
        try:
            # Applied before the loop starts, so an execute sent immediately after the
            # handshake is processed against a fully prepared scope.
            session.apply_bootstrap(hello_ok.get("config"))
            _run(channel, session)
        finally:
            session.close()
    finally:
        try:
            sock.close()
        except OSError:
            pass
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:
        traceback.print_exc()
        sys.exit(1)
