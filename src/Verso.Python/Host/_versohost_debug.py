"""Pausing a running cell at a line, for the Verso Python host.

A cell stops itself: the interpreter is asked to call back on each line of cell code, and the
callback decides whether this line is one to stop at. When it is, the cell's own thread reports
where it is and waits to be told how to carry on. Nothing attaches from outside, so the rest of
the host keeps running, which is what lets the reply arrive at all.

On Python 3.12 and newer the callbacks come from ``sys.monitoring``, where a line that is not
worth stopping at can be switched off after its first report and costs nothing from then on.
Older interpreters use ``sys.settrace``, which reports every line of cell code for as long as a
debugged cell runs. Library code is never reported under either.

Depends only on the standard library and targets CPython 3.8 and newer.
"""

import bisect
import dis
import reprlib
import sys
import threading
import types
from collections import deque
from itertools import islice

# Mirrors versohost: the prefix every cell's code is compiled under, and how many cells are
# remembered before the oldest is forgotten.
CELL_FILENAME_PREFIX = "<cell"
CELL_LIMIT = 200

# A wait for the next instruction polls at this interval so an interrupt is observed promptly
# rather than swallowed by the wait.
WAIT_POLL_SECONDS = 0.05

# What a stop reports is read by a person in a narrow panel, so it is kept short: a few
# hundred names at most, each value cut to a line or two.
MAX_VARIABLES = 200
MAX_VALUE_CHARS = 200

CONTINUE = "continue"
STEP_OVER = "step_over"
STEP_INTO = "step_into"
STEP_OUT = "step_out"

_USE_MONITORING = hasattr(sys, "monitoring")

# Top-level names that are part of the furniture rather than something the cell is working on.
_HIDDEN_AT_TOP_LEVEL = (types.ModuleType, types.FunctionType, types.BuiltinFunctionType, type)


# A collection past this many items is shown from its first few rather than in full.
LARGE_COLLECTION = 1000


class _ShortRepr(reprlib.Repr):
    """
    ``reprlib``, without the two things that make it slow on a very large value. It sorts every
    key of a dict or a set before showing the first few, and a type it has no rule for, which
    includes ``bytes`` and the dict subclasses in ``collections``, has its whole repr built and
    then cut. A stop describes every variable in scope, and a step is a stop, so either would be
    paid on each line stepped over.
    """

    def repr1(self, x, level):
        if level > 0:
            if isinstance(x, (bytes, bytearray)):
                if len(x) > self.maxstring:
                    return repr(x[:self.maxstring])[:-1] + "..."
            elif isinstance(x, dict):
                if len(x) > LARGE_COLLECTION:
                    return self._first_few(
                        x, "{", "}", type(x) is not dict,
                        ("%s: %s" % (self.repr1(key, level - 1), self.repr1(value, level - 1))
                         for key, value in islice(dict.items(x), self.maxdict)))
            elif isinstance(x, (set, frozenset)):
                if len(x) > LARGE_COLLECTION:
                    return self._first_few(
                        x, "{", "}", type(x) is not set,
                        (self.repr1(item, level - 1) for item in islice(iter(x), self.maxset)))
        return super().repr1(x, level)

    @staticmethod
    def _first_few(x, opening, closing, named, pieces):
        text = opening + ", ".join(pieces) + ", ..." + closing
        return "%s(%s)" % (type(x).__name__, text) if named else text


def _make_repr():
    limited = _ShortRepr()
    limited.maxlevel = 3
    limited.maxstring = MAX_VALUE_CHARS
    limited.maxother = MAX_VALUE_CHARS
    limited.maxlong = MAX_VALUE_CHARS
    for name in ("maxlist", "maxtuple", "maxset", "maxfrozenset", "maxdeque", "maxarray", "maxdict"):
        setattr(limited, name, 20)
    return limited


_REPR = _make_repr()


