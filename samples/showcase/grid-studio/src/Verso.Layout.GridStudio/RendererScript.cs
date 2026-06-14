using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Verso.Layout.GridStudio;

/// <summary>
/// Assembles the layout's single self-contained renderer module from the embedded assets:
/// the vendored grid library (Jspreadsheet CE), its helper library (jsuites), their CSS, and
/// our own <c>grid.js</c>. The result is one JavaScript module string the host loads as the
/// renderer package entry point. Embedding keeps the extension a single deployable assembly.
/// </summary>
/// <remarks>
/// The vendored libraries are UMD bundles that bind their global through <c>this</c> and use
/// <c>var</c> at the top level to alias an existing global. Those semantics only hold in a
/// classic script executed at global scope, not inside an ES module (where top-level
/// <c>this</c> is undefined and <c>var</c> is module-scoped). So the entry module ships the
/// library sources as string constants and <c>grid.js</c> injects them as classic
/// <c>&lt;script&gt;</c> elements at runtime — inline scripts the frame's base policy permits.
/// </remarks>
internal static class RendererScript
{
    private const string GridResource = "Verso.Layout.GridStudio.grid.js";
    private const string JspreadsheetJsResource = "Verso.Layout.GridStudio.vendor.jspreadsheet.js";
    private const string JspreadsheetCssResource = "Verso.Layout.GridStudio.vendor.jspreadsheet.css";
    private const string JsuitesJsResource = "Verso.Layout.GridStudio.vendor.jsuites.js";
    private const string JsuitesCssResource = "Verso.Layout.GridStudio.vendor.jsuites.css";

    /// <summary>The assembled renderer entry module, built once on first access.</summary>
    public static string MainJs { get; } = Assemble();

    private static string Assemble()
    {
        var jsuitesJs = Load(JsuitesJsResource);
        var jspreadsheetJs = Load(JspreadsheetJsResource);
        var gridJs = Load(GridResource);
        // jsuites styles first so the grid's own rules win on any shared selector.
        var vendorCss = Load(JsuitesCssResource) + "\n" + Load(JspreadsheetCssResource);

        // JsonSerializer emits a valid, fully escaped JavaScript string literal — the safe way
        // to embed arbitrary source (quotes, newlines, "</script>") as a const.
        var builder = new StringBuilder(jsuitesJs.Length + jspreadsheetJs.Length + gridJs.Length + vendorCss.Length + 256);
        builder.Append("// Grid Studio renderer bundle — assembled by RendererScript at runtime.\n");
        builder.Append("const __GRID_JSUITES__ = ").Append(Encode(jsuitesJs)).Append(";\n");
        builder.Append("const __GRID_JSPREADSHEET__ = ").Append(Encode(jspreadsheetJs)).Append(";\n");
        builder.Append("const __GRID_VENDOR_CSS__ = ").Append(Encode(vendorCss)).Append(";\n");
        builder.Append(gridJs);
        return builder.ToString();
    }

    private static string Encode(string value) => JsonSerializer.Serialize(value);

    private static string Load(string resourceName)
    {
        var assembly = typeof(RendererScript).Assembly;

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded renderer resource '{resourceName}' was not found in the assembly.");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
