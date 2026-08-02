using System.Reflection;
using Verso.Abstractions;
using Verso.Resources;

namespace Verso.Extensions.Formatters;

/// <summary>
/// Formats objects that expose a public parameterless ToHtml() method.
/// </summary>
[VersoExtension]
public sealed class HtmlFormatter : IDataFormatter
{
    // --- IExtension ---

    public string ExtensionId => "verso.formatter.html";
    public string Name => Strings.Formatter_Html;
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => Strings.Formatter_Html_Description;

    // --- IDataFormatter ---

    public IReadOnlyList<Type> SupportedTypes { get; } = new[] { typeof(object) };
    public int Priority => 20;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public bool CanFormat(object value, IFormatterContext context)
    {
        return GetToHtmlMethod(value.GetType()) is not null;
    }

    public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
    {
        var method = GetToHtmlMethod(value.GetType())!;
        var html = (string)method.Invoke(value, null)!;
        return Task.FromResult(new CellOutput("text/html", html));
    }

    private static MethodInfo? GetToHtmlMethod(Type type)
    {
        return type.GetMethod("ToHtml", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
    }
}
