"""
Covers stopping a running cell at a line and stepping through it.

Every test runs against both ways the host is told about lines: ``sys.monitoring`` where the
interpreter has it, and ``sys.settrace`` everywhere. The second is forced rather than left to
whichever interpreter the suite happens to run on, so the path the oldest supported Python
takes is exercised on the newest one too.
"""

import sys

import pytest

import _versohost_debug as debug_support

CELL_A = "11111111-1111-1111-1111-111111111111"
CELL_B = "22222222-2222-2222-2222-222222222222"

SQUARE = "def square(n):\n    result = n * n\n    return result"


@pytest.fixture(params=["monitoring", "settrace"])
def backend(request, monkeypatch):
    if request.param == "monitoring":
        if not hasattr(sys, "monitoring"):
            pytest.skip("sys.monitoring arrived in Python 3.12")
    else:
        monkeypatch.setattr(debug_support, "_USE_MONITORING", False)
    return request.param


@pytest.fixture
def debug(backend, session, channel):
    """
    Run a cell that stops at breakpoints, answering each stop from a list of instructions.

    An instruction is an action name, or a callable given the stop message that returns one,
    for a test that has something to do while the cell is held. A stop with no instruction
    left for it is told to continue. Returns the cell's result and the stops it made.
    """
    def run(code, cell=CELL_A, breakpoints=None, answers=(), debugged=True):
        run.counter += 1
        remaining = list(answers)
        stops = []
        write = channel.write

        def answering(message):
            write(message)
            if message.get("type") != "debug_stopped":
                return
            stops.append(message)
            answer = remaining.pop(0) if remaining else debug_support.CONTINUE
            if callable(answer):
                answer = answer(message)
            session.debugger.resume(answer)

        channel.write = answering
        try:
            request = {"type": "execute", "id": run.counter, "code": code, "cell": cell}
            if debugged:
                request["debug"] = {"breakpoints": breakpoints or {}}
            session.execute(request)
        finally:
            channel.write = write

        return channel.of_type("execute_result")[-1], stops

    run.counter = 100
    return run


def where(stop):
    """The innermost frame of a stop as ``(cell, line)``."""
    return stop["frames"][0]["cell"], stop["frames"][0]["line"]


def variables(frame):
    return {entry["name"]: entry["value"] for entry in frame["variables"]}


# --- breakpoints ---

def test_a_breakpoint_stops_the_cell_at_its_line(debug):
    result, stops = debug("x = 1\ny = x + 1\nz = y * 2\nz", breakpoints={CELL_A: [3]})

    assert result["status"] == "ok"
    assert result["result"]["data"] == "4"
    assert len(stops) == 1
    assert stops[0]["reason"] == "breakpoint"
    assert where(stops[0]) == (CELL_A, 3)


def test_a_stop_reports_what_is_in_scope_before_the_line_runs(debug):
    _, stops = debug("x = 1\ny = x + 1\nz = y * 2", breakpoints={CELL_A: [3]})

    frame = stops[0]["frames"][0]
    assert frame["name"] == ""
    assert variables(frame) == {"x": "1", "y": "2"}
    assert {entry["name"]: entry["type"] for entry in frame["variables"]} == {"x": "int", "y": "int"}


def test_the_cell_namespace_is_listed_without_its_imports_and_functions(debug):
    code = "import json\ndef helper():\n    pass\nclass Thing:\n    pass\nvalue = 5\nvalue"

    _, stops = debug(code, breakpoints={CELL_A: [7]})

    assert variables(stops[0]["frames"][0]) == {"value": "5"}


def test_a_cell_with_no_breakpoints_runs_straight_through(debug):
    result, stops = debug("total = 0\nfor i in range(1000):\n    total += i\ntotal")

    assert stops == []
    assert result["result"]["data"] == "499500"


def test_a_cell_that_was_not_asked_to_debug_ignores_breakpoints(debug, session):
    session.debugger.set_breakpoints({CELL_A: [1]})

    result, stops = debug("x = 1\nx", debugged=False)

    assert stops == []
    assert result["result"]["data"] == "1"


