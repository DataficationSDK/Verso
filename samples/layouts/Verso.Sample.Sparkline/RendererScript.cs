using System.IO;
using System.Text;

namespace Verso.Sample.Sparkline;

/// <summary>
/// Loads the embedded <c>main.js</c> renderer module that the layout ships in its
/// <see cref="LayoutRendererPackage"/>. The script is embedded so the extension is a
/// single self-contained assembly with no loose files to deploy.
/// </summary>
internal static class RendererScript
{
    private const string ResourceName = "Verso.Sample.Sparkline.main.js";

    /// <summary>The renderer entry module source, loaded once on first access.</summary>
    public static string MainJs { get; } = Load();

    private static string Load()
    {
        var assembly = typeof(RendererScript).Assembly;

        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded renderer resource '{ResourceName}' was not found in the assembly.");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
