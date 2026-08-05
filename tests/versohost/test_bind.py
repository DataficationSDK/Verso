"""
Covers projecting a trait into the shared variables.

What the module actually requires of an object is two methods: one that says whether it has a
named trait, and one that calls back when the trait changes. Most of this runs against a plain
object providing exactly those, which is both the contract and the reason a future kernel with
its own live objects could offer the same verb over them. The tests that need real traitlets or
a real widget say so and skip themselves without one.
"""

import sys

import pytest

import _versohost_bind as bind_support
import _versohost_vars as variable_support


def _unchanged(old, new):
    """
    Whether an assignment is worth notifying about. Written out because an array answers ``==``
    with an array of answers rather than with one, and asking it for the truth of that raises.
    """
    if old is new:
        return True

    try:
        return bool(old == new)
    except Exception:
        return False


class Observable(object):
    """
    The contract a bindable object meets, and nothing else.

    Deliberately not a traitlets object: writing the callback dispatch out here is what pins down
    that the module asks for no more than this, and it is what lets these tests run in an
    environment with no widget library at all.
    """

    def __init__(self, **values):
        object.__setattr__(self, "_values", dict(values))
        object.__setattr__(self, "_watchers", {})

    def has_trait(self, name):
        return name in self._values

    def trait_names(self):
        return list(self._values)

    def observe(self, handler, names=None):
        self._watchers.setdefault(names, []).append(handler)

    def unobserve(self, handler, names=None):
        watchers = self._watchers.get(names) or []
        if handler in watchers:
            watchers.remove(handler)

    def __getattr__(self, name):
        try:
            return self._values[name]
        except KeyError:
            raise AttributeError(name)

    def __setattr__(self, name, value):
        if name not in self._values:
            raise AttributeError(name)

        old = self._values[name]
        self._values[name] = value
        if _unchanged(old, value):
            return  # a trait set to what it already holds raises no change

        for handler in list(self._watchers.get(name) or []):
            handler({"name": name, "old": old, "new": value})


class Wired:
    """An installed bind module, with everything it sent and everything it explained."""

    def __init__(self, frames, reports):
        self.frames = frames
        self.reports = reports

    def updates(self, name=None):
        return [
            f for f in self.frames
            if f.get("type") == "bind_update" and (name is None or f.get("name") == name)
        ]

    @property
    def last(self):
        assert self.frames, "nothing was sent"
        return self.frames[-1]


@pytest.fixture
def wired():
    frames = []
    reports = []
    bind_support.install(frames.append, reports.append)

    yield Wired(frames, reports)

    bind_support.reset()


@pytest.fixture
def dial():
    return Observable(value=3, label="start", readings=[])


@pytest.fixture
def scope(dial):
    return {"dial": dial, "dials": [dial], "number": 7}


# --- binding ------------------------------------------------------------------------------


def test_binding_reports_the_trait_and_its_current_value(wired, scope):
    outcome = bind_support.bind(scope, "dial", "value", "threshold")

    assert outcome["status"] == "ok"
    assert outcome["name"] == "threshold"
    assert outcome["expression"] == "dial"
    assert outcome["trait"] == "value"
    assert outcome["value"] == 3
    assert outcome["replaced"] is None
    assert outcome["widget_id"]


def test_a_name_defaults_to_the_trait(wired, scope):
    outcome = bind_support.bind(scope, "dial", "label")

    assert outcome["status"] == "ok"
    assert outcome["name"] == "label"


def test_any_expression_naming_the_object_works(wired, scope):
    outcome = bind_support.bind(scope, "dials[0]", "value", "threshold")

    assert outcome["status"] == "ok"
    assert outcome["value"] == 3


def test_an_unknown_name_is_refused_with_the_reason(wired, scope):
    outcome = bind_support.bind(scope, "missing", "value")

    assert outcome["status"] == "error"
    assert "could not be evaluated" in outcome["reason"]


def test_an_object_with_no_traits_is_refused(wired, scope):
    outcome = bind_support.bind(scope, "number", "value")

    assert outcome["status"] == "error"
    assert "no observable traits" in outcome["reason"]


def test_an_unknown_trait_is_refused_and_names_the_ones_there_are(wired, scope):
    outcome = bind_support.bind(scope, "dial", "valu")

    assert outcome["status"] == "error"
    assert "has no trait named 'valu'" in outcome["reason"]
    assert "value" in outcome["reason"]


def test_naming_no_trait_is_refused(wired, scope):
    assert bind_support.bind(scope, "dial", "")["status"] == "error"
    assert bind_support.bind(scope, "", "value")["status"] == "error"


def test_the_same_trait_bound_again_moves_rather_than_doubling(wired, scope):
    bind_support.bind(scope, "dial", "value", "first")
    outcome = bind_support.bind(scope, "dial", "value", "second")

    assert outcome["replaced"] == "first"
    assert [b["name"] for b in bind_support.describe()] == ["second"]

    scope["dial"].value = 11

    # One update, under the surviving name. A doubled binding would send two.
    assert len(wired.updates()) == 1
    assert wired.last["name"] == "second"


