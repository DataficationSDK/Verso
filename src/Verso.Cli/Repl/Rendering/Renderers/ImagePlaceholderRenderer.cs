using Spectre.Console;
using Spectre.Console.Rendering;
using Verso.Abstractions;

namespace Verso.Cli.Repl.Rendering.Renderers;

/// <summary>
/// Terminals cannot render pixel images. We decode the base64 payload to a temp
/// file under <c>$TMPDIR</c> and return a placeholder renderable that includes
/// the file path so users can open it separately. SVG is written as-is.
/// </summary>
internal static class ImagePlaceholderRenderer
{
    public static IRenderable AsRenderable(CellOutput output, int cellCounter, int index, bool useColor)
    {
        var ext = output.MimeType switch
        {
            "image/png" => "png",
            "image/jpeg" => "jpg",
            "image/gif" => "gif",
            "image/webp" => "webp",
            "image/bmp" => "bmp",
            "image/svg+xml" => "svg",
            _ => "bin"
        };

        var fileName = $"verso-repl-{cellCounter}-{index}.{ext}";
        var path = Path.Combine(Path.GetTempPath(), fileName);

        try
        {
            if (output.MimeType == "image/svg+xml")
                File.WriteAllText(path, output.Content ?? string.Empty);
            else
                File.WriteAllBytes(path, Convert.FromBase64String(output.Content ?? string.Empty));
        }
        catch (Exception ex)
        {
            var msg = $"<image: {output.MimeType} — failed to decode: {ex.Message}>";
            return useColor
                ? new Markup($"[yellow]{Markup.Escape(msg)}[/]")
                : new Text(msg);
        }

        var info = new FileInfo(path);
        var size = FormatSize(info.Length);
        if (useColor)
            return new Markup(
                $"[dim]<image: {Markup.Escape(output.MimeType)}, {size} — saved to [/]" +
                $"[cyan]{Markup.Escape(path)}[/][dim]>[/]");
        return new Text($"<image: {output.MimeType}, {size} — saved to {path}>");
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.#} MB";
    }
}