def describe_value(value):
    """
    A short rendering of a value. A ``__repr__`` that raises still gives a line to show. An
    interrupt that arrives while one is running is not the value's doing, though: it is the cell
    being stopped, and it is let through so that the cell does stop.
    """
    try:
        text = _REPR.repr(value)
    except KeyboardInterrupt:
        raise
    except BaseException as exc:  # a repr that raises is still a value worth listing
        text = "<%s: repr raised %s>" % (type(value).__name__, type(exc).__name__)

    if len(text) > MAX_VALUE_CHARS:
        text = text[:MAX_VALUE_CHARS - 3] + "..."
    return text


def describe_variables(frame, top_level):
    """The names in scope in a frame, as ``{"name", "type", "value"}`` entries."""
    try:
        names = dict(frame.f_locals)
    except KeyboardInterrupt:
        raise
    except BaseException:
        return []

    entries = []
    for name, value in names.items():
        if not isinstance(name, str) or name.startswith("__"):
            continue
        # The cell's namespace holds every import and every function the notebook has
        # defined. Listing them would bury the handful of values the reader is looking for.
        if top_level and isinstance(value, _HIDDEN_AT_TOP_LEVEL):
            continue

        entries.append({
            "name": name,
            "type": type(value).__name__,
            "value": describe_value(value),
        })
        if len(entries) >= MAX_VARIABLES:
            break
    return entries


def is_cell_code(code):
    return code.co_filename.startswith(CELL_FILENAME_PREFIX)


def executable_lines(codes):
    """The lines that begin a statement in the given code objects and everything nested in them."""
    lines = set()
    pending = [code for code in codes if isinstance(code, types.CodeType)]
    while pending:
        code = pending.pop()
        for _, line in dis.findlinestarts(code):
            # Newer interpreters open every code object with an instruction on line 0.
            if line is not None and line >= 1:
                lines.add(line)
        pending.extend(const for const in code.co_consts if isinstance(const, types.CodeType))
    return sorted(lines)


