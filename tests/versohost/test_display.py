"""Coverage for the repr chain and the display builtin."""

import base64

import pytest

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
