using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Verso.Layout.FormStudio;

/// <summary>
/// Assembles the layout's single self-contained renderer module from the embedded assets: the
/// vendored chart library (Chart.js) and our own <c>form.js</c>. The result is one JavaScript
/// module string the host loads as the renderer package entry point. Embedding keeps the
/// extension a single deployable assembly.
/// </summary>
/// <remarks>
/// Chart.js ships as a UMD bundle that binds its global through <c>this</c> at the top level —
/// semantics that only hold in a classic script executed at global scope, not inside an ES
/// module (where top-level <c>this</c> is undefined). So the entry module ships the library
/// source as a string constant and <c>form.js</c> injects it as a classic <c>&lt;script&gt;</c>
/// element at runtime, an inline script the frame's base policy permits.
/// </remarks>
internal static class RendererScript
{
    private const string FormResource = "Verso.Layout.FormStudio.form.js";
    private const string ChartJsResource = "Verso.Layout.FormStudio.vendor.chart.umd.js";

    /// <summary>The assembled renderer entry module, built once on first access.</summary>
    public static string MainJs { get; } = Assemble();

    private static string Assemble()
    {
        var chartJs = Load(ChartJsResource);
        var formJs = Load(FormResource);

        // JsonSerializer emits a valid, fully escaped JavaScript string literal — the safe way
        // to embed arbitrary source (quotes, newlines, "</script>") as a const.
        var builder = new StringBuilder(chartJs.Length + formJs.Length + 256);
        builder.Append("// Form Studio renderer bundle — assembled by RendererScript at runtime.\n");
        builder.Append("const __FORM_CHARTJS__ = ").Append(Encode(chartJs)).Append(";\n");
        builder.Append(formJs);
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
