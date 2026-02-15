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
    public string Version => "0.5.0";
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
        var mime = DetectMimeType(bytes);
        var base64 = Convert.ToBase64String(bytes);
        var html = $"<img src=\"data:{mime};base64,{base64}\" style=\"max-width:100%;\" />";
        return Task.FromResult(new CellOutput("text/html", html));
    }

    internal static string DetectMimeType(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        if (bytes.Length >= 3 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            return "image/gif";
        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
            return "image/bmp";
        if (bytes.Length >= 12 && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
            return "image/webp";
        return "image/png";
    }
}
