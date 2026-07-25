"""Coverage for the notebook shorthands a cell may carry."""

import builtins
import contextlib
import sys

import versohost


@contextlib.contextmanager
def shell_builtins(session):
    """
    Wire just enough of install() for a cell to reach the shell helpers.

    Done inside the test rather than in a fixture because pytest reinstates its own
    ``sys.stdout`` between fixture setup and the test body, which would undo the redirect.
    """
    saved = (sys.stdout, sys.stderr)
    sys.stdout = versohost.StreamWriter(session.router, "stdout")
    sys.stderr = versohost.StreamWriter(session.router, "stderr")
    builtins.__verso_shell__ = session._shell
    builtins.__verso_pip__ = session._pip
    try:
        yield session
    finally:
        sys.stdout, sys.stderr = saved
        del builtins.__verso_shell__
        del builtins.__verso_pip__


def test_a_bang_line_becomes_a_shell_call():
    transformed = versohost.transform_escapes("!echo hello\nprint('after')\n")

    assert transformed == "__verso_shell__('echo hello')\nprint('after')\n"


def test_indentation_is_preserved_so_the_command_still_composes():
    transformed = versohost.transform_escapes("for _ in range(2):\n    !echo twice\n")

    assert transformed == "for _ in range(2):\n    __verso_shell__('echo twice')\n"


def test_a_percent_pip_line_becomes_a_pip_call():
    transformed = versohost.transform_escapes("%pip install rich\n")

    assert transformed == "__verso_pip__('install rich')\n"


def test_source_that_already_parses_is_left_exactly_as_it_is():
    # The bang here is inside a string, so the cell is valid Python and nothing is rewritten.
    source = "message = '''\n!not a command\n'''\nprint(message)\n"

    assert versohost.transform_escapes(source) == source


def test_a_syntax_error_without_shorthands_is_left_alone():
    source = "def (:\n"

    assert versohost.transform_escapes(source) == source


def test_windows_line_endings_survive_the_rewrite():
    transformed = versohost.transform_escapes("!echo hello\r\nprint(1)\r\n")

    assert transformed == "__verso_shell__('echo hello')\r\nprint(1)\r\n"


def test_a_bang_line_runs_and_streams_its_output(session, channel):
    with shell_builtins(session):
        session.execute({"type": "execute", "id": 1, "code": "!echo streamed"})

    assert "streamed" in channel.stream_text("stdout")
    assert channel.of_type("execute_result")[-1]["status"] == "ok"


def test_a_failing_command_reports_its_exit_code(session, channel):
    with shell_builtins(session):
        session.execute({"type": "execute", "id": 1, "code": "!exit 3"})

    assert "exited with code 3" in channel.stream_text("stderr")


def test_shorthands_are_left_alone_when_they_are_turned_off(session, channel):
    session.shell_escapes = False

    session.execute({"type": "execute", "id": 1, "code": "!echo untouched"})

    result = channel.of_type("execute_result")[-1]
    assert result["status"] == "error"
    assert result["error"]["name"] == "SyntaxError"
