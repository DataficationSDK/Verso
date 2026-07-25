"""Rich display support for the Verso Python host.

Reduces a value to a single MIME payload through the repr chain notebooks conventionally
implement, provides the ``IPython.display`` names when IPython itself is not installed, and
routes matplotlib figures into the notebook as they are drawn. Standard library only.

The module is imported by ``versohost.py`` and shares its process, but keeps no state beyond
the emitter callback registered at startup.
"""

import base64
import importlib.util
import io
import json
import os
import sys
import types

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

_emitter = None


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


def emit(mime, data):
    if _emitter is not None:
        _emitter(mime, data)


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

    emit(payload["mime"], payload["data"])


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
        if payload is not None:
            return payload

    return {"mime": "text/plain", "data": _safe_repr(value)}


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


_CHAIN = (
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
    Accepted for compatibility and does nothing. Verso has no way to retract output that
    has already been written to a cell, so clearing would be a claim the host cannot honor.
    """
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
