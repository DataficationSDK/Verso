using Verso.Abstractions;

namespace Verso.Extensions.Formatters;

/// <summary>
/// Formats SVG strings as inline HTML output.
/// </summary>
[VersoExtension]
public sealed class SvgFormatter : IDataFormatter
{
    // --- IExtension ---

    public string ExtensionId => "verso.formatter.svg";
    public string Name => "SVG Formatter";
    public string Version => "0.5.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Formats SVG strings as inline HTML.";

    // --- IDataFormatter ---

    public IReadOnlyList<Type> SupportedTypes { get; } = new[] { typeof(string) };
    public int Priority => 18;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public bool CanFormat(object value, IFormatterContext context)
    {
        return value is string s && s.TrimStart().StartsWith("<svg", StringComparison.OrdinalIgnoreCase);
    }

    public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
    {
        var svgContent = (string)value;
        var html = $"<div class=\"verso-svg-output\" style=\"max-width:100%;overflow:auto;\">{svgContent}</div>";
        return Task.FromResult(new CellOutput("text/html", html));
    }
}
