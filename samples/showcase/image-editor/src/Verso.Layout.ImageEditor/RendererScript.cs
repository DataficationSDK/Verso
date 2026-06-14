using System.Text;

namespace Verso.Layout.ImageEditor;

/// <summary>
/// Loads the embedded <c>main.js</c> renderer module shipped in the layout's
/// <see cref="Verso.Abstractions.LayoutRendererPackage"/>. Embedding keeps the extension a
/// single self-contained assembly with no loose files to deploy.
/// </summary>
internal static class RendererScript
{
    private const string ResourceName = "Verso.Layout.ImageEditor.main.js";

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
