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
import signal
import socket
import sys
import threading
import traceback
import _thread
from collections import deque

PROTOCOL_VERSION = 1
MAX_FRAME_BYTES = 64 * 1024 * 1024
_RECV_CHUNK = 65536

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

# Buffered output is emitted on this cadence, or earlier when a flush is requested or the
# buffer grows past the threshold, so long-running cells show progress as it happens.
FLUSH_INTERVAL_SECONDS = 0.05
FLUSH_THRESHOLD_CHARS = 8192

# Blocking waits poll at this interval so a KeyboardInterrupt is observed promptly.
WAIT_POLL_SECONDS = 0.05

_registered_sources = deque()


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
            try:
                self._channel.write({
                    "type": "stream",
                    "req_id": request_id,
                    "name": name,
                    "text": text,
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
    return {
        "name": type(exc).__name__,
        "message": str(exc),
        "traceback": "".join(formatted),
    }


def syntax_error_payload(exc):
    # format_exception_only already renders the source line and caret for a SyntaxError,
    # and there is no user frame to report because nothing ran.
    return {
        "name": type(exc).__name__,
        "message": str(exc),
        "traceback": "".join(traceback.format_exception_only(type(exc), exc)),
    }


def render_value(value):
    """
    Reduce a value to a MIME payload. This covers the HTML and plain-text cases that the
    1.x kernel supported; the wider repr chain arrives with the rich display work.
    """
    if value is None:
        return None

    html = getattr(value, "_repr_html_", None)
    if callable(html):
        try:
            rendered = html()
            if rendered is not None:
                return {"mime": "text/html", "data": str(rendered)}
        except Exception:
            pass  # fall through to the plain-text form

    return {"mime": "text/plain", "data": repr(value)}


class HostSession:
    """Owns the persistent namespace and executes cells against it."""

    def __init__(self, channel):
        self.channel = channel
        self.router = OutputRouter(channel)
        self.input_bridge = InputBridge(channel, self.router)
        self.scope = new_scope()
        self.request_id = UNSOLICITED_REQUEST_ID

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

        builtins.input = self._input
        builtins.display = self._display

        try:
            import getpass
            getpass.getpass = self._getpass
        except ImportError:
            pass

        # Windows delivers a console control event rather than SIGINT; the handler turns it
        # into the same KeyboardInterrupt the Unix default handler raises.
        sigbreak = getattr(signal, "SIGBREAK", None)
        if sigbreak is not None:
            try:
                signal.signal(sigbreak, _raise_keyboard_interrupt)
            except (ValueError, OSError):
                pass  # no console attached; the interrupt message path still applies

    def apply_bootstrap(self, config):
        """Import the configured modules and run startup code in the persistent scope."""
        config = config or {}

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

        status = "ok"
        result = None
        error = None

        filename = cell_filename(request_id)
        register_source(filename, source)

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
                        result = render_value(value)
            except KeyboardInterrupt:
                status = "cancelled"
            except SystemExit as exc:
                # A cell calling sys.exit() ends the cell, not the interpreter.
                status = "error"
                error = error_payload(exc)
            except BaseException as exc:
                status = "error"
                error = error_payload(exc)

        self.router.set_request(UNSOLICITED_REQUEST_ID)
        self.request_id = UNSOLICITED_REQUEST_ID

        self.channel.write({
            "type": "execute_result",
            "req_id": request_id,
            "status": status,
            "result": result,
            "error": error,
            "variables": {},
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

    # --- builtins handed to user code ---

    def _input(self, prompt=""):
        # The prompt travels with the request rather than being written to stdout: the
        # front end renders it on the input control itself.
        return self.input_bridge.request("" if prompt is None else str(prompt), False, self.request_id)

    def _getpass(self, prompt="Password: ", stream=None):
        return self.input_bridge.request(str(prompt), True, self.request_id)

    def _display(self, obj, mime_type=None):
        if obj is None:
            return

        if mime_type:
            payload = {"mime": str(mime_type), "data": str(obj)}
        else:
            payload = render_value(obj)
            if payload is None:
                return

        # Output written before this call belongs above it.
        self.router.flush()
        self.channel.write({
            "type": "display",
            "req_id": self.request_id,
            "mime": payload["mime"],
            "data": payload["data"],
        })

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


def _hello_payload(token):
    return {
        "type": "hello",
        "token": token,
        "protocol": PROTOCOL_VERSION,
        "python": {
            "version": platform.python_version(),
            "executable": sys.executable,
            "implementation": platform.python_implementation(),
        },
        "capabilities": ["execute", "stream", "display", "input", "interrupt"],
    }


def _run(channel, session):
    """Feed inbound messages onto a queue and dispatch them on the main thread."""
    inbox = queue.Queue()

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

                inbox.put(message)
        except (OSError, SocketClosed, ProtocolError, ValueError):
            pass
        finally:
            session.input_bridge.abandon()
            inbox.put(None)  # sentinel: stream ended

    reader_thread = threading.Thread(target=reader, name="verso-host-reader", daemon=True)
    reader_thread.start()

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
        # Other message types are handled by later layers; ignore them here.


def main(argv=None):
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--connect", required=True)
    args, _ = parser.parse_known_args(argv)

    token = os.environ.get("VERSO_PYHOST_TOKEN", "")
    host, port = _parse_endpoint(args.connect)

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
