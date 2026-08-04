"""Coverage for the repr chain and the display builtin."""

import base64
import sys

import pytest

import _versohost_comm as comm_support
import _versohost_display as display_support


@pytest.fixture
def emitted(monkeypatch):
    """Capture what display() would send, without a channel behind it."""
    captured = []
    monkeypatch.setattr(
        display_support, "_emitter", lambda mime, data: captured.append((mime, data)))
    return captured


def test_html_wins_over_the_later_steps():
    class Rich:
        def _repr_html_(self):
            return "<b>rich</b>"

        def _repr_markdown_(self):
            return "**rich**"

    assert display_support.render_value(Rich()) == {"mime": "text/html", "data": "<b>rich</b>"}


def test_markdown_is_reported_as_markdown():
    class Note:
        def _repr_markdown_(self):
            return "# heading"

    assert display_support.render_value(Note()) == {"mime": "text/markdown", "data": "# heading"}


def test_latex_is_reported_as_latex():
    class Formula:
        def _repr_latex_(self):
            return "$x^2$"

    assert display_support.render_value(Formula()) == {"mime": "text/latex", "data": "$x^2$"}


def test_svg_is_reported_as_an_image():
    class Drawing:
        def _repr_svg_(self):
            return "<svg />"

    assert display_support.render_value(Drawing()) == {"mime": "image/svg+xml", "data": "<svg />"}


def test_png_bytes_are_base64_encoded():
    payload = b"\x89PNG\r\n\x1a\n"

    class Picture:
        def _repr_png_(self):
            return payload

    rendered = display_support.render_value(Picture())

    assert rendered["mime"] == "image/png"
    assert base64.b64decode(rendered["data"]) == payload


def test_json_objects_are_serialized():
    class Document:
        def _repr_json_(self):
            return {"a": 1}

    assert display_support.render_value(Document()) == {
        "mime": "application/json", "data": '{"a": 1}'}


def test_a_mimebundle_contributes_its_richest_supported_form():
    class Bundle:
        def _repr_mimebundle_(self, include=None, exclude=None):
            return {"text/plain": "plain", "text/html": "<i>rich</i>"}

    assert display_support.render_value(Bundle()) == {"mime": "text/html", "data": "<i>rich</i>"}


def test_a_mimebundle_may_carry_metadata_alongside_the_data():
    class Bundle:
        def _repr_mimebundle_(self, include=None, exclude=None):
            return ({"text/html": "<i>rich</i>"}, {"text/html": {"isolated": True}})

    assert display_support.render_value(Bundle()) == {"mime": "text/html", "data": "<i>rich</i>"}


def test_a_mimebundle_offering_nothing_supported_falls_through():
    class Bundle:
        def _repr_mimebundle_(self, include=None, exclude=None):
            return {"application/vnd.custom": "opaque"}

        def _repr_html_(self):
            return "<b>fallback</b>"

    assert display_support.render_value(Bundle()) == {
        "mime": "text/html", "data": "<b>fallback</b>"}


def test_a_raising_step_does_not_stop_the_chain():
    class Awkward:
        def _repr_html_(self):
            raise RuntimeError("no html today")

        def _repr_markdown_(self):
            return "still here"

    assert display_support.render_value(Awkward()) == {
        "mime": "text/markdown", "data": "still here"}


def test_a_step_returning_none_falls_through_to_the_repr():
    class Empty:
        def _repr_html_(self):
            return None

    rendered = display_support.render_value(Empty())

    assert rendered["mime"] == "text/plain"
    assert "Empty" in rendered["data"]


def test_a_value_whose_repr_raises_still_renders():
    class Hostile:
        def __repr__(self):
            raise RuntimeError("not printable")

    assert display_support.render_value(Hostile()) == {
        "mime": "text/plain", "data": "<unprintable Hostile object>"}


def test_none_renders_nothing():
    assert display_support.render_value(None) is None


def test_display_emits_the_rendered_payload(emitted):
    class Rich:
        def _repr_html_(self):
            return "<b>shown</b>"

    display_support.display(Rich())

    assert emitted == [("text/html", "<b>shown</b>")]


def test_display_honours_an_explicit_mime_type(emitted):
    display_support.display("plain words", mime_type="text/html")

    assert emitted == [("text/html", "plain words")]


def test_display_of_none_emits_nothing(emitted):
    display_support.display(None)

    assert emitted == []