def test_a_breakpoint_on_a_line_with_no_statement_stops_at_the_next_one(debug):
    code = "x = 1\n\n# explains the next line\ny = 2\nz = 3"

    _, stops = debug(code, breakpoints={CELL_A: [2, 3]})

    assert [where(stop)[1] for stop in stops] == [4]


def test_a_breakpoint_past_the_last_statement_never_stops(debug):
    _, stops = debug("x = 1\n\n", breakpoints={CELL_A: [3]})

    assert stops == []


def test_a_breakpoint_hit_every_time_round_a_loop(debug):
    _, stops = debug("total = 0\nfor i in range(3):\n    total += i", breakpoints={CELL_A: [3]})

    assert [variables(stop["frames"][0])["i"] for stop in stops] == ["0", "1", "2"]


def test_a_breakpoint_on_a_loop_written_on_one_line_stops_on_every_pass(debug):
    # The loop never arrives at its line from a different one after the first time, which is
    # the only thing sys.monitoring reports as a line. Both ways of being told have to agree.
    result, stops = debug("total = 0\nfor i in range(3): total += i\ntotal", breakpoints={CELL_A: [2]})

    assert result["result"]["data"] == "3"
    assert [where(stop) for stop in stops] == [(CELL_A, 2)] * 4
    assert [variables(stop["frames"][0])["total"] for stop in stops] == ["0", "0", "1", "3"]


def test_a_step_over_on_a_loop_written_on_one_line_goes_round_once(debug):
    _, stops = debug(
        "total = 0\nfor i in range(2): total += i\ntotal",
        breakpoints={CELL_A: [1]},
        answers=[debug_support.STEP_OVER] * 5)

    assert [where(stop)[1] for stop in stops] == [1, 2, 2, 2, 3]


def test_a_breakpoint_in_a_function_an_earlier_cell_defined(debug):
    # The defining cell ran without debugging, which is the ordinary order of things: the
    # reader turns debugging on once something has already gone wrong.
    debug(SQUARE, cell=CELL_A, debugged=False)

    _, stops = debug("square(4)", cell=CELL_B, breakpoints={CELL_A: [2]})

    assert len(stops) == 1
    assert where(stops[0]) == (CELL_A, 2)
    assert [frame["name"] for frame in stops[0]["frames"]] == ["square", ""]
    assert where({"frames": stops[0]["frames"][1:]}) == (CELL_B, 1)
    assert variables(stops[0]["frames"][0]) == {"n": "4"}


def test_only_the_latest_run_of_a_cell_carries_its_breakpoints(debug):
    # The first run's function is still callable under another name, but its lines are no
    # longer the lines the reader is looking at.
    debug("def old():\n    return 1\nkeep = old", cell=CELL_A, debugged=False)
    debug("\n\ndef new():\n    return 2", cell=CELL_A, debugged=False)

    _, stops = debug("keep()\nnew()", cell=CELL_B, breakpoints={CELL_A: [2, 4]})

    assert [where(stop) for stop in stops] == [(CELL_A, 4)]


def test_breakpoints_changed_while_the_cell_runs_take_effect(debug, session):
    def add_another(stop):
        session.debugger.set_breakpoints({CELL_A: [4]})
        return debug_support.CONTINUE

    code = "a = 1\nfor i in range(2):\n    b = i\n    c = i"

    _, stops = debug(code, breakpoints={CELL_A: [1]}, answers=[add_another])

    assert [where(stop)[1] for stop in stops] == [1, 4, 4]


# --- stepping ---

def test_step_over_runs_through_a_call(debug):
    debug(SQUARE, cell=CELL_A, debugged=False)

    _, stops = debug(
        "a = square(3)\nb = a + 1\nb", cell=CELL_B,
        breakpoints={CELL_B: [1]}, answers=[debug_support.STEP_OVER])

    assert [where(stop) for stop in stops] == [(CELL_B, 1), (CELL_B, 2)]
    assert [stop["reason"] for stop in stops] == ["breakpoint", "step"]
    assert variables(stops[1]["frames"][0])["a"] == "9"


