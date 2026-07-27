"""Completions, hover, and diagnostics for the cells this host runs.

Answers are produced in this process, so they reflect the interpreter the cells execute in
and the live cell namespace rather than a separate analysis environment. jedi does the work
when it is importable; without it the standard library still supplies usable completions and
syntax diagnostics. Depends only on the standard library and targets CPython 3.8 and newer.
"""

import ast
import importlib
import os
import sys
import threading

# Diagnostics compile under a fixed name: nothing looks the source up afterwards, and a
# stable name keeps the reported messages identical from one request to the next.
DIAGNOSTIC_FILENAME = "<cell>"

# The standard library's completer is a state machine with no count of its own, so the walk
# is bounded rather than trusted to end.
MAX_COMPLETION_STATES = 500

# Directory holding the analysis tools, provisioned separately from this script. Appended to
# the end of sys.path so nothing in it can stand in for a package the user installed.
_tools_path = None

# Import outcome for jedi, with the state of the tools directory at the time of a failed
# attempt. Retrying only when that state changes means a provision landing mid-session is
# picked up without paying for a full path scan on every keystroke.
_jedi = None
_jedi_attempted = False
_tools_stamp = None

# The import is warmed on a thread of its own while requests arrive on another, so a caller that
# finds the attempt already under way waits for it rather than reading a half-set outcome.
_import_lock = threading.Lock()


def configure(tools_path):
    """Record the tools directory and place it at the end of the import path."""
    global _tools_path

    if not tools_path or not isinstance(tools_path, str):
        return

    _tools_path = tools_path
    if tools_path not in sys.path:
        sys.path.append(tools_path)


def reset_cache():
    """Forget the recorded import outcome so the next request tries again."""
    global _jedi, _jedi_attempted, _tools_stamp

    _jedi = None
    _jedi_attempted = False
    _tools_stamp = None


def jedi_available():
    """Whether the richer analysis is available. Attempts the import if it has not yet run."""
    return load_jedi() is not None


def load_jedi():
    """Return the jedi module, or None when it is not importable."""
    global _jedi, _jedi_attempted, _tools_stamp

    if _jedi is not None:
        return _jedi

    with _import_lock:
        if _jedi is not None:
            return _jedi

        stamp = _tools_directory_stamp()
        if _jedi_attempted and stamp == _tools_stamp:
            return None

        _jedi_attempted = True
        _tools_stamp = stamp

        # A directory that gained its contents after this process started is invisible to the
        # finders until they forget what they saw on the path before it was populated.
        importlib.invalidate_caches()

        try:
            _jedi = importlib.import_module("jedi")
        except Exception:
            _jedi = None

        return _jedi


def _tools_directory_stamp():
    if not _tools_path:
        return None
    try:
        return os.stat(_tools_path).st_mtime
    except OSError:
        return None


# --- position and context helpers ---


def offset_to_line_column(text, offset):
    """Turn a 0-based character offset into a 0-based line and column."""
    if offset <= 0:
        return 0, 0

    line = 0
    column = 0
    for character in text[:offset]:
        if character == "\n":
            line += 1
            column = 0
        else:
            column += 1

    return line, column


def build_combined_source(history, code):
    """
    Put the sources of earlier cells ahead of the current one so a name defined in one cell
    is in context for the next. Returns the combined text and the number of lines that
    precede the current code, which is what maps a reported position back to the cell.
    """
    if not history:
        return code, 0

    prefix = "\n".join(history) + "\n"
    return prefix + code, prefix.count("\n")


def _is_identifier_or_dot(character):
    return character.isalnum() or character == "_" or character == "."


def _docstring(item):
    try:
        text = item.docstring()
    except Exception:
        return ""
    return text or ""


def _first_line(text):
    if not text:
        return ""
    return text.split("\n")[0].strip()


# --- completions ---


def complete(history, code, cursor, namespace):
    """Completion candidates at a cursor, as plain dictionaries the reply carries as they are."""
    code = code or ""
    jedi = load_jedi()
    if jedi is None:
        return _stdlib_completions(code, cursor, namespace)

    try:
        combined, prefix_lines = build_combined_source(history, code)
        line, column = offset_to_line_column(code, cursor)

        interpreter = jedi.Interpreter(combined, namespaces=[dict(namespace)])

        # jedi counts lines from one and columns from zero.
        candidates = interpreter.complete(prefix_lines + line + 1, column)

        results = []
        for item in candidates:
            name = getattr(item, "name", "") or ""
            if not name:
                continue
            results.append({
                "name": name,
                "insert": name,
                "kind": getattr(item, "type", "") or "",
                "doc": _first_line(_docstring(item)),
            })

        return results
    except Exception:
        return _stdlib_completions(code, cursor, namespace)


