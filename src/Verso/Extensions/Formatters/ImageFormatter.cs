using Verso.Abstractions;

namespace Verso.Extensions.Formatters;

/// <summary>
/// Formats byte arrays as inline base64-encoded PNG images.
/// </summary>
[VersoExtension]
public sealed class ImageFormatter : IDataFormatter
{
    // --- IExtension ---

    public string ExtensionId => "verso.formatter.image";
    public string Name => "Image Formatter";
    public string Version => "0.1.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Formats byte arrays as inline base64 images.";

    // --- IDataFormatter ---

    public IReadOnlyList<Type> SupportedTypes { get; } = new[] { typeof(byte[]) };
    public int Priority => 15;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public bool CanFormat(object value, IFormatterContext context)
    {
        return value is byte[];
    }

    public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
    {
        var bytes = (byte[])value;
        var base64 = Convert.ToBase64String(bytes);
        var html = $"<img src=\"data:image/png;base64,{base64}\" />";
        return Task.FromResult(new CellOutput("text/html", html));
    }
}
