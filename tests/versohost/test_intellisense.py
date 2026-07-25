"""Completions, hover, and diagnostics, with and without the analysis library."""

import os

import pytest

import _versohost_intel as intel

jedi_required = pytest.mark.skipif(
    not intel.jedi_available(), reason="jedi is not installed in this environment")


@pytest.fixture
def without_jedi(monkeypatch):
    """
    Force the standard-library paths. Pinning the recorded attempt as well as the module keeps
    the fallbacks under test even when jedi is installed alongside the suite.
    """
    monkeypatch.setattr(intel, "_jedi", None)
    monkeypatch.setattr(intel, "_jedi_attempted", True)
    monkeypatch.setattr(intel, "_tools_path", None)
    monkeypatch.setattr(intel, "_tools_stamp", None)


# --- position and context helpers ---


def test_offset_zero_is_the_start_of_the_first_line():
    assert intel.offset_to_line_column("abc", 0) == (0, 0)


def test_a_negative_offset_is_treated_as_the_start():
    assert intel.offset_to_line_column("abc", -5) == (0, 0)


def test_offset_counts_columns_within_a_line():
    assert intel.offset_to_line_column("abcdef", 4) == (0, 4)


def test_offset_past_a_newline_starts_the_next_line():
    assert intel.offset_to_line_column("ab\ncd", 4) == (1, 1)


def test_an_offset_beyond_the_text_stops_at_its_end():
    assert intel.offset_to_line_column("ab\ncd", 99) == (1, 2)


def test_no_history_leaves_the_code_and_offset_alone():
    assert intel.build_combined_source([], "x = 1") == ("x = 1", 0)


def test_history_precedes_the_code_and_reports_its_line_count():
    combined, prefix_lines = intel.build_combined_source(["a = 1", "b = 2"], "c = 3")

    assert combined == "a = 1\nb = 2\nc = 3"
    assert prefix_lines == 2


def test_a_multi_line_history_entry_counts_every_line():
    combined, prefix_lines = intel.build_combined_source(["a = 1\nb = 2"], "c = 3")

    assert combined == "a = 1\nb = 2\nc = 3"
    assert prefix_lines == 2


# --- standard-library fallbacks ---


def test_completions_come_from_the_standard_library_without_jedi(without_jedi):
    names = [item["name"] for item in intel.complete([], "le", 2, {})]

    assert "len" in names


def test_fallback_completions_report_dotted_names(without_jedi):
    names = [item["name"] for item in intel.complete([], "os.pa", 5, {"os": os})]

    assert "os.path" in names


def test_fallback_completions_leave_the_kind_for_the_caller_to_default(without_jedi):
    items = intel.complete([], "le", 2, {})

    assert items
    assert all(item["kind"] == "" for item in items)


def test_a_cursor_on_nothing_completes_nothing(without_jedi):
    assert intel.complete([], "x = ", 4, {}) == []


def test_hover_is_unavailable_without_jedi(without_jedi):
    assert intel.hover([], "len", 1, {}) is None


def test_diagnostics_come_from_the_compiler_without_jedi(without_jedi):
    found = intel.diagnostics([], "def f(:")

    assert len(found) == 1
    assert found[0]["line"] == 0
    assert found[0]["code"] == "SyntaxError"
    assert found[0]["message"]


def test_compiler_diagnostics_report_the_offending_line(without_jedi):
    found = intel.diagnostics([], "x = 1\ny = 2\ndef f(:")

    assert len(found) == 1
    assert found[0]["line"] == 2


def test_compiler_diagnostics_mark_at_least_one_character(without_jedi):
    found = intel.diagnostics([], "def f(:")

    assert found[0]["end_line"] >= found[0]["line"]
    assert found[0]["end_column"] > found[0]["column"]


def test_valid_code_has_no_compiler_diagnostics(without_jedi):
    assert intel.diagnostics([], "x = 1\nprint(x)\n") == []


