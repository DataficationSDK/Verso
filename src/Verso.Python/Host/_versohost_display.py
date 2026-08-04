"""Rich display support for the Verso Python host.

Reduces a value to a single MIME payload through the repr chain notebooks conventionally
implement, provides the ``IPython.display`` names when IPython itself is not installed, and
routes matplotlib figures into the notebook as they are drawn. Nothing outside the standard
library is imported at module scope, and the one host module it reads is under the same rule.

The module is imported by ``versohost.py`` and shares its process. What it keeps between calls
is the emitter registered at startup and the two settings the managing side sends for a cell.
"""

import base64
import importlib.util
import io
import json
import os
import sys
import threading
import traceback
import types
import weakref

import _versohost_comm as comm_support

# The MIME types the managing side knows how to render, most preferred first. A mimebundle
# is searched in this order, so an object offering several forms contributes its richest.
SUPPORTED_MIMES = (
    "text/html",
    "text/markdown",
    "text/latex",
    "image/svg+xml",
    "image/png",
    "image/jpeg",
    "application/json",
    "text/plain",
)

BINARY_MIMES = ("image/png", "image/jpeg", "image/gif", "image/webp", "image/bmp")

PYPLOT_MODULE = "matplotlib.pyplot"

# What a widget offers instead of a rendering: a reference to a model the front end is
# expected to already hold. Nothing in this host holds one, so the reference is turned into
# a self-contained document carrying the model's state with it.
WIDGET_MIME = "application/vnd.jupyter.widget-view+json"

# Carries that document. Named apart from text/html because it is a whole page rather than a
# fragment, and the surface showing it has to give it a frame of its own to run in.
WIDGET_OUTPUT_MIME = "text/x-verso-widget"

# The same document, asking for a message channel to be opened for it. The distinction lives
# only between this module and the managing side: what reaches the notebook is a widget output
# either way, and whether it holds a channel is what makes it live. Kept as a MIME rather than
# a flag beside one because a display payload carries a type and its data and nothing else.
WIDGET_LIVE_MIME = "text/x-verso-widget-live"

# A widget document is saved into the notebook, so it is charged against the file every time
# the cell runs. Widget state is mostly the data being drawn, and a large mesh or a long
# series reaches a size worth refusing rather than quietly writing to disk.
WIDGET_LIMIT_BYTES = 4 * 1024 * 1024

# Where the front end's own code is loaded from. The published bundle carries the whole widget
# front end, including the machinery a live view talks to Python through, so one address covers
# both a saved page opened on its own and a view in a running notebook.
WIDGET_ASSET_SOURCES = ("cdn", "bundled")

CDN_EMBED_URL = (
    "https://cdn.jsdelivr.net/npm/@jupyter-widgets/html-manager@^1.0.1/dist/embed-amd.js")

# Returned by a chain step that has decided a value is not worth showing at all, as opposed
# to None, which means the step declined and the next one should try.
SUPPRESS = object()

_emitter = None
_live_widgets = False
_asset_source = "cdn"


# --- emission ---------------------------------------------------------------------------


def install(emitter):
    """
    Register the callback that turns a payload into a display event and put the library
    hooks in place. The callback takes a MIME type and its already-encoded data.
    """
    global _emitter
    _emitter = emitter

    _install_ipython_shim()
    _install_matplotlib_hook()


def set_live_widgets(enabled):
    """
    Record whether the managing side can give a widget a channel to talk over.

    Set for each cell, because it is a property of the surface the output is going to rather
    than of this interpreter: the same session answers a notebook in an editor, where there is
    a view to talk to, and a file being baked, where there is not.
    """
    global _live_widgets
    _live_widgets = bool(enabled)


def configure_assets(source):
    """
    Choose where a widget document loads the front end from.

    Only one source is served today. The choice is carried this far anyway so that adding a
    copy alongside the notebook later changes which URL is written and nothing else: not the
    shape of the document, not the protocol, and not what a saved file means.
    """
    global _asset_source
    if isinstance(source, str) and source.strip().lower() in WIDGET_ASSET_SOURCES:
        _asset_source = source.strip().lower()


def _embed_script_url():
    # A copy served beside the notebook would be answered here. Until one is, the published
    # bundle is what both sources resolve to, which is why asking for the other is documented
    # as a reservation rather than as something with an effect.
    return CDN_EMBED_URL