# --- widgets ------------------------------------------------------------------------------

# A widget offers a reference to a model rather than a rendering, so the ordinary chain would
# settle for its repr. These cover turning that reference into something that draws itself,
# and declining cleanly whenever it cannot be done. ipywidgets is not installed here, so the
# embedding it would do is stood in for; the real one is exercised by running a notebook.


class FakeWidget:
    """Offers what a widget offers: a model reference and a repr, and no rendering."""

    def __init__(self, name="Plot"):
        self._name = name

    def _repr_mimebundle_(self, **kwargs):
        return {
            "text/plain": "%s(antialias=3)" % self._name,
            display_support.WIDGET_MIME: {"model_id": "abc123", "version_major": 2},
        }

    def __repr__(self):
        return "%s(antialias=3)" % self._name


# What the two readings of a widget's state look like, covering the four cases that decide
# whether a value reaches the page: a default nobody assigned that the page cannot do without,
# a default nobody assigned that holds nothing, an assigned value, and an assigned nothing.
FULL_STATE = {
    "model-1": {
        "model_name": "AnyModel",
        "state": {
            "_esm": "export default { render() {} };",
            "tooltip": None,
            "description": "count",
            "value": None,
        },
    },
}

ASSIGNED_STATE = {
    "model-1": {
        "model_name": "AnyModel",
        "state": {
            "description": "count",
            "value": None,
        },
    },
}


@pytest.fixture
def embedding(monkeypatch):
    """
    Stand in for ipywidgets' embedder and its widget registry, neither of which is installed
    here.

    Returns a setter taking whatever embed_snippet should do, so a test can make it produce a
    snippet, produce an enormous one, or fail. The keyword arguments of each call are recorded
    on ``calls``, so a test can assert on how the embedder was asked as well as what it gave
    back.
    """
    import copy
    import types

    class Widget:
        """The one thing the display path asks of the registry."""

        @staticmethod
        def get_manager_state(drop_defaults=True, **kwargs):
            return {"state": copy.deepcopy(ASSIGNED_STATE if drop_defaults else FULL_STATE)}

    def install(behaviour):
        package = types.ModuleType("ipywidgets")
        module = types.ModuleType("ipywidgets.embed")

        def embed_snippet(views, **kwargs):
            install.calls.append(kwargs)
            return behaviour(views)

        module.embed_snippet = embed_snippet
        package.embed = module
        package.Widget = Widget
        monkeypatch.setitem(sys.modules, "ipywidgets", package)
        monkeypatch.setitem(sys.modules, "ipywidgets.embed", module)

    install.calls = []
    return install


def test_a_widget_becomes_a_document_that_can_draw_itself(embedding):
    embedding(lambda views: "<script>the widget</script>")

    payload = display_support.render_value(FakeWidget())

    assert payload["mime"] == display_support.WIDGET_OUTPUT_MIME
    assert payload["data"].lstrip().startswith("<!DOCTYPE html>")
    assert "<script>the widget</script>" in payload["data"]


def test_the_widget_being_shown_is_the_one_handed_over(embedding):
    seen = []
    embedding(lambda views: seen.append(views) or "<script></script>")
    widget = FakeWidget()

    display_support.render_value(widget)

    assert seen == [[widget]]


def test_the_page_gets_the_defaults_it_needs_and_not_the_ones_holding_nothing(embedding):
    # The embedder's own reading leaves out every value still equal to its class default, which
    # takes with it the module source a widget built on anywidget keeps there. Asking for all of
    # it instead writes traits nobody assigned as an explicit null, where the page saw no entry
    # at all before, which is a difference no front end was written against.
    embedding(lambda views: "<script></script>")

    display_support.render_value(FakeWidget())

    assert embedding.calls[0]["state"]["model-1"]["state"] == {
        "_esm": "export default { render() {} };",
        "description": "count",
        "value": None,
    }


def test_a_synchronized_trait_at_its_default_reaches_the_document():
    """
    The property the previous test asserts indirectly, against the real embedder.

    Written against a trait that holds its declared default, because that is the only case the
    two settings disagree about: a trait carrying anything else survives either way, so a widget
    built to have one would pass whichever setting was in force.
    """
    ipywidgets = pytest.importorskip("ipywidgets")
    traitlets = pytest.importorskip("traitlets")

    marker = "kept-although-it-equals-the-default"

    class Marked(ipywidgets.DOMWidget):
        _model_name = traitlets.Unicode("IntSliderModel").tag(sync=True)
        _view_name = traitlets.Unicode("IntSliderView").tag(sync=True)
        keepsake = traitlets.Unicode(marker).tag(sync=True)

    payload = display_support.render_value(Marked())

    assert payload["mime"] == display_support.WIDGET_OUTPUT_MIME, (
        "Expected a widget document, got %r. The rest of this test says nothing until it does."
        % payload["mime"])
    assert marker in payload["data"]


