using Verso.Abstractions;

namespace Verso.Extensions.Formatters;

/// <summary>
/// Formats arbitrary objects as HTML tables showing public properties and fields.
/// Acts as a catch-all for types not handled by more specific formatters.
/// </summary>
[VersoExtension]
public sealed class ObjectFormatter : IDataFormatter
{
    private static readonly HashSet<Type> ExcludedTypes = new()
    {
        typeof(string), typeof(int), typeof(long), typeof(float), typeof(double),
        typeof(decimal), typeof(bool), typeof(DateTime), typeof(DateTimeOffset),
        typeof(char), typeof(Guid), typeof(byte), typeof(short), typeof(ushort),
        typeof(uint), typeof(ulong), typeof(sbyte)
    };

    // --- IExtension ---

    public string ExtensionId => "verso.formatter.object";
    public string Name => "Object Formatter";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Formats objects as HTML tables showing public properties and fields.";

    // --- IDataFormatter ---

    public IReadOnlyList<Type> SupportedTypes { get; } = new[] { typeof(object) };
    public int Priority => 5;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public bool CanFormat(object value, IFormatterContext context)
    {
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || ExcludedTypes.Contains(type))
            return false;

        return HasPublicMembers(type);
    }

    public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
    {
        var html = ObjectTreeRenderer.RenderObject(value, value.GetType());
        return Task.FromResult(new CellOutput("text/html", html));
    }

    private static bool HasPublicMembers(Type type)
    {
        return ObjectTreeRenderer.HasPublicMembers(type);
    }
}