def emit(mime, data, widget_id=None):
    """
    Hand one payload to the managing side.

    ``widget_id`` names the widget a payload was made from, and is carried only for the one
    payload kind that has one. It is passed on only when there is one, so an emitter written
    against the two the rest of this module produces goes on working.
    """
    if _emitter is None:
        return

    if widget_id is None:
        _emitter(mime, data)
    else:
        _emitter(mime, data, widget_id)


def display(obj, mime_type=None):
    """The ``display()`` builtin cells call. An explicit MIME type bypasses the chain."""
    if obj is None:
        return

    if mime_type:
        payload = {"mime": str(mime_type), "data": _to_text(obj)}
    else:
        payload = render_value(obj)
        if payload is None:
            return

    if capture_display(payload["mime"], payload["data"]):
        return

    emit(payload["mime"], payload["data"], payload.get("widget_id"))


# --- output areas -------------------------------------------------------------------------

# Which output areas are collecting on this thread, innermost last. A stack because the blocks
# nest, and per-thread because a background thread writing while another thread sits inside a
# block is not what that block was opened to collect.
_capture = threading.local()
_capture_installed = False

# Areas asked to clear once something arrives to replace what they hold. That is what
# ``clear_output(wait=True)`` means, and it is what keeps an area driven by a callback from
# blinking empty between one write and the next.
_pending_clear = weakref.WeakSet()


def _capture_stack():
    stack = getattr(_capture, "stack", None)
    if stack is None:
        stack = []
        _capture.stack = stack
    return stack


def _capture_target():
    stack = _capture_stack()
    return stack[-1] if stack else None


def _record(widget, change):
    """
    Change what an area holds, guarding against the change itself producing output.

    Assigning the trait sends a message, and a message that cannot be sent is reported on
    standard error. Without the guard that report arrives back here and recurses. Answers
    whether the change was made, so a caller told no can fall back rather than lose the text.
    """
    if getattr(_capture, "recording", False):
        return False

    _capture.recording = True
    try:
        change(widget)
        return True
    except Exception:
        return False
    finally:
        _capture.recording = False


def _append(area, output):
    """Add one output, honoring a clear that was asked to wait for it."""
    if area in _pending_clear:
        _pending_clear.discard(area)
        area.outputs = (output,)
    else:
        area.outputs += (output,)


def capture_stream(name, text):
    """
    Offer stream text to whichever output area is collecting on this thread.

    Answers whether it was taken. Told no, a caller sends the text the way it otherwise
    would, which is what keeps a failure here from losing output rather than misplacing it.
    """
    widget = _capture_target()
    if widget is None:
        return False

    return _record(widget, lambda area: _append(
        area, {"output_type": "stream", "name": name, "text": text}))


def capture_display(mime, data):
    """
    Offer a rendered payload to the area collecting on this thread.

    A widget's own document is left alone: it is a whole page with a front end in it, which
    is not something an output area inside another widget can draw, so those go on reaching
    the cell as they did before.
    """
    if mime in (WIDGET_OUTPUT_MIME, WIDGET_LIVE_MIME):
        return False

    widget = _capture_target()
    if widget is None:
        return False

    return _record(widget, lambda area: _append(
        area, {"output_type": "display_data", "data": {mime: data}, "metadata": {}}))


def capture_clear(wait=False):
    """
    Empty the area collecting on this thread. Answers whether there was one.

    Asked to wait, the area keeps what it holds until something arrives to replace it, so a
    callback that clears and writes on every keystroke never shows an empty area in between.
    """
    widget = _capture_target()
    if widget is None:
        return False

    if wait:
        _pending_clear.add(widget)
        return True

    _pending_clear.discard(widget)
    return _record(widget, lambda area: setattr(area, "outputs", ()))


def _flush_streams():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.flush()
        except Exception:
            pass


