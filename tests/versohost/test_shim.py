"""Coverage for the IPython.display stand-in and the matplotlib hook."""

import sys
import types

import pytest

import _versohost_display as display_support


@pytest.fixture
def clean_modules():
    """Restore sys.modules and sys.meta_path, both of which installation mutates."""
    saved_modules = dict(sys.modules)
    saved_meta_path = list(sys.meta_path)
    yield
    sys.modules.clear()
    sys.modules.update(saved_modules)
    sys.meta_path[:] = saved_meta_path


def test_the_shim_stays_out_of_sys_modules_until_it_is_imported(clean_modules, monkeypatch):
    monkeypatch.setattr(display_support, "_module_available", lambda name: False)
    sys.modules.pop("IPython", None)
    sys.modules.pop("IPython.display", None)

    display_support._install_ipython_shim()

    # Libraries read an imported IPython as a sign a shell may be running, so a notebook that
    # never mentions it must not acquire one.
    assert "IPython" not in sys.modules


def test_the_shim_supports_importing_names_from_it(clean_modules, monkeypatch):
    monkeypatch.setattr(display_support, "_module_available", lambda name: False)
    sys.modules.pop("IPython", None)
    sys.modules.pop("IPython.display", None)

    display_support._install_ipython_shim()

    from IPython.display import HTML, Markdown, clear_output

    assert HTML("<b>x</b>")._repr_html_() == "<b>x</b>"
    assert Markdown("# x")._repr_markdown_() == "# x"
    assert clear_output() is None
    assert sys.modules["IPython.display"].display is display_support.display


def test_the_shim_answers_the_questions_libraries_ask_of_ipython(clean_modules, monkeypatch):
    monkeypatch.setattr(display_support, "_module_available", lambda name: False)
    sys.modules.pop("IPython", None)
    sys.modules.pop("IPython.display", None)

    display_support._install_ipython_shim()

    import IPython

    # matplotlib asks both of these while choosing a backend, and raising would fail the cell.
    assert IPython.get_ipython() is None
    assert IPython.version_info[:2] >= (8, 24)


def test_a_real_ipython_keeps_its_own_module(clean_modules, monkeypatch):
    installed = types.ModuleType("IPython.display")
    installed.display = lambda obj: None
    package = types.ModuleType("IPython")
    package.__path__ = []
    package.display = installed

    monkeypatch.setitem(sys.modules, "IPython", package)
    monkeypatch.setitem(sys.modules, "IPython.display", installed)
    monkeypatch.setattr(display_support, "_module_available", lambda name: True)

    display_support._install_ipython_shim()

    # The module itself is untouched, but its entry point now reaches the notebook.
    assert sys.modules["IPython.display"] is installed
    assert installed.display is display_support.display


def test_the_wrappers_carry_the_matching_repr_methods():
    assert display_support.Latex("$x$")._repr_latex_() == "$x$"
    assert display_support.SVG("<svg />")._repr_svg_() == "<svg />"
    assert display_support.JSON({"a": 1})._repr_json_() == {"a": 1}


def test_an_image_from_bytes_renders_as_an_element():
    image = display_support.Image(data=b"\x89PNG\r\n\x1a\nbody")

    rendered = image._repr_html_()

    assert rendered.startswith('<img src="data:image/png;base64,')


def test_an_image_from_a_url_renders_as_a_reference():
    image = display_support.Image(url="https://example.invalid/cat.png")

    assert image._repr_html_() == '<img src="https://example.invalid/cat.png" />'


def test_patching_pyplot_makes_show_publish_the_open_figures(monkeypatch):
    emitted = []
    monkeypatch.setattr(
        display_support, "_emitter", lambda mime, data: emitted.append((mime, data)))

    pyplot = _fake_pyplot()
    display_support.patch_pyplot(pyplot)
    monkeypatch.setitem(sys.modules, display_support.PYPLOT_MODULE, pyplot)

    pyplot.show()

    assert [mime for mime, _ in emitted] == ["image/png"]
    assert pyplot.closed


def test_patching_pyplot_twice_leaves_one_patch(monkeypatch):
    pyplot = _fake_pyplot()
    display_support.patch_pyplot(pyplot)
    first = pyplot.show
    display_support.patch_pyplot(pyplot)

    assert pyplot.show is first


def test_flushing_figures_without_matplotlib_is_a_no_op(monkeypatch):
    monkeypatch.delitem(sys.modules, display_support.PYPLOT_MODULE, raising=False)

    display_support.flush_figures()


def test_the_import_watcher_is_registered_for_pyplot(clean_modules, monkeypatch):
    monkeypatch.delitem(sys.modules, display_support.PYPLOT_MODULE, raising=False)

    display_support._install_matplotlib_hook()

    watched = [
        finder.module_name for finder in sys.meta_path
        if isinstance(finder, display_support.ImportWatcher)
    ]
    assert display_support.PYPLOT_MODULE in watched


def test_the_import_watcher_is_registered_only_once(clean_modules, monkeypatch):
    monkeypatch.delitem(sys.modules, display_support.PYPLOT_MODULE, raising=False)

    display_support._install_matplotlib_hook()
    display_support._install_matplotlib_hook()

    watched = [
        finder.module_name for finder in sys.meta_path
        if isinstance(finder, display_support.ImportWatcher)
    ]
    assert watched.count(display_support.PYPLOT_MODULE) == 1


def test_the_import_watcher_runs_the_callback_when_the_module_loads(clean_modules):
    loaded = []
    display_support.watch_import("json.decoder", loaded.append)

    sys.modules.pop("json.decoder", None)
    import json.decoder  # noqa: F401  (the import is the behaviour under test)

    assert [module.__name__ for module in loaded] == ["json.decoder"]


def _fake_pyplot():
    """A stand-in for pyplot with one open figure, so matplotlib is not a test dependency."""
    class Figure:
        def savefig(self, buffer, format=None, bbox_inches=None):
            buffer.write(b"\x89PNGfake")

    module = types.ModuleType("matplotlib.pyplot")
    module.closed = False

    def close(which):
        module.closed = True

    module.get_fignums = lambda: [1]
    module.figure = lambda number: Figure()
    module.close = close
    module.show = _unpatched_show
    return module


def _unpatched_show(*args, **kwargs):
    raise AssertionError("the original show() was called")
