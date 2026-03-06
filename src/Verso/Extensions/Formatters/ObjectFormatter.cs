using System.Net;
using System.Reflection;
using System.Text;
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
        var type = value.GetType();
        var members = GetMemberValues(type, value);

        var sb = new StringBuilder();
        sb.Append("<table><thead><tr><th>Member</th><th>Value</th></tr></thead><tbody>");

        foreach (var (name, memberValue) in members)
        {
            sb.Append("<tr><td>")
              .Append(WebUtility.HtmlEncode(name))
              .Append("</td><td>")
              .Append(WebUtility.HtmlEncode(memberValue?.ToString() ?? ""))
              .Append("</td></tr>");
        }

        sb.Append("</tbody></table>");

        return Task.FromResult(new CellOutput("text/html", sb.ToString()));
    }

    private static bool HasPublicMembers(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                   .Any(p => p.CanRead && p.GetIndexParameters().Length == 0)
            || type.GetFields(BindingFlags.Public | BindingFlags.Instance).Length > 0;
    }

    private static (string Name, object? Value)[] GetMemberValues(Type type, object value)
    {
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Select(p => (p.Name, Value: (object?)p.GetValue(value)));

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Select(f => (f.Name, Value: (object?)f.GetValue(value)));

        return props.Concat(fields).ToArray();
    }
}
