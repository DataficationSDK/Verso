namespace Verso.Python.Kernel;

/// <summary>
/// Holds the Python bootstrap code that is executed once when the kernel scope is created.
/// Sets up stdout/stderr capture, a <c>display()</c> function, and optional matplotlib/IPython hooks.
/// </summary>
internal static class OutputCapture
{
    /// <summary>
    /// Matplotlib/IPython hook code. Extracted as a separate constant so the kernel can
    /// re-execute it after <c>#!pip</c> adds new packages to <c>sys.path</c>.
    /// At init time matplotlib may not be installed yet, so the <c>ImportError</c>
    /// catch allows it to be silently skipped and retried later.
    /// </summary>
    internal const string LibraryHooksCode = @"
# --- matplotlib hook ---
try:
    import matplotlib
    matplotlib.use('Agg')  # must be before pyplot import; warns if already imported
    import matplotlib.pyplot as _verso_plt
    try:
        _verso_plt.switch_backend('Agg')  # fallback: works even after pyplot import
    except Exception:
        pass

    # Always re-patch plt.show so the function's __globals__ binds to the
    # current scope's _verso_display_outputs. Without this, a kernel restart
    # creates a new scope (and new display list) but the old patched show
    # still writes to the previous scope's list — producing no visible output.
    def _verso_patched_show(*args, **kwargs):
        import base64
        for _fn in _verso_plt.get_fignums():
            _fig = _verso_plt.figure(_fn)
            _buf = io.BytesIO()
            _fig.savefig(_buf, format='png', bbox_inches='tight')
            _buf.seek(0)
            _b64 = base64.b64encode(_buf.read()).decode('ascii')
            _verso_display_outputs.append(('text/html', f'<img src=""data:image/png;base64,{_b64}"" />'))
            _buf.close()
        _verso_plt.close('all')
    _verso_patched_show._verso_patched = True
    _verso_plt.show = _verso_patched_show
except Exception:
    pass

# --- IPython.display hook (conditional) ---
try:
    import IPython.display as _verso_ipd
    _verso_ipd.display = display
except ImportError:
    pass
";

    /// <summary>
    /// Post-execution code that captures any remaining open matplotlib figures.
    /// Runs after user code and before output drain so that plots created without
    /// an explicit <c>plt.show()</c> call are still rendered.
    /// </summary>
    internal const string MatplotlibCaptureCode = @"
if 'matplotlib.pyplot' in sys.modules:
    try:
        import matplotlib.pyplot as _mpl_cap
        _fignums = _mpl_cap.get_fignums()
        if _fignums:
            import base64 as _b64_cap
            for _fn in _fignums:
                _fig = _mpl_cap.figure(_fn)
                _buf = io.BytesIO()
                _fig.savefig(_buf, format='png', bbox_inches='tight')
                _buf.seek(0)
                _b64_str = _b64_cap.b64encode(_buf.read()).decode('ascii')
                _verso_display_outputs.append(('text/html', f'<img src=""data:image/png;base64,{_b64_str}"" />'))
                _buf.close()
            _mpl_cap.close('all')
    except Exception:
        pass
";

    /// <summary>
    /// Python bootstrap code executed in the kernel scope at initialization.
    /// </summary>
    public const string BootstrapCode = @"
import sys, io

# --- stdout / stderr capture ---
_verso_stdout = io.StringIO()
_verso_stderr = io.StringIO()
sys.stdout = _verso_stdout
sys.stderr = _verso_stderr

# --- rich display queue ---
_verso_display_outputs = []

def display(obj):
    # Route an object through rich-output detection into the display queue.
    if obj is None:
        return

    if hasattr(obj, '_repr_html_') and callable(obj._repr_html_):
        html = obj._repr_html_()
        if html is not None:
            _verso_display_outputs.append(('text/html', str(html)))
            return

    if hasattr(obj, '_repr_png_') and callable(obj._repr_png_):
        png_data = obj._repr_png_()
        if png_data is not None:
            import base64
            b64 = base64.b64encode(png_data).decode('ascii')
            _verso_display_outputs.append(('text/html', f'<img src=""data:image/png;base64,{b64}"" />'))
            return

    if hasattr(obj, '_repr_svg_') and callable(obj._repr_svg_):
        svg = obj._repr_svg_()
        if svg is not None:
            _verso_display_outputs.append(('text/html', str(svg)))
            return

    _verso_display_outputs.append(('text/plain', str(obj)))
" + LibraryHooksCode + @"
# --- flush helpers ---
def _verso_flush_stdout():
    global _verso_stdout
    val = _verso_stdout.getvalue()
    _verso_stdout = io.StringIO()
    sys.stdout = _verso_stdout
    return val

def _verso_flush_stderr():
    global _verso_stderr
    val = _verso_stderr.getvalue()
    _verso_stderr = io.StringIO()
    sys.stderr = _verso_stderr
    return val

def _verso_flush_display():
    global _verso_display_outputs
    items = list(_verso_display_outputs)
    _verso_display_outputs.clear()
    return items
";
}
