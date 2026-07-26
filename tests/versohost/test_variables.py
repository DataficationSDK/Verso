"""Coverage for reducing the cell namespace into something the shared store can hold."""

import sys

import _versohost_vars as variable_support


def test_plain_values_survive_the_round_trip():
    variables, oversized = variable_support.collect(
        {"count": 3, "ratio": 1.5, "name": "verso", "ready": True})

    assert variables == {"count": 3, "ratio": 1.5, "name": "verso", "ready": True}
    assert oversized == []


def test_nested_containers_are_reduced_recursively():
    variables, _ = variable_support.collect(
        {"rows": [{"city": "Tokyo", "population": 13960000}], "pairs": (1, 2)})

    assert variables["rows"] == [{"city": "Tokyo", "population": 13960000}]
    assert variables["pairs"] == [1, 2]


def test_private_names_modules_and_callables_are_left_behind():
    scope = {
        "_hidden": 1,
        "__dunder__": 2,
        "shown": 3,
        "module": sys,
        "function": len,
        "cls": Exception,
        "nothing": None,
    }

    variables, _ = variable_support.collect(scope)

    assert variables == {"shown": 3}


def test_a_cycle_is_pruned_rather_than_followed():
    cyclic = {"name": "loop"}
    cyclic["self"] = cyclic

    variables, _ = variable_support.collect({"cyclic": cyclic})

    assert variables["cyclic"]["name"] == "loop"
    assert variables["cyclic"]["self"] is None


def test_a_deep_but_acyclic_value_stops_at_the_depth_limit():
    deep = current = []
    for _ in range(MAX_TEST_DEPTH):
        nested = []
        current.append(nested)
        current = nested

    variables, oversized = variable_support.collect({"deep": deep})

    assert oversized == []
    assert isinstance(variables["deep"], list)


def test_non_finite_floats_travel_as_text():
    variables, _ = variable_support.collect(
        {"nan": float("nan"), "inf": float("inf"), "negative": float("-inf")})

    # JSON has no way to say these, and the bare tokens Python would write are not parseable
    # by the managing side.
    assert variables == {"nan": "nan", "inf": "inf", "negative": "-inf"}


def test_a_value_json_cannot_describe_publishes_the_way_it_prints():
    class Point:
        def __repr__(self):
            return "Point(1, 2)"

    variables, _ = variable_support.collect({"point": Point()})

    assert variables["point"] == "Point(1, 2)"


def test_non_string_keys_take_their_printed_form():
    variables, _ = variable_support.collect({"lookup": {1: "one", None: "none"}})

    assert variables["lookup"] == {"1": "one", "None": "none"}


def test_a_value_over_the_limit_publishes_a_description_instead():
    variables, oversized = variable_support.collect({"big": "x" * 500}, limit_bytes=64)

    assert oversized == ["big"]
    assert variables["big"] == "<str, over the 64 bytes limit, not shared>"


def test_the_run_stops_growing_once_the_total_budget_is_spent():
    scope = {"first": "x" * 200, "second": "y" * 200, "third": "z" * 200}

    variables, oversized = variable_support.collect(
        scope, limit_bytes=1024, total_limit_bytes=256)

    # The first value fits, and what follows is described rather than sent.
    assert len(oversized) >= 1
    assert all(variables[name].startswith("<str,") for name in oversized)


def test_inject_sets_names_into_the_scope():
    scope = {}

    variable_support.apply_inject(scope, {"greeting": "hello", "count": 2})

    assert scope == {"greeting": "hello", "count": 2}


def test_inject_leaves_interpreter_owned_names_alone():
    scope = {"__builtins__": "the real thing"}

    variable_support.apply_inject(
        scope,
        {
            "__builtins__": "clobbered",
            "__verso_python_interpreter": "/usr/bin/python3",
            "greeting": "hello",
        },
    )

    # Replacing __builtins__ would take away every builtin the cell has, and the session keys
    # Verso writes to the same store are not variables anyone asked for.
    assert scope["__builtins__"] == "the real thing"
    assert "__verso_python_interpreter" not in scope
    assert scope["greeting"] == "hello"


def test_inject_keeps_a_single_underscore_name():
    scope = {}

    # Other languages use a leading underscore for ordinary variables, so a C# cell binding
    # _row_count is sharing a value rather than hiding one.
    variable_support.apply_inject(scope, {"_row_count": 42})

    assert scope["_row_count"] == 42


def test_inject_ignores_a_payload_that_is_not_a_mapping():
    scope = {}

    variable_support.apply_inject(scope, None)
    variable_support.apply_inject(scope, ["not", "a", "mapping"])

    assert scope == {}


def test_byte_counts_read_the_way_a_person_would_write_them():
    assert variable_support.format_bytes(512) == "512 bytes"
    assert variable_support.format_bytes(1536) == "1.5 KB"
    assert variable_support.format_bytes(8 * 1024 * 1024) == "8 MB"


MAX_TEST_DEPTH = variable_support.MAX_DEPTH + 50