def test_two_traits_of_one_object_are_separate_bindings(wired, scope):
    bind_support.bind(scope, "dial", "value", "threshold")
    bind_support.bind(scope, "dial", "label", "caption")

    assert sorted(b["name"] for b in bind_support.describe()) == ["caption", "threshold"]


def test_unbinding_stops_the_updates(wired, scope):
    bind_support.bind(scope, "dial", "value", "threshold")

    assert bind_support.unbind("threshold") is True

    scope["dial"].value = 42
    assert wired.updates() == []


def test_unbinding_a_name_that_was_never_bound_says_so(wired, scope):
    assert bind_support.unbind("threshold") is False


def test_names_are_matched_without_regard_to_case(wired, scope):
    bind_support.bind(scope, "dial", "value", "Threshold")

    assert bind_support.unbind("threshold") is True


# --- what a change sends ------------------------------------------------------------------


def test_a_trait_change_is_sent_under_the_bound_name(wired, scope):
    bind_support.bind(scope, "dial", "value", "threshold")

    scope["dial"].value = 98

    frame = wired.last
    assert frame["type"] == "bind_update"
    assert frame["req_id"] == bind_support.UNSOLICITED_REQUEST_ID
    assert frame["name"] == "threshold"
    assert frame["value"] == 98


def test_a_trait_set_to_the_same_value_sends_nothing(wired, scope):
    bind_support.bind(scope, "dial", "value", "threshold")

    scope["dial"].value = 3

    assert wired.updates() == []


def test_a_list_trait_crosses_as_a_list(wired, scope):
    bind_support.bind(scope, "dial", "readings", "readings")

    scope["dial"].readings = [1, 2, 3]

    assert wired.last["value"] == [1, 2, 3]


def test_a_value_with_no_json_form_of_its_own_crosses_tagged(wired, scope):
    import datetime

    scope["dial"] = Observable(stamp=None)
    bind_support.bind(scope, "dial", "stamp", "stamp")

    scope["dial"].stamp = datetime.date(2026, 8, 4)

    assert wired.last["value"] == {
        "__verso_type__": "date", "__verso_value__": "2026-08-04"}


# --- what arrives from another kernel -----------------------------------------------------


def test_a_value_from_another_kernel_reaches_the_trait(wired, scope):
    bind_support.bind(scope, "dial", "value", "threshold")

    assert bind_support.apply("threshold", 61) is True
    assert scope["dial"].value == 61


def test_setting_the_trait_runs_the_author_callbacks(wired, scope):
    seen = []
    scope["dial"].observe(lambda change: seen.append(change["new"]), names="value")
    bind_support.bind(scope, "dial", "value", "threshold")

    bind_support.apply("threshold", 55)

    assert seen == [55]


def test_a_value_from_another_kernel_is_sent_straight_back(wired, scope):
    """
    The echo is deliberate. Going out through the same path both ways is what makes a value the
    trait coerced, or refused, visible rather than silently different from what the other kernel
    holds. Recognising it as an echo is the managing side's job.
    """
    bind_support.bind(scope, "dial", "value", "threshold")

    bind_support.apply("threshold", 61)

    assert [f["value"] for f in wired.updates()] == [61]


def test_a_value_the_trait_will_not_take_is_explained(wired, scope):
    class Strict(Observable):
        def __setattr__(self, name, value):
            if name == "value" and not isinstance(value, int):
                raise TypeError("value must be an int")
            Observable.__setattr__(self, name, value)

    scope["dial"] = Strict(value=3)
    bind_support.bind(scope, "dial", "value", "threshold")

    assert bind_support.apply("threshold", "not a number") is False
    assert scope["dial"].value == 3
    assert any("could not be set" in report for report in wired.reports)


def test_a_value_for_a_name_that_is_not_bound_does_nothing(wired, scope):
    assert bind_support.apply("threshold", 5) is False


def test_a_tagged_value_is_rebuilt_before_it_reaches_the_trait(wired, scope):
    import decimal

    scope["priced"] = Observable(amount=None)
    bind_support.bind(scope, "priced", "amount", "amount")

    bind_support.apply("amount", {"__verso_type__": "decimal", "__verso_value__": "12.75"})

    assert scope["priced"].amount == decimal.Decimal("12.75")


# --- size ---------------------------------------------------------------------------------


def _oversize():
    """A value comfortably past what a projected value may occupy, in plain Python."""
    return [float(index) for index in range(200_000)]


def test_a_value_over_the_limit_is_refused_rather_than_described(wired, scope):
    scope["sampled"] = Observable(samples=_oversize())

    outcome = bind_support.bind(scope, "sampled", "samples", "samples")

    assert outcome["status"] == "error"
    assert "over the" in outcome["reason"]
    assert variable_support.format_bytes(bind_support.LIMIT_BYTES) in outcome["reason"]