def test_a_trait_holding_nothing_is_left_out_of_the_real_state():
    """
    The other half, also against the real thing: a trait nobody assigned, whose default is
    nothing at all, stays out of the page. Writing it would put an explicit null where the
    document used to carry no entry, which is a distinction a front end is free to act on.
    """
    ipywidgets = pytest.importorskip("ipywidgets")

    slider = ipywidgets.IntSlider(description="threshold")

    # Found by its own id rather than by model name: the registry holds every widget any test in
    # this file has made, and more than one of them can answer to the same name.
    entry = display_support._embedded_state(ipywidgets.Widget)[slider.model_id]

    assert slider.tooltip is None, "The trait under test has to be the one nobody assigned."
    assert "tooltip" not in entry["state"]
    assert entry["state"]["description"] == "threshold"


def test_a_widget_falls_back_to_its_repr_without_ipywidgets(monkeypatch):
    # Nothing to embed with, so the cell keeps the description it had before rather than
    # showing nothing at all.
    monkeypatch.setitem(sys.modules, "ipywidgets", None)
    monkeypatch.setitem(sys.modules, "ipywidgets.embed", None)

    assert display_support.render_value(FakeWidget()) == {
        "mime": "text/plain",
        "data": "Plot(antialias=3)",
    }


def test_a_widget_that_cannot_be_serialized_falls_back_to_its_repr(embedding):
    def explode(views):
        raise ValueError("no state")

    embedding(explode)

    assert display_support.render_value(FakeWidget())["mime"] == "text/plain"


def test_a_widget_holding_too_much_data_says_so_rather_than_embedding_it(embedding):
    # The document is saved into the notebook every time the cell runs, so there is a size
    # past which writing it is the wrong thing to do.
    embedding(lambda views: "x" * (display_support.WIDGET_LIMIT_BYTES + 1))

    payload = display_support.render_value(FakeWidget())

    assert payload["mime"] == "text/plain"
    assert "too much data" in payload["data"]


# --- live widgets -------------------------------------------------------------------------

# Whether a widget is going to be live is decided here and acted on elsewhere: the document is
# the same either way, and what differs is the type it is offered under, which is what asks the
# managing side to open a channel for it. These cover the decision, since the two things it
# depends on are known in two different places and neither is this module's own.


@pytest.fixture
def live(monkeypatch):
    """
    Ask for live widgets, with the comm substitution reported as in place.

    Both are stood in for. Whether there is a view to talk to is the managing side's answer and
    arrives with each cell, and whether the comm targets are registered depends on a library
    that is not installed in the environment these tests usually run in.
    """
    def arrange(wanted=True, comms=True, targets=True):
        display_support.set_live_widgets(wanted)
        monkeypatch.setattr(comm_support, "is_installed", lambda: comms)
        monkeypatch.setattr(comm_support, "ensure_targets", lambda: targets)

    yield arrange
    display_support.set_live_widgets(False)


def test_a_widget_asks_for_a_channel_when_there_is_a_view_to_talk_to(embedding, live):
    live()
    embedding(lambda views: "<script>the widget</script>")

    payload = display_support.render_value(FakeWidget())

    assert payload["mime"] == display_support.WIDGET_LIVE_MIME


def test_a_live_widget_is_the_same_document_as_a_still_one(embedding, live):
    # The state travelling in the document is what a saved file shows, what an exported page
    # draws, and what a view falls back to when the interpreter that owns the widget has gone.
    # Leaving it out of a live document would trade all three for a smaller frame.
    embedding(lambda views: "<script>the widget</script>")
    still = display_support.render_value(FakeWidget())

    live()
    running = display_support.render_value(FakeWidget())

    assert still["mime"] == display_support.WIDGET_OUTPUT_MIME
    assert running["mime"] == display_support.WIDGET_LIVE_MIME
    assert still["data"] == running["data"]


