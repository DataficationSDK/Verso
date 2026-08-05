"""Error reporting: status selection and traceback shaping."""

import threading

import versohost


def test_a_syntax_error_reports_the_caret(execute):
    result = execute("def (:")

    assert result["status"] == "error"
    assert result["error"]["name"] == "SyntaxError"
    assert "^" in result["error"]["traceback"]
    assert result["result"] is None


def test_a_syntax_error_runs_nothing(execute):
    execute("marker = 'untouched'\ndef (:")

    # A cell is compiled as a whole, so a syntax error anywhere means no part of it ran.
    assert execute("'marker' in dir()")["result"]["data"] == "False"


def test_a_runtime_error_carries_the_exception_type(execute):
    result = execute("raise ValueError('nope')")

    assert result["status"] == "error"
    assert result["error"]["name"] == "ValueError"
    assert result["error"]["message"] == "nope"
    assert "ValueError: nope" in result["error"]["traceback"]


def test_a_missing_module_is_named_on_the_error(execute):
    # A dynamic import is invisible to a pre-execution scan, so the failure is the only place
    # the module can be named for the install path to act on.
    result = execute("import verso_absent_module_for_tests")

    assert result["error"]["name"] == "ModuleNotFoundError"
    assert result["error"]["missing_module"] == "verso_absent_module_for_tests"


def test_a_missing_submodule_names_the_module_it_failed_on(execute):
    result = execute("import verso_absent_module_for_tests.inner")

    assert result["error"]["missing_module"].startswith("verso_absent_module_for_tests")


def test_a_submodule_of_a_package_that_is_present_is_not_offered_for_install(execute):
    # The package is installed and has no such submodule, so this is a mistake in the import
    # rather than something an install could satisfy. Reporting it would offer to install the
    # package that is already there, and the cell would fail the same way afterwards.
    result = execute("from json.decoder2 import thing")

    assert result["error"]["name"] == "ModuleNotFoundError"
    assert "missing_module" not in result["error"]


def test_a_missing_name_inside_a_real_module_is_not_a_missing_module(execute):
    # ImportError rather than ModuleNotFoundError: the module is there and the name is not,
    # which no install fixes.
    result = execute("from os import definitely_not_a_real_attribute")

    assert result["error"]["name"] == "ImportError"
    assert "missing_module" not in result["error"]


def test_a_traceback_starts_at_the_user_frame(execute):
    result = execute("def boom():\n    raise ValueError('nope')\nboom()")

    traceback = result["error"]["traceback"]
    assert "in boom" in traceback

    # The frames that compile and run the cell are the harness, not the user's program.
    assert "versohost.py" not in traceback
    assert "eval(" not in traceback


def test_a_traceback_shows_the_offending_source_line(execute):
    result = execute("value = 1\nvalue.missing_attribute")

    # The cell text is registered so the reported frame renders the line, as a file-backed one would.
    assert "value.missing_attribute" in result["error"]["traceback"]


def test_an_error_deep_in_a_library_keeps_the_library_frames(execute):
    result = execute("import json\njson.loads('{oops}')")

    traceback = result["error"]["traceback"]
    assert "json" in traceback
    assert "versohost.py" not in traceback


def test_an_interrupt_reports_a_cancelled_status(execute):
    result = execute("raise KeyboardInterrupt()")

    # Cancellation is not a failure of the cell, so it carries no error payload.
    assert result["status"] == "cancelled"
    assert result["error"] is None


def test_an_interrupt_inside_a_call_still_cancels(execute):
    result = execute("def stop():\n    raise KeyboardInterrupt()\nstop()")

    assert result["status"] == "cancelled"


def test_an_interrupt_message_is_raised_as_it_arrives(session, monkeypatch):
    """
    The reader raises an interrupt itself rather than queueing it.

    While a cell runs, the main thread is inside user code and not reading the queue, so a
    queued interrupt would only be seen once the cell it was meant to stop had ended. Where
    the managing process cannot deliver a signal, this message is the only way to stop a cell.
    """
    raised = threading.Event()
    monkeypatch.setattr(versohost._thread, "interrupt_main", raised.set)

    versohost._run(ScriptedChannel([{"type": "interrupt"}, {"type": "shutdown"}]), session)

    assert raised.is_set()


class ScriptedChannel:
    """Hands the reader a fixed run of messages, then reports the stream as ended."""

    def __init__(self, messages):
        self._messages = list(messages)
        self.written = []

    def read(self):
        return self._messages.pop(0) if self._messages else None

    def write(self, message):
        self.written.append(message)


def test_exit_ends_the_cell_rather_than_the_host(execute):
    result = execute("import sys\nsys.exit(3)")

    assert result["status"] == "error"
    assert result["error"]["name"] == "SystemExit"

    # The interpreter is still serving requests.
    assert execute("'alive'")["result"]["data"] == "'alive'"


def test_output_written_before_a_failure_is_kept(session, channel):
    writer = versohost.StreamWriter(session.router, "stdout")
    session.scope["out"] = writer

    session.execute({"type": "execute", "id": 1, "code": "out.write('progress')\nraise ValueError('x')"})

    assert "progress" in channel.stream_text()
    assert channel.of_type("execute_result")[-1]["status"] == "error"


def test_the_result_id_matches_the_request(execute, channel):
    execute("1", request_id=17)

    assert channel.of_type("execute_result")[-1]["req_id"] == 17
