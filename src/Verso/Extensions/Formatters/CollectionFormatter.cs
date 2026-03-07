using System.Collections;
using Verso.Abstractions;

namespace Verso.Extensions.Formatters;

/// <summary>
/// Formats IEnumerable collections as HTML tables with property-inferred column headers.
/// </summary>
[VersoExtension]
public sealed class CollectionFormatter : IDataFormatter
{
    // --- IExtension ---

    public string ExtensionId => "verso.formatter.collection";
    public string Name => "Collection Formatter";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Formats collections as HTML tables.";

    // --- IDataFormatter ---

    public IReadOnlyList<Type> SupportedTypes { get; } = new[] { typeof(IEnumerable) };
    public int Priority => 10;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public bool CanFormat(object value, IFormatterContext context)
    {
        return value is IEnumerable && value is not string;
    }

    public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
    {
        var html = ObjectTreeRenderer.RenderCollection((IEnumerable)value);
        return Task.FromResult(new CellOutput("text/html", html));
    }
}