def test_step_into_enters_the_function_the_line_calls(debug):
    debug(SQUARE, cell=CELL_A, debugged=False)

    _, stops = debug(
        "a = square(3)\nb = a + 1", cell=CELL_B,
        breakpoints={CELL_B: [1]}, answers=[debug_support.STEP_INTO])

    assert [where(stop) for stop in stops] == [(CELL_B, 1), (CELL_A, 2)]
    assert [frame["name"] for frame in stops[1]["frames"]] == ["square", ""]


def test_step_out_runs_to_the_caller(debug):
    debug(SQUARE, cell=CELL_A, debugged=False)

    _, stops = debug(
        "a = square(3)\nb = a + 1", cell=CELL_B,
        breakpoints={CELL_A: [2]}, answers=[debug_support.STEP_OUT])

    assert [where(stop) for stop in stops] == [(CELL_A, 2), (CELL_B, 2)]
    assert stops[1]["reason"] == "step"


def test_step_over_still_stops_at_a_breakpoint_inside_the_call(debug):
    debug(SQUARE, cell=CELL_A, debugged=False)

    _, stops = debug(
        "a = square(3)\nb = a + 1", cell=CELL_B,
        breakpoints={CELL_B: [1], CELL_A: [3]}, answers=[debug_support.STEP_OVER])

    assert [where(stop) for stop in stops] == [(CELL_B, 1), (CELL_A, 3)]
    assert stops[1]["reason"] == "breakpoint"


def test_step_into_does_not_stop_in_library_code(debug):
    code = "import json\ntext = json.dumps({'a': 1})\nlength = len(text)"

    _, stops = debug(code, breakpoints={CELL_A: [2]}, answers=[debug_support.STEP_INTO])

    assert [where(stop) for stop in stops] == [(CELL_A, 2), (CELL_A, 3)]


def test_step_into_reaches_cell_code_called_back_from_library_code(debug):
    code = "def key(item):\n    return -item\nordered = sorted([1, 2], key=key)"

    _, stops = debug(code, breakpoints={CELL_A: [3]}, answers=[debug_support.STEP_INTO])

    assert [where(stop) for stop in stops] == [(CELL_A, 3), (CELL_A, 2)]
    assert stops[1]["frames"][0]["name"] == "key"


def test_stepping_off_the_end_of_the_cell_finishes_it(debug):
    result, stops = debug(
        "x = 1\nx + 1", breakpoints={CELL_A: [1]},
        answers=[debug_support.STEP_OVER, debug_support.STEP_OVER])

    assert [where(stop)[1] for stop in stops] == [1, 2]
    assert result["result"]["data"] == "2"


def test_a_step_left_unfinished_does_not_carry_into_the_next_cell(debug):
    debug("x = 1", breakpoints={CELL_A: [1]}, answers=[debug_support.STEP_INTO])

    _, stops = debug("y = 2\nz = 3", cell=CELL_B)

    assert stops == []


# --- other shapes of cell ---

def test_a_cell_with_top_level_await_stops_and_steps(debug):
    code = "import asyncio\nawait asyncio.sleep(0)\nvalue = 5\nvalue + 1"

    result, stops = debug(code, breakpoints={CELL_A: [3]}, answers=[debug_support.STEP_OVER])

    assert [where(stop)[1] for stop in stops] == [3, 4]
    assert result["result"]["data"] == "6"


def test_output_printed_before_a_stop_is_sent_before_it(debug, session, channel):
    # Written to the router directly, which is where the cell's print lands once the host
    # has redirected the standard streams. The fixture leaves the real ones in place.
    session.scope["emit"] = lambda: session.router.write("stdout", "before")

    debug("emit()\nx = 1", breakpoints={CELL_A: [2]})

    kinds = [m["type"] for m in channel.messages if m["type"] in ("stream", "debug_stopped")]
    assert kinds == ["stream", "debug_stopped"]