def install_output_capture():
    """
    Make ``ipywidgets.Output`` collect what is written inside it.

    The library's own implementation asks a kernel to route messages by the id of the request
    being served, and collects nothing when there is no kernel to ask. That is this host, so
    ``with area:`` and ``@area.capture()`` run the block and leave the area empty, which is
    the worst of the available failures: the pattern is in most of the widget examples an
    author will copy, and it fails without saying anything.

    The same contract is kept here by the only means this host has, redirecting while a block
    is open. ``msg_id`` is deliberately left alone: a view reads it to decide whether a kernel
    is feeding the area, and nothing here is.

    Installed once, when the first widget opens its comm. That is early enough, because an
    area has to be built before it can be entered, and building one opens a comm.
    """
    global _capture_installed

    if _capture_installed:
        return True

    try:
        from ipywidgets import Output
    except Exception:
        return False

    def enter(self):
        # What was written before the block belongs to what came before it, and goes on its
        # own way first so the two do not arrive interleaved.
        _flush_streams()
        _capture_stack().append(self)
        return self

    def leave(self, etype, evalue, tb):
        _flush_streams()

        stack = _capture_stack()
        if stack and stack[-1] is self:
            stack.pop()
        elif self in stack:
            # Left in an order it was not entered in. Dropping the entry is better than
            # leaving text going to an area the author has stopped collecting into.
            stack.remove(self)

        if etype is None:
            return False

        # Reported where the block's own output went rather than raised, which is what the
        # library does when a kernel is present and the only useful answer here: a callback
        # running between cells has nothing to raise into.
        _record(self, lambda area: _append(area, {
            "output_type": "stream",
            "name": "stderr",
            "text": "".join(traceback.format_exception(etype, evalue, tb)),
        }))
        return True

    def clear(self, wait=False, **keys):
        """
        Empty this area.

        Replaces the library's own, which asks whichever ``IPython.display.clear_output`` it
        imported to publish a message to a kernel. In an environment with IPython installed
        that is the real one, which answers nothing outside a shell, so an area could not be
        cleared even from inside a block. This is also what ``capture(clear_output=True)``
        calls, and that is the shape a callback firing on every keystroke needs.
        """
        if wait:
            _pending_clear.add(self)
            return

        _pending_clear.discard(self)
        _record(self, lambda area: setattr(area, "outputs", ()))

    try:
        Output.__enter__ = enter
        Output.__exit__ = leave
        Output.clear_output = clear
    except Exception:
        return False

    _capture_installed = True
    return True


# --- the repr chain ---------------------------------------------------------------------


def render_value(value):
    """
    Reduce a value to ``{"mime": ..., "data": ...}``, or None when there is nothing to show.

    Each step is attempted in turn and a step that raises is treated as one that declined,
    so a broken ``_repr_html_`` still leaves the plain-text form reachable.
    """
    if value is None:
        return None

    for step in _CHAIN:
        try:
            payload = step(value)
        except Exception:
            continue
        if payload is SUPPRESS:
            return None
        if payload is not None:
            return payload

    return {"mime": "text/plain", "data": _safe_repr(value)}