def test_a_widget_asks_for_nothing_when_no_view_can_be_reached(embedding, live):
    # What baking a file and running a terminal both look like. Asking anyway would open a
    # channel nothing ever mounts, and everything sent to it would queue until it failed.
    live(wanted=False)
    embedding(lambda views: "<script></script>")

    assert display_support.render_value(FakeWidget())["mime"] == display_support.WIDGET_OUTPUT_MIME


def test_a_widget_asks_for_nothing_when_its_messages_would_go_nowhere(embedding, live):
    # A channel with no comm substitution behind it is a control that answers nobody, which
    # looks live and is not.
    live(comms=False)
    embedding(lambda views: "<script></script>")

    assert display_support.render_value(FakeWidget())["mime"] == display_support.WIDGET_OUTPUT_MIME


def test_a_widget_asks_for_nothing_when_a_view_would_have_nothing_to_ask(embedding, live):
    # Without the targets registered, a mounted view cannot open the comm it asks for every
    # widget's state through, and a widget built before anything was drawing is unreachable.
    live(targets=False)
    embedding(lambda views: "<script></script>")

    assert display_support.render_value(FakeWidget())["mime"] == display_support.WIDGET_OUTPUT_MIME


def test_the_document_names_where_the_front_end_is_loaded_from(embedding):
    embedding(lambda views: "<script></script>")

    display_support.render_value(FakeWidget())

    assert embedding.calls[0]["embed_url"] == display_support.CDN_EMBED_URL


def test_an_asset_source_nobody_serves_is_not_taken(embedding):
    display_support.configure_assets("bundled")
    try:
        assert display_support._asset_source == "bundled"

        display_support.configure_assets("somewhere-else")
        assert display_support._asset_source == "bundled", (
            "An unrecognised source has to leave the last one in place rather than reset it.")
    finally:
        display_support.configure_assets("cdn")


def test_an_asset_source_that_is_not_served_yet_still_draws(embedding):
    # Reserved rather than implemented: a session asking for a copy that does not ship gets the
    # published bundle, which draws, rather than an address that answers nothing.
    display_support.configure_assets("bundled")
    try:
        embedding(lambda views: "<script></script>")
        display_support.render_value(FakeWidget())

        assert embedding.calls[0]["embed_url"] == display_support.CDN_EMBED_URL
    finally:
        display_support.configure_assets("cdn")


def test_an_object_offering_html_is_not_treated_as_a_widget(embedding):
    embedding(lambda views: "<script>should not be reached</script>")

    class Rich:
        def _repr_html_(self):
            return "<b>rich</b>"

    assert display_support.render_value(Rich()) == {"mime": "text/html", "data": "<b>rich</b>"}


def test_a_plain_value_is_not_treated_as_a_widget(embedding):
    embedding(lambda views: "<script>should not be reached</script>")

    assert display_support.render_value([1, 2, 3]) == {"mime": "text/plain", "data": "[1, 2, 3]"}


# --- output areas -------------------------------------------------------------------------


def _output_widget(captured):
    """An ipywidgets.Output stand-in, which is what a `with output:` block leaves behind."""

    namespace = {
        "outputs": captured,
        "_repr_mimebundle_": lambda self, **kwargs: {
            "text/plain": "Output()",
            display_support.WIDGET_MIME: {"model_id": "def456"},
        },
        "__repr__": lambda self: "Output()",
    }
    cls = type("Output", (object,), namespace)
    cls.__module__ = "ipywidgets.widgets.widget_output"
    return cls()


def test_an_empty_output_area_shows_nothing_at_all(embedding):
    # There is no IPython shell here for `with output:` to capture through, so an output area
    # is always empty. Libraries display one next to the real figure, and drawing it would put
    # a blank frame under every plot.
    embedding(lambda views: "<script>should not be reached</script>")

    assert display_support.render_value(_output_widget(())) is None


def test_an_output_area_holding_something_is_still_drawn(embedding):
    embedding(lambda views: "<script>the widget</script>")

    payload = display_support.render_value(_output_widget(({"name": "stdout"},)))

    assert payload["mime"] == display_support.WIDGET_OUTPUT_MIME


def test_an_unrelated_class_called_output_is_not_suppressed(embedding):
    embedding(lambda views: "<script>the widget</script>")

    class Output(FakeWidget):
        outputs = ()

    payload = display_support.render_value(Output())

    assert payload["mime"] == display_support.WIDGET_OUTPUT_MIME
