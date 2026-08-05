"""Import discovery and declared-requirement checks."""

import importlib
import importlib.metadata as metadata
import sys

import pytest

import _versohost_scan as scan_support
import versohost

# A name no index could plausibly serve, so its absence is not environment dependent.
ABSENT = "verso_absent_module_for_tests"
ABSENT_DIST = "verso-absent-dist-for-tests"


def entries(found):
    """The missing list, whether it came from the module directly or from a reply."""
    return found["missing"] if isinstance(found, dict) else found


def modules(found):
    return [entry["module"] for entry in entries(found)]


def optional_flags(found):
    return {entry["module"]: entry["optional"] for entry in entries(found)}


# --- what the walk finds ---


def test_a_present_module_is_not_reported():
    assert scan_support.scan("import os, json") == []


def test_a_missing_module_is_reported():
    assert scan_support.scan("import " + ABSENT) == [{"module": ABSENT, "optional": False}]


def test_a_from_import_reports_the_root():
    found = scan_support.scan("from %s.sub.deeper import thing" % ABSENT)

    assert found == [{"module": ABSENT, "optional": False}]


def test_an_alias_does_not_change_the_module_name():
    found = scan_support.scan("import %s as shorthand" % ABSENT)

    assert found == [{"module": ABSENT, "optional": False}]


def test_an_import_inside_a_function_is_found():
    # The function may never be called, but the dependency is real either way, and a lazily
    # imported module is exactly the case a scan is worth running for.
    found = scan_support.scan("def build():\n    import %s\n    return 1" % ABSENT)

    assert found == [{"module": ABSENT, "optional": False}]


def test_an_import_inside_a_class_body_is_found():
    found = scan_support.scan("class Holder:\n    import %s" % ABSENT)

    assert found == [{"module": ABSENT, "optional": False}]


def test_a_relative_import_is_skipped():
    assert scan_support.scan("from . import sibling") == []
    assert scan_support.scan("from .. import cousin") == []


def test_future_imports_are_skipped():
    assert scan_support.scan("from __future__ import annotations") == []


def test_each_module_is_reported_once():
    code = "import %s\nimport %s\nfrom %s import thing" % (ABSENT, ABSENT, ABSENT)

    assert modules(scan_support.scan(code)) == [ABSENT]


def test_results_are_ordered_so_a_reply_is_stable():
    other = ABSENT + "_second"
    code = "import %s\nimport %s" % (other, ABSENT)

    assert modules(scan_support.scan(code)) == sorted([ABSENT, other])


# --- guarded imports ---


def test_a_try_wrapped_import_is_optional():
    code = "try:\n    import %s\nexcept ImportError:\n    pass" % ABSENT

    assert optional_flags(scan_support.scan(code)) == {ABSENT: True}


def test_one_unguarded_import_makes_the_module_required():
    code = (
        "try:\n    import %s\nexcept ImportError:\n    pass\nimport %s" % (ABSENT, ABSENT)
    )

    assert optional_flags(scan_support.scan(code)) == {ABSENT: False}


def test_an_import_in_an_except_clause_is_not_guarded():
    # The fallback branch runs unconditionally once it is reached, so it is a hard requirement.
    code = "try:\n    pass\nexcept Exception:\n    import %s" % ABSENT

    assert optional_flags(scan_support.scan(code)) == {ABSENT: False}


def test_an_import_in_a_finally_clause_is_not_guarded():
    code = "try:\n    pass\nfinally:\n    import %s" % ABSENT

    assert optional_flags(scan_support.scan(code)) == {ABSENT: False}


# --- code the scan cannot read ---


def test_unparseable_code_reports_nothing():
    # A scan must never be the reason a cell fails to run; the execution path reports the
    # syntax error with a caret of its own.
    assert scan_support.scan("def broken(:") == []


def test_empty_code_reports_nothing():
    assert scan_support.scan("") == []
    assert scan_support.scan(None) == []


def test_a_module_whose_finder_raises_counts_as_present(monkeypatch):
    def explode(name):
        raise RuntimeError("a broken path entry")

    monkeypatch.setattr(scan_support.importlib.util, "find_spec", explode)

    # An install would not fix a broken sys.path entry, so nothing is reported.
    assert scan_support.scan("import " + ABSENT) == []


# --- what a failed import can be satisfied by ---


def test_a_top_level_module_is_installable():
    assert scan_support.installable(ABSENT)


def test_a_submodule_of_a_package_that_is_present_is_not_installable():
    # json is here and has no such submodule, so the import names something that does not
    # exist rather than something that is missing. Installing again would change nothing.
    assert not scan_support.installable("json.decoder2")


