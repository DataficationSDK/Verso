using Verso.Abstractions;

namespace Verso.Extensions.Formatters;

/// <summary>
/// Formats primitive and common value types as plain text.
/// </summary>
[VersoExtension]
public sealed class PrimitiveFormatter : IDataFormatter
{
    private static readonly HashSet<Type> PrimitiveTypes = new()
    {
        typeof(string),
        typeof(int),
        typeof(long),
        typeof(float),
        typeof(double),
        typeof(decimal),
        typeof(bool),
        typeof(DateTime),
        typeof(DateTimeOffset),
        typeof(char),
        typeof(Guid)
    };

    // --- IExtension ---

    public string ExtensionId => "verso.formatter.primitive";
    public string Name => "Primitive Formatter";
    public string Version => "0.1.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Formats primitive and common value types as plain text.";

    // --- IDataFormatter ---

    public IReadOnlyList<Type> SupportedTypes { get; } = PrimitiveTypes.ToList();
    public int Priority => 0;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public bool CanFormat(object value, IFormatterContext context)
    {
        return PrimitiveTypes.Contains(value.GetType());
    }

    public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
    {
        return Task.FromResult(new CellOutput("text/plain", value.ToString() ?? ""));
    }
}