class CellDebugger:
    """
    Decides where a running cell stops, and holds it there.

    One instance lives for the life of the host. A run that is to be debugged calls ``begin``
    before the cell's code and ``end`` after it; between the two, ``set_breakpoints`` and
    ``resume`` arrive from the thread reading the socket while the cell's own thread is either
    running or waiting in ``_stop``.
    """

    def __init__(self, report, flush):
        """
        ``report(frames, reason)`` sends a stop to the managing side, and ``flush()`` pushes out
        anything the cell has printed, so output written before a stop appears before it.
        """
        self._report = report
        self._flush = flush
        self._lock = threading.Lock()

        # Which cell each compiled file belongs to, and which file is the latest run of each
        # cell. Only the latest run's lines match what the reader is looking at, so only it
        # can carry that cell's breakpoints.
        self._cell_of_file = {}
        self._file_of_cell = {}
        self._code_lines = {}       # file -> sorted lines that begin a statement
        self._files = deque()

        self._breakpoints = {}      # cell -> frozenset of lines
        self._lines_of_file = {}    # file -> frozenset of lines, derived from the two above

        self._active = False
        self._thread = None         # the thread running the cell, the only one that stops
        self._describing = False

        # How the cell was last told to carry on, and the frame that instruction was given in.
        self._mode = CONTINUE
        self._step_frame = None
        self._step_codes = frozenset()

        self._resumed = threading.Event()
        self._action = CONTINUE
        self._waiting = False

        # sys.monitoring only: where each line of a code object starts, kept for the run.
        self._monitoring_tool = None
        self._line_starts = {}

    # --- which cell is which ---

    def note_run(self, filename, cell, codes=()):
        """
        Record that ``filename`` is the code of ``cell`` as it was just run, compiled into
        ``codes``.
        """
        if not cell:
            return
        with self._lock:
            self._cell_of_file[filename] = cell
            self._file_of_cell[cell] = filename
            self._code_lines[filename] = executable_lines(codes)
            self._files.append(filename)
            while len(self._files) > CELL_LIMIT:
                old = self._files.popleft()
                self._code_lines.pop(old, None)
                old_cell = self._cell_of_file.pop(old, None)
                if old_cell is not None and self._file_of_cell.get(old_cell) == old:
                    del self._file_of_cell[old_cell]
            self._rebuild_lines()

    def _rebuild_lines(self):
        lines = {}
        for cell, wanted in self._breakpoints.items():
            filename = self._file_of_cell.get(cell)
            if filename is None:
                continue

            # A breakpoint on a line that begins no statement, a blank line or a comment,
            # would never be reached. It is moved to the next line that does, which is where
            # the reader meant the cell to stop.
            starts = self._code_lines.get(filename) or []
            bound = set()
            for line in wanted:
                at = bisect.bisect_left(starts, line)
                if at < len(starts):
                    bound.add(starts[at])
            if bound:
                lines[filename] = frozenset(bound)
        self._lines_of_file = lines

    # --- called around a run ---

    def begin(self, breakpoints):
        """Start reporting lines for the cell about to run on this thread."""
        self._apply_breakpoints(breakpoints)
        self._thread = threading.get_ident()
        self._mode = CONTINUE
        self._step_frame = None
        self._step_codes = frozenset()
        self._waiting = False
        self._active = True
        self._install()

    def end(self):
        """Stop reporting lines. Safe to call when ``begin`` was not."""
        if not self._active:
            return
        self._active = False
        self._uninstall()
        self._step_frame = None
        self._step_codes = frozenset()
        self._mode = CONTINUE

    # --- called from the thread reading the socket ---

    def set_breakpoints(self, breakpoints):
        self._apply_breakpoints(breakpoints)
        # Lines switched off as not worth reporting may be worth it now.
        self._restart()

    def resume(self, action):
        """Let a stopped cell carry on."""
        with self._lock:
            self._action = action if action in (STEP_OVER, STEP_INTO, STEP_OUT) else CONTINUE
        self._resumed.set()

    def abandon(self):
        """Release a stopped cell when nobody is left to tell it how to carry on."""
        self.resume(CONTINUE)

    def _apply_breakpoints(self, breakpoints):
        cleaned = {}
        if isinstance(breakpoints, dict):
            for cell, lines in breakpoints.items():
                if not isinstance(lines, (list, tuple)):
                    continue
                wanted = frozenset(
                    line for line in lines
                    if isinstance(line, int) and not isinstance(line, bool) and line >= 1)
                if wanted:
                    cleaned[str(cell)] = wanted
        with self._lock:
            self._breakpoints = cleaned
            self._rebuild_lines()

    # --- deciding ---

    def _decide(self, frame, line):
        """
        Whether to stop at this line: the reason when so, ``None`` when not, and ``False`` when
        the line is also not worth being told about again until something changes.
        """
        code = frame.f_code
        if line in self._lines_of_file.get(code.co_filename, ()):
            return "breakpoint"

        mode = self._mode
        if mode == CONTINUE:
            return False
        if mode == STEP_INTO:
            return "step"

        step_frame = self._step_frame
        if frame is step_frame:
            return "step" if mode == STEP_OVER else None

        # A frame the step frame is still underneath is something it called, which a step over
        # or out runs straight through. Anything else means the step frame has returned.
        caller = frame.f_back
        while caller is not None:
            if caller is step_frame:
                # Code that is not also running somewhere up the stack cannot be where the
                # step ends, so its lines need not be reported for the rest of this step.
                return False if code not in self._step_codes else None
            caller = caller.f_back
        return "step"

    def _on_line(self, frame, line):
        """Handle one line of cell code. Returns ``False`` when the line can be switched off."""
        if not self._active or self._describing or threading.get_ident() != self._thread:
            return None

        reason = self._decide(frame, line)
        if not reason:
            return reason

        self._stop(frame, line, reason)
        return None

    def _stop(self, frame, line, reason):
        frames = []
        self._describing = True
        try:
            current = frame
            while current is not None:
                code = current.f_code
                if is_cell_code(code):
                    top_level = code.co_name == "<module>"
                    frames.append({
                        "name": "" if top_level else code.co_name,
                        "cell": self._cell_of_file.get(code.co_filename),
                        "line": line if current is frame else current.f_lineno,
                        "variables": describe_variables(current, top_level),
                    })
                current = current.f_back
        finally:
            self._describing = False

        self._resumed.clear()
        self._waiting = True
        try:
            self._flush()
            self._report(frames, reason)

            # Polled so that an interrupt reaches this thread while it waits. It surfaces here
            # as KeyboardInterrupt and unwinds the cell, which is what stopping a stopped cell
            # means.
            while not self._resumed.wait(WAIT_POLL_SECONDS):
                pass
        finally:
            self._waiting = False

        with self._lock:
            action = self._action

        self._mode = action
        if action in (STEP_OVER, STEP_OUT):
            self._step_frame = frame
            codes = set()
            current = frame
            while current is not None:
                codes.add(current.f_code)
                current = current.f_back
            self._step_codes = frozenset(codes)
        else:
            self._step_frame = None
            self._step_codes = frozenset()

        # Every line is a candidate again under the new instruction.
        self._restart()

    # --- asking the interpreter for lines ---

    def _install(self):
        if _USE_MONITORING:
            self._install_monitoring()
        else:
            sys.settrace(self._trace_call)

    def _uninstall(self):
        if _USE_MONITORING:
            self._uninstall_monitoring()
        else:
            sys.settrace(None)

    def _restart(self):
        if _USE_MONITORING and self._active:
            try:
                sys.monitoring.restart_events()
            except Exception:
                pass

    # sys.settrace, Python 3.8 to 3.11.

    def _trace_call(self, frame, event, arg):
        # Asked once per call. Library code is turned away here and runs untraced.
        if event == "call" and is_cell_code(frame.f_code):
            return self._trace_line
        return None

    def _trace_line(self, frame, event, arg):
        if event == "line":
            self._on_line(frame, frame.f_lineno)
        return self._trace_line

    # sys.monitoring, Python 3.12 and newer.

    def _install_monitoring(self):
        monitoring = sys.monitoring
        tool = monitoring.DEBUGGER_ID
        try:
            monitoring.use_tool_id(tool, "verso")
        except ValueError:
            # Something else is already debugging this process. Leave it to that tool rather
            # than fight over the slot; the cell runs without stopping.
            self._monitoring_tool = None
            return

        self._monitoring_tool = tool
        self._line_starts = {}
        monitoring.register_callback(tool, monitoring.events.LINE, self._monitor_line)
        monitoring.register_callback(tool, monitoring.events.JUMP, self._monitor_jump)
        monitoring.set_events(tool, monitoring.events.LINE | monitoring.events.JUMP)
        monitoring.restart_events()

    def _uninstall_monitoring(self):
        tool = self._monitoring_tool
        if tool is None:
            return
        monitoring = sys.monitoring
        try:
            monitoring.set_events(tool, 0)
            monitoring.register_callback(tool, monitoring.events.LINE, None)
            monitoring.register_callback(tool, monitoring.events.JUMP, None)
            monitoring.free_tool_id(tool)
        except Exception:
            pass
        self._monitoring_tool = None
        self._line_starts = {}

    def _monitor_line(self, code, line):
        if not is_cell_code(code):
            return sys.monitoring.DISABLE

        # The callback is given the code and the line but not the frame, which is the caller.
        if self._on_line(sys._getframe(1), line) is False:
            return sys.monitoring.DISABLE
        return None

    def _monitor_jump(self, code, source, destination):
        # A line is reported when execution arrives at it from a different one. A loop written
        # on one line goes round without doing that, so its second pass onwards would go
        # unreported, where sys.settrace reports each. A jump backwards that lands on the line
        # it left is such a pass beginning, and is handled as that line being reached again.
        if destination >= source or not is_cell_code(code):
            return sys.monitoring.DISABLE

        line = self._line_at(code, source)
        if line is None or line != self._line_at(code, destination):
            return sys.monitoring.DISABLE

        if self._on_line(sys._getframe(1), line) is False:
            return sys.monitoring.DISABLE
        return None

    def _line_at(self, code, offset):
        """The line an instruction belongs to, or ``None`` when it belongs to none."""
        starts = self._line_starts.get(code)
        if starts is None:
            pairs = sorted(dis.findlinestarts(code))
            starts = ([at for at, _ in pairs], [line for _, line in pairs])
            self._line_starts[code] = starts

        offsets, lines = starts
        at = bisect.bisect_right(offsets, offset) - 1
        return lines[at] if at >= 0 else None