def test_an_interrupt_while_stopped_cancels_the_cell(debug):
    def interrupt(stop):
        raise KeyboardInterrupt()

    result, stops = debug("x = 1\ny = 2", breakpoints={CELL_A: [1]}, answers=[interrupt])

    assert len(stops) == 1
    assert result["status"] == "cancelled"

    # The session is left able to run, and to debug, the next cell.
    result, stops = debug("z = 3\nz", cell=CELL_B, breakpoints={CELL_B: [2]})
    assert result["result"]["data"] == "3"
    assert len(stops) == 1


def test_an_interrupt_while_a_stop_is_being_described_cancels_the_cell(debug):
    # The interrupt lands inside a value's repr, before the stop has been sent. Taking it for a
    # repr that failed would report the stop to a side that has already given up on the cell.
    code = (
        "class Slow(object):\n"
        "    def __repr__(self):\n"
        "        raise KeyboardInterrupt()\n"
        "item = Slow()\n"
        "done = True"
    )

    result, stops = debug(code, breakpoints={CELL_A: [5]})

    assert stops == []
    assert result["status"] == "cancelled"

    # The session is left able to debug the next cell, once the value that interrupts whoever
    # looks at it is out of the way.
    result, stops = debug("del item\nz = 3\nz", cell=CELL_B, breakpoints={CELL_B: [3]})
    assert result["result"]["data"] == "3"
    assert len(stops) == 1


def test_an_error_in_a_debugged_cell_is_reported_as_usual(debug):
    result, stops = debug("x = 1\nraise ValueError('no')", breakpoints={CELL_A: [1]})

    assert len(stops) == 1
    assert result["status"] == "error"
    assert result["error"]["name"] == "ValueError"


# --- describing values ---

def test_a_long_value_is_cut_short():
    assert len(debug_support.describe_value("x" * 5000)) <= debug_support.MAX_VALUE_CHARS
    assert len(debug_support.describe_value(list(range(100000)))) <= debug_support.MAX_VALUE_CHARS


def test_a_value_whose_repr_raises_is_still_described():
    class Awkward(object):
        def __repr__(self):
            raise RuntimeError("no")

    assert "Awkward" in debug_support.describe_value(Awkward())


def test_an_interrupt_is_not_taken_for_a_repr_that_failed():
    class Interrupted(object):
        def __repr__(self):
            raise KeyboardInterrupt()

    with pytest.raises(KeyboardInterrupt):
        debug_support.describe_value(Interrupted())


def test_a_very_large_collection_is_shown_from_its_first_few_without_building_its_repr():
    class Counted(dict):
        def __repr__(self):
            raise AssertionError("the whole repr was asked for")

    class Tagged(set):
        def __repr__(self):
            raise AssertionError("the whole repr was asked for")

    many = debug_support.LARGE_COLLECTION + 1

    text = debug_support.describe_value(Counted((n, n * n) for n in range(many)))
    assert text.startswith("Counted({0: 0, 1: 1, 2: 4, ")
    assert text.endswith(", ...})")

    text = debug_support.describe_value(dict((n, n) for n in range(many)))
    assert text.startswith("{0: 0, 1: 1, ")
    assert text.endswith(", ...}")

    text = debug_support.describe_value(Tagged(range(many)))
    assert text.startswith("Tagged({")
    assert text.endswith(", ...})")

    text = debug_support.describe_value(b"x" * 100000)
    assert text.startswith("b'xxxx")
    assert text.endswith("...")
    assert len(text) <= debug_support.MAX_VALUE_CHARS


def test_a_small_collection_is_described_as_before():
    from collections import OrderedDict

    assert debug_support.describe_value({"b": 2, "a": 1}) == "{'a': 1, 'b': 2}"
    assert debug_support.describe_value(OrderedDict(a=1)) == repr(OrderedDict(a=1))
    assert debug_support.describe_value(b"abc") == "b'abc'"


def test_describing_a_value_does_not_stop_in_its_repr(debug):
    code = (
        "class Loud(object):\n"
        "    def __repr__(self):\n"
        "        return 'loud'\n"
        "item = Loud()\n"
        "done = True"
    )

    _, stops = debug(code, breakpoints={CELL_A: [3, 5]})

    assert [where(stop)[1] for stop in stops] == [5]
    assert variables(stops[0]["frames"][0])["item"] == "loud"
