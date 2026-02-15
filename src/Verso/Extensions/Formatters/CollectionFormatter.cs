using System.Collections;
using System.Net;
using System.Reflection;
using System.Text;
using Verso.Abstractions;

namespace Verso.Extensions.Formatters;

/// <summary>
/// Formats IEnumerable collections as HTML tables with property-inferred column headers.
/// </summary>
[VersoExtension]
public sealed class CollectionFormatter : IDataFormatter
{
    private const int MaxRows = 100;

    private static readonly HashSet<Type> PrimitiveLikeTypes = new()
    {
        typeof(string), typeof(int), typeof(long), typeof(float), typeof(double),
        typeof(decimal), typeof(bool), typeof(DateTime), typeof(DateTimeOffset),
        typeof(char), typeof(Guid), typeof(byte), typeof(short), typeof(ushort),
        typeof(uint), typeof(ulong), typeof(sbyte)
    };

    // --- IExtension ---

    public string ExtensionId => "verso.formatter.collection";
    public string Name => "Collection Formatter";
    public string Version => "0.5.0";
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
        var enumerable = (IEnumerable)value;
        var items = new List<object?>();
        foreach (var item in enumerable)
        {
            items.Add(item);
            if (items.Count > MaxRows)
                break;
        }

        if (items.Count == 0)
        {
            return Task.FromResult(new CellOutput("text/html", "<em>Empty collection</em>"));
        }

        // Determine columns from first non-null element
        var firstNonNull = items.FirstOrDefault(i => i is not null);
        if (firstNonNull is null)
        {
            return Task.FromResult(new CellOutput("text/html", "<em>Empty collection</em>"));
        }

        var elementType = firstNonNull.GetType();
        var isPrimitiveLike = IsPrimitiveLike(elementType);

        PropertyInfo[] columns = isPrimitiveLike
            ? Array.Empty<PropertyInfo>()
            : elementType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var sb = new StringBuilder();
        sb.Append("<table><thead><tr>");

        if (isPrimitiveLike || columns.Length == 0)
        {
            sb.Append("<th>Value</th>");
        }
        else
        {
            foreach (var col in columns)
            {
                sb.Append("<th>").Append(WebUtility.HtmlEncode(col.Name)).Append("</th>");
            }
        }

        sb.Append("</tr></thead><tbody>");

        var rowCount = Math.Min(items.Count, MaxRows);
        for (int i = 0; i < rowCount; i++)
        {
            sb.Append("<tr>");
            var item = items[i];

            if (isPrimitiveLike || columns.Length == 0)
            {
                sb.Append("<td>").Append(WebUtility.HtmlEncode(item?.ToString() ?? "")).Append("</td>");
            }
            else
            {
                foreach (var col in columns)
                {
                    var val = item is not null ? col.GetValue(item) : null;
                    sb.Append("<td>").Append(WebUtility.HtmlEncode(val?.ToString() ?? "")).Append("</td>");
                }
            }

            sb.Append("</tr>");
        }

        sb.Append("</tbody>");

        if (items.Count > MaxRows)
        {
            sb.Append("<tfoot><tr><td colspan=\"")
              .Append(isPrimitiveLike || columns.Length == 0 ? 1 : columns.Length)
              .Append("\">... truncated at ")
              .Append(MaxRows)
              .Append(" rows</td></tr></tfoot>");
        }

        sb.Append("</table>");

        return Task.FromResult(new CellOutput("text/html", sb.ToString()));
    }

    private static bool IsPrimitiveLike(Type type)
    {
        return type.IsPrimitive || type.IsEnum || PrimitiveLikeTypes.Contains(type);
    }
}