def _stdlib_completions(code, cursor, namespace):
    """
    Completions from the standard library's own completer, driven by the text immediately
    before the cursor. It reports full dotted names where jedi reports the trailing segment.
    """
    try:
        import rlcompleter
    except Exception:
        return []

    position = min(max(cursor, 0), len(code))
    start = position
    while start > 0 and _is_identifier_or_dot(code[start - 1]):
        start -= 1

    token = code[start:position]
    if not token:
        return []

    try:
        completer = rlcompleter.Completer(dict(namespace))
    except Exception:
        return []

    results = []
    for state in range(MAX_COMPLETION_STATES):
        try:
            match = completer.complete(token, state)
        except Exception:
            break

        if not match:
            break

        # The completer appends an opening parenthesis to callables; the editors insert the
        # name and leave the call to the user.
        name = match.rstrip("(")
        results.append({"name": name, "insert": name, "kind": "", "doc": ""})

    return results


# --- hover ---


def hover(history, code, cursor, namespace):
    """
    What the name under the cursor refers to, or None when nothing can be said about it.
    Only the richer analysis can answer this, so there is no standard-library fallback.
    """
    code = code or ""
    jedi = load_jedi()
    if jedi is None:
        return None

    try:
        combined, prefix_lines = build_combined_source(history, code)
        line, column = offset_to_line_column(code, cursor)

        interpreter = jedi.Interpreter(combined, namespaces=[dict(namespace)])
        names = interpreter.infer(prefix_lines + line + 1, column)
        if not names:
            return None

        first = names[0]
        signature = getattr(first, "full_name", "") or getattr(first, "type", "") or ""
        docstring = _docstring(first)

        content = "%s\n\n%s" % (signature, docstring) if docstring else signature
        if not content:
            return None

        return {"content": content}
    except Exception:
        return None


# --- diagnostics ---


def diagnostics(history, code):
    """
    Syntax problems in the current cell, positioned against the cell rather than the
    replayed context. Positions are 0-based on both axes, which is what the editors use.
    """
    code = code or ""
    jedi = load_jedi()
    if jedi is None:
        return _compile_diagnostics(code)

    try:
        combined, prefix_lines = build_combined_source(history, code)
        errors = jedi.Script(combined).get_syntax_errors()
    except Exception:
        return _compile_diagnostics(code)

    results = []
    for error in errors:
        try:
            mapped = _map_syntax_error(error, prefix_lines)
        except Exception:
            continue
        if mapped is not None:
            results.append(mapped)

    return results


def _map_syntax_error(error, prefix_lines):
    line = getattr(error, "line", 0) - 1 - prefix_lines

    # A problem inside the replayed context belongs to an earlier cell, not this one.
    if line < 0:
        return None

    column = getattr(error, "column", 0)

    try:
        message = error.get_message()
    except Exception:
        message = ""

    end_line = line
    end_column = column + 1

    until_line = getattr(error, "until_line", None)
    until_column = getattr(error, "until_column", None)
    if isinstance(until_line, int) and isinstance(until_column, int):
        end_line = max(until_line - 1 - prefix_lines, line)
        end_column = until_column

    return {
        "line": line,
        "column": column,
        "end_line": end_line,
        "end_column": end_column,
        "message": message or "Syntax error",
        "code": "SyntaxError",
    }


def _compile_diagnostics(code):
    """
    Syntax diagnostics from the compiler. The current cell is compiled on its own, because
    the context of earlier cells cannot change whether this one parses.
    """
    if not code.strip():
        return []

    try:
        # The same flag the execution path compiles with, so a cell awaiting at the top level
        # is not reported as a mistake.
        compile(code, DIAGNOSTIC_FILENAME, "exec", ast.PyCF_ALLOW_TOP_LEVEL_AWAIT)
        return []
    except SyntaxError as exc:
        return [_compile_error_payload(exc)]
    except Exception:
        # A cell that upsets the compiler in some other way is left unannotated; running it
        # is what will report the real problem.
        return []


def _compile_error_payload(exc):
    line = max((exc.lineno or 1) - 1, 0)
    column = max((exc.offset or 1) - 1, 0)

    end_line = line
    end_column = column + 1

    raw_end_line = getattr(exc, "end_lineno", None)
    raw_end_column = getattr(exc, "end_offset", None)
    if isinstance(raw_end_line, int) and raw_end_line > 0:
        end_line = max(raw_end_line - 1, line)
    if isinstance(raw_end_column, int) and raw_end_column > 0:
        end_column = raw_end_column - 1

    # An empty range would leave nothing for an editor to mark.
    if end_line == line and end_column <= column:
        end_column = column + 1

    return {
        "line": line,
        "column": column,
        "end_line": end_line,
        "end_column": end_column,
        "message": getattr(exc, "msg", None) or "Syntax error",
        "code": "SyntaxError",
    }