def test_a_top_level_await_is_not_a_compiler_diagnostic(without_jedi):
    # Cells may await at the top level, so the diagnostic path has to compile the way the
    # execution path does.
    assert intel.diagnostics([], "import asyncio\nawait asyncio.sleep(0)") == []


def test_empty_code_has_no_compiler_diagnostics(without_jedi):
    assert intel.diagnostics([], "   \n  ") == []


# --- jedi paths ---


@jedi_required
def test_completions_report_module_members():
    names = [item["name"] for item in intel.complete([], "os.", 3, {"os": os})]

    assert "path" in names


@jedi_required
def test_completions_filter_by_what_has_been_typed():
    names = [item["name"] for item in intel.complete([], "os.pa", 5, {"os": os})]

    assert "path" in names


@jedi_required
def test_completions_carry_a_kind():
    items = intel.complete([], "os.", 3, {"os": os})

    assert items
    assert all(item["kind"] for item in items)


@jedi_required
def test_completions_see_a_name_defined_by_an_earlier_cell():
    names = [item["name"] for item in intel.complete(["value = 41"], "val", 3, {})]

    assert "value" in names


@jedi_required
def test_completions_see_a_value_in_the_live_namespace():
    names = [item["name"] for item in intel.complete([], "items.", 6, {"items": [1, 2, 3]})]

    assert "append" in names


@jedi_required
def test_hover_describes_a_builtin():
    info = intel.hover([], "len", 1, {})

    assert info is not None
    assert "len" in info["content"]


@jedi_required
def test_hover_on_whitespace_says_nothing():
    assert intel.hover([], "x = 1\n   ", 8, {}) is None


@jedi_required
def test_diagnostics_position_against_the_cell_not_the_context():
    found = intel.diagnostics(["a = 1", "b = 2"], "def f(:")

    assert len(found) == 1
    assert found[0]["line"] == 0


@jedi_required
def test_diagnostics_ignore_problems_in_the_replayed_context():
    # The earlier cell's own text is not this cell's problem to report.
    assert intel.diagnostics(["def broken(:"], "x = 1") == []


# --- request handling ---


def test_a_completion_reply_echoes_the_request_id(assist):
    reply = assist("complete", "le", request_id=7)

    assert reply["req_id"] == 7
    assert isinstance(reply["completions"], list)


def test_a_hover_reply_carries_null_when_nothing_is_known(assist):
    reply = assist("hover", "   ")

    assert reply["hover"] is None


def test_a_diagnostics_reply_lists_the_problems(assist):
    reply = assist("diagnostics", "def f(:")

    assert len(reply["diagnostics"]) == 1


def test_an_unknown_request_is_ignored(session, channel):
    session.answer_intellisense({"type": "nonsense", "id": 1})

    assert channel.messages == []


def test_requests_are_answered_as_empty_while_a_cell_runs(session, assist):
    session.executing = True

    assert assist("complete", "le")["completions"] == []
    assert assist("hover", "len", cursor=1)["hover"] is None
    assert assist("diagnostics", "def f(:")["diagnostics"] == []


def test_a_cell_that_ran_becomes_context(session, execute):
    execute("value = 41")

    assert list(session.history) == ["value = 41"]


def test_a_cell_that_raised_still_becomes_context(session, execute):
    execute("value = 41\nraise ValueError('boom')")

    assert len(session.history) == 1


def test_a_cell_that_does_not_parse_is_not_context(session, execute):
    execute("def f(:")

    assert list(session.history) == []


def test_a_cancelled_cell_is_not_context(session, execute):
    execute("raise KeyboardInterrupt()")

    assert list(session.history) == []


def test_the_executing_flag_is_clear_between_cells(session, execute):
    execute("value = 41")

    assert session.executing is False


@jedi_required
def test_context_can_be_declined_by_the_requester(session, assist):
    session.history.append("value = 41")

    with_context = assist("complete", "val")["completions"]
    without_context = assist("complete", "val", history=False)["completions"]

    assert any(item["name"] == "value" for item in with_context)
    assert not any(item["name"] == "value" for item in without_context)