def test_a_submodule_of_a_module_that_is_not_a_package_is_not_installable():
    assert not scan_support.installable("os.path.deeper")


def test_a_portion_of_a_namespace_package_is_installable(tmp_path, monkeypatch):
    # Namespace portions come from separate distributions, so the one that is missing is
    # exactly what an install would add.
    portion = tmp_path / "verso_ns_for_tests" / "portion"
    portion.mkdir(parents=True)
    (portion / "__init__.py").write_text("")

    monkeypatch.syspath_prepend(str(tmp_path))
    importlib.invalidate_caches()

    try:
        importlib.import_module("verso_ns_for_tests.portion")

        assert scan_support.installable("verso_ns_for_tests.other")
    finally:
        for name in list(sys.modules):
            if name == "verso_ns_for_tests" or name.startswith("verso_ns_for_tests."):
                del sys.modules[name]


def test_a_name_no_import_could_have_produced_is_left_alone():
    # Nothing rules out an install when the parent was never imported, which is the shape a
    # ModuleNotFoundError raised by hand takes.
    assert scan_support.installable("verso_absent_parent_for_tests.child")


def test_an_empty_name_is_not_installable():
    assert not scan_support.installable("")
    assert not scan_support.installable(None)
    assert not scan_support.installable("json.")


# --- declared requirements ---


def test_an_installed_distribution_is_satisfied():
    installed = next(
        (d.metadata["Name"] for d in metadata.distributions() if d.metadata["Name"]), None)
    if installed is None:
        pytest.skip("no distribution metadata is available in this environment")

    assert scan_support.unsatisfied([installed]) == []


def test_an_absent_distribution_is_reported():
    assert scan_support.unsatisfied([ABSENT_DIST]) == [ABSENT_DIST]


def test_a_version_specifier_does_not_hide_the_name():
    assert scan_support.unsatisfied([ABSENT_DIST + ">=2.0"]) == [ABSENT_DIST + ">=2.0"]


def test_extras_and_markers_do_not_hide_the_name():
    requirement = ABSENT_DIST + '[extra] ; python_version > "3"'

    assert scan_support.unsatisfied([requirement]) == [requirement]


def test_a_direct_reference_does_not_hide_the_name():
    requirement = ABSENT_DIST + " @ https://example.invalid/pkg.whl"

    assert scan_support.unsatisfied([requirement]) == [requirement]


def test_an_installed_distribution_with_a_specifier_is_left_alone():
    installed = next(
        (d.metadata["Name"] for d in metadata.distributions() if d.metadata["Name"]), None)
    if installed is None:
        pytest.skip("no distribution metadata is available in this environment")

    # The specifier is not evaluated here. Reporting it unsatisfied would ask the user to
    # approve an install every session for something already in place.
    assert scan_support.unsatisfied([installed + ">=0.0.1"]) == []


def test_blank_and_non_string_requirements_are_ignored():
    assert scan_support.unsatisfied(["", "   ", None, 7]) == []


def test_no_requirements_reports_nothing():
    assert scan_support.unsatisfied([]) == []
    assert scan_support.unsatisfied(None) == []


# --- the request the session answers ---


def test_the_reply_echoes_the_request_id(scan):
    reply = scan(code="import os", request_id=17)

    assert reply["req_id"] == 17
    assert reply["type"] == "scan_reply"


def test_the_reply_carries_both_lists(scan):
    reply = scan(code="import " + ABSENT, requirements=[ABSENT_DIST])

    assert modules(reply) == [ABSENT]
    assert reply["unsatisfied"] == [ABSENT_DIST]


def test_a_cell_needing_nothing_answers_with_empty_lists(scan):
    reply = scan(code="x = 1")

    assert reply["missing"] == []
    assert reply["unsatisfied"] == []


def test_a_failing_scan_still_answers(session, channel, monkeypatch):
    def explode(code):
        raise RuntimeError("boom")

    monkeypatch.setattr(scan_support, "scan", explode)
    session.scan_imports({"type": "scan_imports", "id": 3, "code": "import os"})

    reply = channel.of_type("scan_reply")[-1]
    assert reply["req_id"] == 3
    assert reply["missing"] == []


def test_the_scan_invalidates_the_import_caches(session, monkeypatch):
    # A package a magic command installed moments ago is only discoverable once the finders
    # forget what the path looked like before it existed.
    calls = []
    monkeypatch.setattr(
        versohost.importlib, "invalidate_caches", lambda: calls.append(1))

    session.scan_imports({"type": "scan_imports", "id": 1, "code": "import os"})

    assert calls