def _from_widget(value):
    """
    Turn a widget into a document that can draw itself.

    A widget reports only a model reference and a plain-text repr, so the ordinary chain
    settles for the repr and the cell shows something like ``Plot(antialias=3, ...)``. The
    reference is only useful to a front end already holding the model, which is a thing this
    host does not have and cannot cheaply become.

    ipywidgets can serialize a widget's state into a page that carries its own loader, so the
    state travels with the notebook and the drawing happens in the browser. That page is what
    this returns. It is only reachable when the author's own environment has ipywidgets, which
    it does whenever a widget exists to be shown, so nothing is added to this host to support
    it. Anything that fails along the way declines and leaves the repr reachable.

    The same page is returned whether or not the widget is going to be live. State that
    travels with the document is what a saved file shows when it is opened again, what an
    exported page draws, and what a view falls back to when the interpreter that owns the
    widget has gone. A view given a channel takes the state from the interpreter instead, so
    what it draws is current rather than as it was when the cell ran.
    """
    bundle = _mimebundle_of(value)
    if bundle is None or WIDGET_MIME not in bundle:
        return None

    if _is_empty_output_widget(value):
        return SUPPRESS

    document = _document_for(value)
    if document is None:
        return None

    if len(document.encode("utf-8", "replace")) > WIDGET_LIMIT_BYTES:
        return {
            "mime": "text/plain",
            "data": (
                "This widget holds too much data to embed in the notebook "
                "(over %d MB). Draw fewer points, or export it with the library's own "
                "save or snapshot function." % (WIDGET_LIMIT_BYTES // (1024 * 1024))
            ),
        }

    if not _wants_a_channel():
        return {"mime": WIDGET_OUTPUT_MIME, "data": document}

    # Named so the managing side can ask for this widget's page again later, which is what lets a
    # save record the state a reader is looking at rather than the state the cell drew. The name
    # is the model id the view already addresses the widget by, so nothing new is invented for it.
    return {"mime": WIDGET_LIVE_MIME, "data": document,
            "widget_id": _model_id_of(value)}


def _document_for(widget):
    """
    The self-contained page one widget draws itself from, or None when it cannot be built.

    Shared by the page written when a widget is first shown and the one asked for when the
    notebook is being saved, so a refreshed page and the original are the same kind of thing
    and nothing downstream has to tell them apart.
    """
    try:
        from ipywidgets import Widget
        from ipywidgets.embed import embed_snippet
    except Exception:
        return None

    try:
        snippet = embed_snippet(
            views=[widget], state=_embedded_state(Widget), embed_url=_embed_script_url())
    except Exception:
        return None

    return WIDGET_DOCUMENT.replace("{snippet}", snippet)


def _model_id_of(widget):
    try:
        return widget.model_id
    except Exception:
        return None


def snapshot(widget_id):
    """
    A current page for one widget, named by the model id its view was addressed by.

    Answered on the thread the author's own cells run on, so what it reads is a widget nothing
    is in the middle of changing, and so a request arriving during a cell waits for the cell
    rather than reading a half-applied state. Everything that can go wrong answers None and the
    page already written stands: the widget may have been discarded, the interpreter may no
    longer have ipywidgets, or the state may have grown past what a notebook will hold.
    """
    if not widget_id:
        return None

    widget = _widget_by_id(widget_id)
    if widget is None:
        return None

    document = _document_for(widget)
    if document is None:
        return None

    if len(document.encode("utf-8", "replace")) > WIDGET_LIMIT_BYTES:
        return None

    return document


def _widget_by_id(widget_id):
    """
    The live widget behind a model id, read from whichever register this ipywidgets keeps.

    The register moved from the widget class to the module that defines it, and the old name is
    kept as an alias that warns when it is read, so the module is asked first and the class only
    when there is nothing there. A release keeping neither leaves the page already written in
    place rather than raising.
    """
    try:
        from ipywidgets.widgets import widget as widget_module
    except Exception:
        widget_module = None

    register = getattr(widget_module, "_instances", None)
    if isinstance(register, dict):
        return register.get(widget_id)

    try:
        from ipywidgets import Widget
    except Exception:
        return None

    register = getattr(Widget, "widgets", None)
    return register.get(widget_id) if isinstance(register, dict) else None


def _wants_a_channel():
    """
    Whether this widget should be given a channel to the view that draws it.

    Three things have to hold, and each is known in a different place. The surface has to have
    a view to talk to, which only the managing side knows and reports for each cell. The comm
    substitution has to be in place, or a widget's own messages go nowhere. And the targets a
    front end opens have to be registered, which is what lets a mounted view ask for the state
    of every widget at once instead of waiting for one to change.
    """
    if not _live_widgets or not comm_support.is_installed():
        return False

    return bool(comm_support.ensure_targets())


def _embedded_state(widget_class):
    """
    The widget state to put in the page, read twice and reconciled.

    Left to itself the embedder writes only the synchronized values that differ from their
    class defaults, which saves a good deal but gives up more than it can afford: some widgets
    keep the very thing the page needs in a trait nobody ever assigned to. A widget built on
    anywidget carries its module source and its styling that way, and without them the page
    loads with nothing to run and the cell shows a broken control.

    Asking for everything instead would also write out every trait that was never set to
    anything, as an explicit null where the page had no entry at all before. A front end is
    free to read those two differently, and none of them were written against a document that
    carries the first. Those are dropped again here, so the one thing this changes about a
    document is the defaults that actually hold something.
    """
    full = widget_class.get_manager_state(drop_defaults=False)["state"]
    trimmed = widget_class.get_manager_state(drop_defaults=True)["state"]
    return _without_unset_values(full, trimmed)


def _without_unset_values(full, trimmed):
    """
    Drop every null from ``full`` that ``trimmed`` also left out, and keep the rest.

    A null the trimmed reading kept is one the author assigned, which is a decision worth
    carrying. A null it left out is a trait still holding a default that happens to be null,
    which is the pair this is here to separate.
    """
    for model_id, entry in full.items():
        state = entry.get("state")
        if not isinstance(state, dict):
            continue

        assigned = trimmed.get(model_id, {}).get("state", {})
        entry["state"] = {
            name: held for name, held in state.items()
            if held is not None or name in assigned
        }

    return full


def _is_empty_output_widget(value):
    """
    Whether this is an output area with nothing in it.

    ``ipywidgets.Output`` collects what is written inside a ``with`` block. Libraries also
    build one, collect nothing into it, and display it alongside the real figure, which would
    otherwise put a second, blank frame under every plot. An area holding anything is left
    alone and rendered normally, which is what an area that did collect something reaches.
    """
    if type(value).__name__ != "Output":
        return False
    if getattr(type(value), "__module__", "").split(".")[0] != "ipywidgets":
        return False
    return not getattr(value, "outputs", None)


def _mimebundle_of(value):
    method = getattr(value, "_repr_mimebundle_", None)
    if not callable(method):
        return None

    bundle = _unwrap(method())
    return bundle if isinstance(bundle, dict) else None


def _from_mimebundle(value):
    method = getattr(value, "_repr_mimebundle_", None)
    if not callable(method):
        return None

    bundle = _unwrap(method())
    if not isinstance(bundle, dict):
        return None

    for mime in SUPPORTED_MIMES:
        data = bundle.get(mime)
        if data is not None:
            return _payload(mime, data)
    return None


def _from_repr_method(value, method_name, mime):
    method = getattr(value, method_name, None)
    if not callable(method):
        return None

    rendered = _unwrap(method())
    if rendered is None:
        return None
    return _payload(mime, rendered)


def _from_html(value):
    return _from_repr_method(value, "_repr_html_", "text/html")


def _from_markdown(value):
    return _from_repr_method(value, "_repr_markdown_", "text/markdown")


def _from_latex(value):
    return _from_repr_method(value, "_repr_latex_", "text/latex")


def _from_svg(value):
    return _from_repr_method(value, "_repr_svg_", "image/svg+xml")


def _from_png(value):
    return _from_repr_method(value, "_repr_png_", "image/png")


def _from_json(value):
    return _from_repr_method(value, "_repr_json_", "application/json")


# The page a widget is delivered in. Deliberately bare: the loader and the widget's own
# styling come with the snippet, and the surface showing this gives it its own frame, so
# anything added here would only compete with both. The background is left transparent so
# the notebook's own background shows through in either theme.
#
# The one thing it does say is what colour things are. The widget stylesheet arrives with a
# light theme written into it, black text on white, which is unreadable on a dark page and is
# not a value the widget author chose. The block below points those names at the colours the
# host puts on this document, so a widget follows the notebook the way everything else does.
# Two things make it work. The selector is doubled because the widget stylesheet is added to
# the page after this one and claims the same names at the strength of a single ':root'. And
# the values are read rather than copied, so when the host restates its colours on a theme
# change these follow with no further help. Each has a fallback that matches the stylesheet's
# own light theme, so a frame that is given no colours looks exactly as it did before.
WIDGET_DOCUMENT = """<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Widget</title>
<style>
html, body { margin: 0; padding: 0; background: transparent; }
:root:root {
  --jp-content-font-color0: var(--verso-cell-output-foreground, var(--verso-fg-default, black));
  --jp-content-font-color1: var(--verso-cell-output-foreground, var(--verso-fg-default, black));
  --jp-content-font-color2: var(--verso-fg-muted, rgba(0, 0, 0, 0.54));
  --jp-content-font-color3: var(--verso-fg-muted, rgba(0, 0, 0, 0.38));
  --jp-ui-font-color0: var(--verso-cell-output-foreground, var(--verso-fg-default, black));
  --jp-ui-font-color1: var(--verso-cell-output-foreground, var(--verso-fg-default, rgba(0, 0, 0, 0.8)));
  --jp-ui-font-color2: var(--verso-fg-muted, rgba(0, 0, 0, 0.54));
  --jp-ui-font-color3: var(--verso-fg-muted, rgba(0, 0, 0, 0.38));
  --jp-inverse-ui-font-color0: var(--verso-cell-output-background, var(--verso-bg-default, white));
  --jp-inverse-ui-font-color1: var(--verso-cell-output-background, var(--verso-bg-default, white));
  --jp-layout-color0: var(--verso-cell-output-background, var(--verso-bg-default, white));
  --jp-layout-color1: var(--verso-cell-output-background, var(--verso-bg-default, white));
  /* Not the page colour. These two are the face of a button, a tag, a progress bar, and the
     unfilled part of a slider track, so they have to stand off the surface behind them rather
     than match it. The border colour is the one value the host offers that is a step of
     contrast in both themes, which is exactly what these need to be. */
  --jp-layout-color2: var(--verso-border-default, #eeeeee);
  --jp-layout-color3: var(--verso-border-default, #e0e0e0);
  --jp-ui-layout-color1: var(--verso-cell-output-background, var(--verso-bg-default, white));
  --jp-border-color0: var(--verso-border-default, #bdbdbd);
  --jp-border-color1: var(--verso-border-default, #9e9e9e);
  --jp-border-color2: var(--verso-border-default, #e0e0e0);
  --jp-border-color3: var(--verso-border-default, #eeeeee);
  --jp-brand-color0: var(--verso-accent-primary, #1976d2);
  --jp-brand-color1: var(--verso-accent-primary, #2196f3);
  --jp-ui-font-family: var(--verso-ui-font-family, sans-serif);
  --jp-content-font-family: var(--verso-ui-font-family, sans-serif);
  --jp-code-font-family: var(--verso-font-family-mono, monospace);
}
</style>
</head>
<body>
{snippet}
</body>
</html>
"""

_CHAIN = (
    # Ahead of the bundle reader, which would otherwise settle for a widget's plain-text repr.
    _from_widget,
    _from_mimebundle,
    _from_html,
    _from_markdown,
    _from_latex,
    _from_svg,
    _from_png,
    _from_json,
)


def _payload(mime, data):
    if mime in BINARY_MIMES:
        encoded = _to_base64(data)
        return None if encoded is None else {"mime": mime, "data": encoded}

    if mime == "application/json":
        text = _to_json_text(data)
        return None if text is None else {"mime": mime, "data": text}

    return {"mime": mime, "data": _to_text(data)}


def _unwrap(rendered):
    """Repr methods may return the data alone or a (data, metadata) pair; keep the data."""
    if isinstance(rendered, tuple) and len(rendered) == 2 and isinstance(rendered[1], dict):
        return rendered[0]
    return rendered


def _to_text(data):
    if isinstance(data, str):
        return data
    if isinstance(data, (list, tuple)):
        return "".join(_to_text(part) for part in data)
    return str(data)


def _to_base64(data):
    if isinstance(data, (bytes, bytearray, memoryview)):
        return base64.b64encode(bytes(data)).decode("ascii")
    if isinstance(data, str):
        # Already encoded, which is how a mimebundle carries image data.
        return data.strip()
    return None


def _to_json_text(data):
    if isinstance(data, str):
        return data
    try:
        # allow_nan is off because the bare NaN and Infinity tokens Python would otherwise
        # write are not valid JSON, and the managing side refuses them.
        return json.dumps(data, allow_nan=False, default=str)
    except (TypeError, ValueError):
        return None


def _safe_repr(value):
    try:
        return repr(value)
    except Exception:
        return "<unprintable %s object>" % type(value).__name__


# --- the IPython.display shim -----------------------------------------------------------


class _Rich(object):
    """Carries a single rendering of an object, in one MIME form."""

    def __init__(self, data=""):
        self.data = data

    def __repr__(self):
        return "%s(%r)" % (type(self).__name__, self.data)


class HTML(_Rich):
    def _repr_html_(self):
        return self.data


class Markdown(_Rich):
    def _repr_markdown_(self):
        return self.data


class Latex(_Rich):
    def _repr_latex_(self):
        return self.data


class SVG(_Rich):
    def _repr_svg_(self):
        return self.data


class JSON(_Rich):
    def _repr_json_(self):
        return self.data


class Image(object):
    """
    An image from bytes, a file, or a URL. All three render as an HTML element, which is
    also how the managing side presents raster data, so nothing is lost by the uniformity.
    """

    def __init__(self, data=None, url=None, filename=None, format=None, **kwargs):
        if data is None and filename is not None:
            with open(filename, "rb") as handle:
                data = handle.read()

        self.data = data
        self.url = url
        self.filename = filename
        self.format = (format or _sniff_image_format(data) or "png").lower()

    def _repr_html_(self):
        if self.url:
            return '<img src="%s" />' % self.url
        if isinstance(self.data, (bytes, bytearray, memoryview)):
            encoded = base64.b64encode(bytes(self.data)).decode("ascii")
            return '<img src="data:image/%s;base64,%s" />' % (self.format, encoded)
        return None

    def __repr__(self):
        return "<Image %s>" % (self.url or self.filename or self.format)


def clear_output(wait=False):
    """
    Empty the output area collecting on this thread, when there is one.

    Called inside ``with area:`` it does what it says, which is also what makes
    ``area.capture(clear_output=True)`` work: that is how a callback that fires on every
    keystroke keeps its area from growing without end. Anywhere else it is accepted and does
    nothing, because Verso has no way to retract output already written to a cell and
    clearing would be a claim the host cannot honor.
    """
    capture_clear(wait)
    return None


def get_ipython():
    """
    Always None, which is what real IPython answers outside a shell.

    Libraries treat an importable IPython as a hint that one may be running and ask this
    before taking an IPython-specific path. matplotlib does exactly that while selecting a
    backend, so the stand-in has to answer rather than raise.
    """
    return None


# Reported to libraries that gate legacy workarounds on an IPython version. A recent one is
# the honest answer: none of those workarounds apply here, and the shell hooks they patch do
# not exist in this host.
SHIM_VERSION_INFO = (8, 24, 0, "")
SHIM_VERSION = "8.24.0"


def _sniff_image_format(data):
    if not isinstance(data, (bytes, bytearray)):
        return None
    header = bytes(data[:8])
    if header.startswith(b"\x89PNG"):
        return "png"
    if header.startswith(b"\xff\xd8"):
        return "jpeg"
    if header.startswith(b"GIF8"):
        return "gif"
    return None


def build_shim_module():
    """Assemble the stand-in ``IPython.display`` module."""
    module = types.ModuleType("IPython.display")
    module.display = display
    module.HTML = HTML
    module.Markdown = Markdown
    module.Latex = Latex
    module.SVG = SVG
    module.JSON = JSON
    module.Image = Image
    module.clear_output = clear_output
    module.__all__ = [
        "display", "HTML", "Markdown", "Latex", "SVG", "JSON", "Image", "clear_output"]
    return module


def build_ipython_package():
    """Assemble the stand-in ``IPython`` package that carries the display module."""
    package = types.ModuleType("IPython")
    package.__path__ = []  # marks it a package, so "import IPython.display" resolves
    package.get_ipython = get_ipython
    package.version_info = SHIM_VERSION_INFO
    package.__version__ = SHIM_VERSION
    return package


def _install_ipython_shim():
    if _module_available("IPython"):
        # A real IPython is left as it is: its display objects already carry the dunder reprs
        # the chain reads. Only the entry point is redirected, so a call made outside an
        # IPython shell still reaches the notebook. The redirect waits for the first import
        # rather than forcing one, because importing IPython at startup is not cheap.
        already = sys.modules.get("IPython.display")
        if already is not None:
            _redirect_ipython_display(already)
        else:
            watch_import("IPython.display", _redirect_ipython_display)
        return

    install_shim_finder()


def _redirect_ipython_display(module):
    try:
        module.display = display
    except Exception:
        pass


class ShimLoader(object):
    """Hands back an already-assembled module, so there is nothing to execute."""

    def __init__(self, builder, is_package):
        self._builder = builder
        self._is_package = is_package

    def is_package(self, fullname):
        return self._is_package

    def create_module(self, spec):
        return self._builder()

    def exec_module(self, module):
        return None


class ShimFinder(object):
    """
    Serves the display names only when a cell asks for them.

    Registering them up front would put IPython in ``sys.modules`` for every session, and
    libraries read that as a sign a shell may be running: matplotlib, for one, changes how it
    selects a backend. A notebook that never mentions IPython should look like what it is.
    """

    BUILDERS = {
        "IPython": (build_ipython_package, True),
        "IPython.display": (build_shim_module, False),
    }

    def find_spec(self, fullname, path=None, target=None):
        entry = self.BUILDERS.get(fullname)
        if entry is None:
            return None

        builder, is_package = entry
        return importlib.util.spec_from_loader(fullname, ShimLoader(builder, is_package))


def install_shim_finder():
    if any(isinstance(finder, ShimFinder) for finder in sys.meta_path):
        return

    # Appended rather than inserted: a real IPython installed later in the session is found
    # by the ordinary machinery first, and this only answers when nothing else does.
    sys.meta_path.append(ShimFinder())


def _module_available(name):
    """Whether a module can be imported, without importing it."""
    try:
        return importlib.util.find_spec(name) is not None
    except Exception:
        return False


# --- matplotlib -------------------------------------------------------------------------


def flush_figures():
    """Emit every open figure as an image and close them. Safe when matplotlib is absent."""
    pyplot = sys.modules.get(PYPLOT_MODULE)
    if pyplot is None:
        return

    try:
        numbers = list(pyplot.get_fignums())
    except Exception:
        return

    if not numbers:
        return

    for number in numbers:
        try:
            figure = pyplot.figure(number)
            buffer = io.BytesIO()
            figure.savefig(buffer, format="png", bbox_inches="tight")
            emit("image/png", base64.b64encode(buffer.getvalue()).decode("ascii"))
        except Exception:
            continue

    try:
        pyplot.close("all")
    except Exception:
        pass


def patch_pyplot(pyplot):
    """Make ``show()`` publish the current figures instead of opening a window."""
    if getattr(getattr(pyplot, "show", None), "_verso_patched", False):
        return

    def show(*args, **kwargs):
        flush_figures()

    show._verso_patched = True
    pyplot.show = show


def _install_matplotlib_hook():
    # Selected through the environment so matplotlib picks it up on import without this
    # process having to import matplotlib to say so.
    os.environ.setdefault("MPLBACKEND", "Agg")

    pyplot = sys.modules.get(PYPLOT_MODULE)
    if pyplot is not None:
        patch_pyplot(pyplot)
        return

    # Patching on import rather than between cells means a figure shown in the same cell
    # that imports matplotlib still appears at the show() call, not after the cell ends.
    watch_import(PYPLOT_MODULE, patch_pyplot)


# --- post-import hooks ------------------------------------------------------------------


class _WrappedLoader(object):
    """Runs a callback once a watched module has finished loading."""

    def __init__(self, inner, on_loaded):
        self._inner = inner
        self._on_loaded = on_loaded

    def create_module(self, spec):
        return self._inner.create_module(spec)

    def exec_module(self, module):
        self._inner.exec_module(module)
        try:
            self._on_loaded(module)
        except Exception:
            pass

    def __getattr__(self, name):
        return getattr(self._inner, name)


class ImportWatcher(object):
    """
    A meta-path entry that leaves every import alone except one named module, whose loader
    it wraps so a callback runs the moment that module is ready. Python has no post-import
    hook, and polling ``sys.modules`` between cells would miss an import and a use that
    share a cell.
    """

    def __init__(self, module_name, on_loaded):
        self.module_name = module_name
        self._on_loaded = on_loaded

    def find_spec(self, fullname, path=None, target=None):
        if fullname != self.module_name:
            return None

        for finder in list(sys.meta_path):
            if finder is self:
                continue
            find_spec = getattr(finder, "find_spec", None)
            if find_spec is None:
                continue
            try:
                spec = find_spec(fullname, path, target)
            except Exception:
                continue
            if spec is not None and spec.loader is not None:
                spec.loader = _WrappedLoader(spec.loader, self._on_loaded)
                return spec

        return None


def watch_import(module_name, on_loaded):
    for finder in sys.meta_path:
        if isinstance(finder, ImportWatcher) and finder.module_name == module_name:
            return
    sys.meta_path.insert(0, ImportWatcher(module_name, on_loaded))