def test_a_change_that_grows_past_the_limit_keeps_what_crossed_before(wired, scope):
    scope["sampled"] = Observable(samples=[1.0])
    bind_support.bind(scope, "sampled", "samples", "samples")

    scope["sampled"].samples = _oversize()

    assert wired.updates() == []
    assert any("over the" in report for report in wired.reports)


def test_an_oversize_control_explains_itself_once(wired, scope):
    scope["sampled"] = Observable(samples=[1.0])
    bind_support.bind(scope, "sampled", "samples", "samples")

    scope["sampled"].samples = _oversize()
    scope["sampled"].samples = _oversize() + [1.0]

    assert len(wired.reports) == 1


def test_a_value_that_fits_again_is_sent_and_explained_again_if_it_grows(wired, scope):
    scope["sampled"] = Observable(samples=[1.0])
    bind_support.bind(scope, "sampled", "samples", "samples")

    scope["sampled"].samples = _oversize()
    scope["sampled"].samples = [2.0]
    scope["sampled"].samples = _oversize()

    assert [f["value"] for f in wired.updates()] == [[2.0]]
    assert len(wired.reports) == 2


# --- traitlets ----------------------------------------------------------------------------


def test_a_real_traitlets_object_binds_and_reports_changes(wired):
    traitlets = pytest.importorskip("traitlets")

    class Dial(traitlets.HasTraits):
        value = traitlets.Int(3)

    dial = Dial()
    outcome = bind_support.bind({"dial": dial}, "dial", "value", "threshold")

    assert outcome["status"] == "ok"
    assert outcome["value"] == 3

    dial.value = 98
    assert wired.last["value"] == 98


def test_a_value_a_real_trait_refuses_is_explained(wired):
    traitlets = pytest.importorskip("traitlets")

    class Dial(traitlets.HasTraits):
        value = traitlets.Int(3)

    dial = Dial()
    bind_support.bind({"dial": dial}, "dial", "value", "threshold")

    assert bind_support.apply("threshold", "not a number") is False
    assert dial.value == 3
    assert any("could not be set" in report for report in wired.reports)


def test_an_array_crosses_as_the_numbers_in_it(wired):
    numpy = pytest.importorskip("numpy")

    scope = {"sampled": Observable(samples=None)}
    bind_support.bind(scope, "sampled", "samples", "samples")

    scope["sampled"].samples = numpy.array([1.5, 2.5, 3.5])

    assert wired.last["value"] == [1.5, 2.5, 3.5]


# --- widgets ------------------------------------------------------------------------------


def test_a_widget_trait_is_anchored_to_its_model_id(wired):
    ipywidgets = pytest.importorskip("ipywidgets")

    slider = ipywidgets.IntSlider(value=12)
    try:
        outcome = bind_support.bind({"slider": slider}, "slider", "value", "threshold")

        assert outcome["status"] == "ok"
        assert outcome["widget_id"] == slider.model_id
        assert outcome["value"] == 12
    finally:
        slider.close()


def test_a_widget_valued_trait_is_refused(wired):
    ipywidgets = pytest.importorskip("ipywidgets")

    slider = ipywidgets.IntSlider()
    try:
        outcome = bind_support.bind({"slider": slider}, "slider", "layout")

        assert outcome["status"] == "error"
        assert "live object" in outcome["reason"]
    finally:
        slider.close()


def test_a_trait_holding_several_widgets_is_refused(wired):
    ipywidgets = pytest.importorskip("ipywidgets")

    box = ipywidgets.HBox([ipywidgets.IntSlider()])
    try:
        outcome = bind_support.bind({"box": box}, "box", "children")

        assert outcome["status"] == "error"
        assert "live object" in outcome["reason"]
    finally:
        box.close()


def test_moving_a_slider_sends_its_value(wired):
    ipywidgets = pytest.importorskip("ipywidgets")

    slider = ipywidgets.IntSlider(value=1)
    try:
        bind_support.bind({"slider": slider}, "slider", "value", "threshold")
        slider.value = 98

        assert wired.last["name"] == "threshold"
        assert wired.last["value"] == 98
    finally:
        slider.close()


# --- teardown -----------------------------------------------------------------------------


def test_resetting_stops_watching_everything(scope):
    frames = []
    bind_support.install(frames.append)
    bind_support.bind(scope, "dial", "value", "threshold")

    bind_support.reset()
    scope["dial"].value = 77

    assert frames == []
    assert bind_support.describe() == []


def test_a_change_with_nowhere_to_go_is_not_an_error(wired, scope):
    bind_support.bind(scope, "dial", "value", "threshold")
    bind_support.install(None)

    scope["dial"].value = 5  # the connection has gone; assigning a trait must not raise


def test_the_module_needs_no_widget_library_to_load():
    """
    A session that never shows a widget must not pay for one, and an environment with neither
    ipywidgets nor traitlets still has to start. Nothing here imports either at load time, so
    the module already being present is the whole of the check.
    """
    assert sys.modules["_versohost_bind"] is bind_support
