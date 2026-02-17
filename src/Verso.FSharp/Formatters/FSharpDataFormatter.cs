using System.Collections;
using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.FSharp.Core;
using Microsoft.FSharp.Reflection;
using Verso.Abstractions;

namespace Verso.FSharp.Formatters;

/// <summary>
/// Formats F# types (records, discriminated unions, options, results, collections, tuples)
/// as rich HTML output with themed CSS.
/// </summary>
[VersoExtension]
public sealed class FSharpDataFormatter : IDataFormatter
{
    private const int MaxCollectionItems = 100;
    private const int MaxDepth = 5;

    // --- IExtension ---

    public string ExtensionId => "verso.fsharp.formatter";
    public string Name => "F# Data Formatter";
    public string Version => "0.1.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Formats F# types as rich HTML tables and styled output.";

    // --- IDataFormatter ---

    public IReadOnlyList<Type> SupportedTypes { get; } = new[] { typeof(object) };
    public int Priority => 20;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public bool CanFormat(object value, IFormatterContext context)
    {
        if (value is null) return false;
        var type = value.GetType();
        return IsFSharpType(type);
    }

    public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
    {
        var sb = new StringBuilder();
        AppendStyles(sb);
        sb.Append("<div class=\"verso-fsharp-output\">");
        RenderValue(sb, value, 0);
        sb.Append("</div>");
        return Task.FromResult(new CellOutput("text/html", sb.ToString()));
    }

    // --- Type detection ---

    internal static bool IsFSharpType(Type type)
    {
        if (type.Namespace?.StartsWith("Microsoft.FSharp") == true)
            return true;

        try { if (FSharpType.IsRecord(type, null)) return true; } catch { /* not a record */ }
        try { if (FSharpType.IsUnion(type, null)) return true; } catch { /* not a union */ }
        try { if (FSharpType.IsTuple(type)) return true; } catch { /* not a tuple */ }

        return false;
    }

    // --- Value rendering ---

    private void RenderValue(StringBuilder sb, object? value, int depth)
    {
        if (value is null)
        {
            sb.Append("<span class=\"verso-fsharp-none\">null</span>");
            return;
        }

        if (depth > MaxDepth)
        {
            sb.Append(WebUtility.HtmlEncode(value.ToString() ?? ""));
            return;
        }

        var type = value.GetType();

        // Option<T>
        if (IsOptionType(type))
        {
            RenderOption(sb, value, type, depth);
            return;
        }

        // Result<T,E>
        if (IsResultType(type))
        {
            RenderResult(sb, value, type, depth);
            return;
        }

        // Tuple
        try
        {
            if (FSharpType.IsTuple(type))
            {
                RenderTuple(sb, value, type, depth);
                return;
            }
        }
        catch { /* not a tuple */ }

        // Record
        try
        {
            if (FSharpType.IsRecord(type, null))
            {
                RenderRecord(sb, value, type, depth);
                return;
            }
        }
        catch { /* not a record */ }

        // Map<K,V> — before Union since Map is internally a DU
        if (IsMapType(type))
        {
            RenderMap(sb, value, depth);
            return;
        }

        // Set<T> — before Union since Set is internally a DU
        if (IsSetType(type))
        {
            RenderSet(sb, value, depth);
            return;
        }

        // IEnumerable (FSharpList, arrays, seqs) — before Union since FSharpList is a DU
        if (value is IEnumerable enumerable && value is not string)
        {
            RenderCollection(sb, enumerable, depth);
            return;
        }

        // Union (non-Option, non-Result, non-collection)
        try
        {
            if (FSharpType.IsUnion(type, null))
            {
                RenderUnion(sb, value, type, depth);
                return;
            }
        }
        catch { /* not a union */ }

        // Fallback
        sb.Append(WebUtility.HtmlEncode(value.ToString() ?? ""));
    }

    // --- Option ---

    private static bool IsOptionType(Type type)
    {
        if (!type.IsGenericType) return false;
        var def = type.GetGenericTypeDefinition();
        return def.FullName == "Microsoft.FSharp.Core.FSharpOption`1";
    }

    private void RenderOption(StringBuilder sb, object value, Type type, int depth)
    {
        // FSharpOption<T> has a static get_Value and a tag
        var tagProp = type.GetProperty("Tag");
        var tag = tagProp?.GetValue(value) is int t ? t : -1;

        if (tag == 0) // None
        {
            sb.Append("<span class=\"verso-fsharp-none\">None</span>");
        }
        else // Some
        {
            var valueProp = type.GetProperty("Value");
            var inner = valueProp?.GetValue(value);
            RenderValue(sb, inner, depth + 1);
        }
    }

    // --- Result ---

    private static bool IsResultType(Type type)
    {
        if (!type.IsGenericType) return false;
        var def = type.GetGenericTypeDefinition();
        return def.FullName == "Microsoft.FSharp.Core.FSharpResult`2";
    }

    private void RenderResult(StringBuilder sb, object value, Type type, int depth)
    {
        var tagProp = type.GetProperty("Tag");
        var tag = tagProp?.GetValue(value) is int t ? t : -1;

        if (tag == 0) // Ok
        {
            var resultValueProp = type.GetProperty("ResultValue");
            var inner = resultValueProp?.GetValue(value);
            sb.Append("<span class=\"verso-fsharp-ok\">");
            sb.Append("<strong>Ok</strong> ");
            RenderValue(sb, inner, depth + 1);
            sb.Append("</span>");
        }
        else // Error
        {
            var errorValueProp = type.GetProperty("ErrorValue");
            var inner = errorValueProp?.GetValue(value);
            sb.Append("<span class=\"verso-fsharp-error\">");
            sb.Append("<strong>Error</strong> ");
            RenderValue(sb, inner, depth + 1);
            sb.Append("</span>");
        }
    }

    // --- Map ---

    private static bool IsMapType(Type type)
    {
        if (!type.IsGenericType) return false;
        var def = type.GetGenericTypeDefinition();
        return def.FullName == "Microsoft.FSharp.Collections.FSharpMap`2";
    }

    private void RenderMap(StringBuilder sb, object value, int depth)
    {
        var enumerable = (IEnumerable)value;
        var items = new List<(object? key, object? val)>();

        foreach (var kvp in enumerable)
        {
            var kvpType = kvp.GetType();
            var keyProp = kvpType.GetProperty("Key");
            var valProp = kvpType.GetProperty("Value");
            items.Add((keyProp?.GetValue(kvp), valProp?.GetValue(kvp)));
            if (items.Count > MaxCollectionItems) break;
        }

        if (items.Count == 0)
        {
            sb.Append("<em>Empty map</em>");
            return;
        }

        sb.Append("<table><thead><tr><th>Key</th><th>Value</th></tr></thead><tbody>");
        var count = Math.Min(items.Count, MaxCollectionItems);
        for (int i = 0; i < count; i++)
        {
            sb.Append("<tr><td>");
            RenderValue(sb, items[i].key, depth + 1);
            sb.Append("</td><td>");
            RenderValue(sb, items[i].val, depth + 1);
            sb.Append("</td></tr>");
        }
        sb.Append("</tbody></table>");

        if (items.Count > MaxCollectionItems)
        {
            sb.Append("<div class=\"verso-fsharp-footer\">Showing ")
              .Append(MaxCollectionItems)
              .Append(" of more items</div>");
        }
    }

    // --- Set ---

    private static bool IsSetType(Type type)
    {
        if (!type.IsGenericType) return false;
        var def = type.GetGenericTypeDefinition();
        return def.FullName == "Microsoft.FSharp.Collections.FSharpSet`1";
    }

    private void RenderSet(StringBuilder sb, object value, int depth)
    {
        var enumerable = (IEnumerable)value;
        var items = new List<object?>();
        foreach (var item in enumerable)
        {
            items.Add(item);
            if (items.Count > MaxCollectionItems) break;
        }

        if (items.Count == 0)
        {
            sb.Append("<em>Empty set</em>");
            return;
        }

        var count = Math.Min(items.Count, MaxCollectionItems);

        if (count <= 10)
        {
            // Inline comma-separated
            sb.Append("set [");
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append("; ");
                sb.Append(WebUtility.HtmlEncode(items[i]?.ToString() ?? ""));
            }
            sb.Append(']');
        }
        else
        {
            // Table for larger sets
            sb.Append("<table><thead><tr><th>Value</th></tr></thead><tbody>");
            for (int i = 0; i < count; i++)
            {
                sb.Append("<tr><td>");
                RenderValue(sb, items[i], depth + 1);
                sb.Append("</td></tr>");
            }
            sb.Append("</tbody></table>");
        }

        if (items.Count > MaxCollectionItems)
        {
            sb.Append("<div class=\"verso-fsharp-footer\">Showing ")
              .Append(MaxCollectionItems)
              .Append(" of more items</div>");
        }
    }

    // --- Collections (FSharpList, arrays, seqs) ---

    private void RenderCollection(StringBuilder sb, IEnumerable enumerable, int depth)
    {
        var items = new List<object?>();
        foreach (var item in enumerable)
        {
            items.Add(item);
            if (items.Count > MaxCollectionItems) break;
        }

        if (items.Count == 0)
        {
            sb.Append("<em>Empty collection</em>");
            return;
        }

        var firstNonNull = items.FirstOrDefault(i => i is not null);
        if (firstNonNull is null)
        {
            sb.Append("<em>Empty collection</em>");
            return;
        }

        var elementType = firstNonNull.GetType();
        var isPrimitiveLike = IsPrimitiveLike(elementType);
        PropertyInfo[] columns = isPrimitiveLike
            ? Array.Empty<PropertyInfo>()
            : elementType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var count = Math.Min(items.Count, MaxCollectionItems);

        if (isPrimitiveLike || columns.Length == 0)
        {
            // Vertical list for primitives
            sb.Append("<ul>");
            for (int i = 0; i < count; i++)
            {
                sb.Append("<li>").Append(WebUtility.HtmlEncode(items[i]?.ToString() ?? "")).Append("</li>");
            }
            sb.Append("</ul>");
        }
        else
        {
            // Table with property columns for complex elements
            sb.Append("<table><thead><tr>");
            foreach (var col in columns)
            {
                sb.Append("<th>").Append(WebUtility.HtmlEncode(col.Name)).Append("</th>");
            }
            sb.Append("</tr></thead><tbody>");

            for (int i = 0; i < count; i++)
            {
                sb.Append("<tr>");
                var item = items[i];
                foreach (var col in columns)
                {
                    sb.Append("<td>");
                    var val = item is not null ? col.GetValue(item) : null;
                    sb.Append(WebUtility.HtmlEncode(val?.ToString() ?? ""));
                    sb.Append("</td>");
                }
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table>");
        }

        if (items.Count > MaxCollectionItems)
        {
            sb.Append("<div class=\"verso-fsharp-footer\">Showing ")
              .Append(MaxCollectionItems)
              .Append(" of ")
              .Append(items.Count > MaxCollectionItems ? "more" : items.Count.ToString())
              .Append(" items</div>");
        }
    }

    // --- Unions ---

    private void RenderUnion(StringBuilder sb, object value, Type type, int depth)
    {
        var (caseInfo, fields) = FSharpValue.GetUnionFields(value, type, null);

        if (fields.Length == 0)
        {
            // No-field case: just the case name
            sb.Append(WebUtility.HtmlEncode(caseInfo.Name));
        }
        else if (fields.Length == 1 && FSharpType.GetUnionCases(type, null).Length == 1)
        {
            // Single-case, single-field: show value directly
            RenderValue(sb, fields[0], depth + 1);
        }
        else
        {
            // Case name + fields
            sb.Append(WebUtility.HtmlEncode(caseInfo.Name));
            sb.Append(" (");
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                RenderValue(sb, fields[i], depth + 1);
            }
            sb.Append(')');
        }
    }

    // --- Records ---

    private void RenderRecord(StringBuilder sb, object value, Type type, int depth)
    {
        var fields = FSharpType.GetRecordFields(type, null);
        var values = FSharpValue.GetRecordFields(value, null);

        sb.Append("<table><thead><tr><th>Field</th><th>Value</th></tr></thead><tbody>");
        for (int i = 0; i < fields.Length; i++)
        {
            sb.Append("<tr><td>").Append(WebUtility.HtmlEncode(fields[i].Name)).Append("</td><td>");
            RenderValue(sb, values[i], depth + 1);
            sb.Append("</td></tr>");
        }
        sb.Append("</tbody></table>");
    }

    // --- Tuples ---

    private void RenderTuple(StringBuilder sb, object value, Type type, int depth)
    {
        var fields = FSharpValue.GetTupleFields(value);
        var types = FSharpType.GetTupleElements(type);

        sb.Append('(');
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append("<span title=\"").Append(WebUtility.HtmlEncode(types[i].Name)).Append("\">");
            RenderValue(sb, fields[i], depth + 1);
            sb.Append("</span>");
        }
        sb.Append(')');
    }

    // --- CSS ---

    internal static void AppendStyles(StringBuilder sb)
    {
        sb.Append("<style>");

        sb.Append(".verso-fsharp-output{");
        sb.Append("--fsharp-bg:var(--vscode-editor-background,var(--verso-cell-output-background,#fff));");
        sb.Append("--fsharp-fg:var(--vscode-editor-foreground,var(--verso-cell-output-foreground,#1e1e1e));");
        sb.Append("--fsharp-border:var(--vscode-editorWidget-border,var(--verso-border-default,#e0e0e0));");
        sb.Append("--fsharp-header-bg:var(--vscode-editorWidget-background,var(--verso-cell-background,#f5f5f5));");
        sb.Append("--fsharp-hover:var(--vscode-list-hoverBackground,var(--verso-cell-hover-background,#f0f0f0));");
        sb.Append("--fsharp-muted:var(--vscode-descriptionForeground,var(--verso-editor-line-number,#858585));");
        sb.Append("--fsharp-error-bg:var(--vscode-inputValidation-errorBackground,var(--verso-status-error,#fde7e9));");
        sb.Append("--fsharp-ok-bg:var(--vscode-inputValidation-infoBackground,var(--verso-status-success,#e6f4ea));");
        sb.Append("font-family:var(--verso-code-output-font-family,monospace);font-size:13px;color:var(--fsharp-fg);}");

        // Table
        sb.Append(".verso-fsharp-output table{border-collapse:collapse;width:auto;background:var(--fsharp-bg);color:var(--fsharp-fg);}");
        sb.Append(".verso-fsharp-output th{text-align:left;padding:6px 12px;border-bottom:2px solid var(--fsharp-border);background:var(--fsharp-header-bg);font-weight:600;}");
        sb.Append(".verso-fsharp-output td{padding:5px 12px;border-bottom:1px solid var(--fsharp-border);}");
        sb.Append(".verso-fsharp-output tbody tr:hover{background:var(--fsharp-hover);}");

        // None / null
        sb.Append(".verso-fsharp-output .verso-fsharp-none{color:var(--fsharp-muted);font-style:italic;}");

        // Ok / Error
        sb.Append(".verso-fsharp-output .verso-fsharp-ok{padding:2px 6px;background:var(--fsharp-ok-bg);border-radius:3px;}");
        sb.Append(".verso-fsharp-output .verso-fsharp-error{padding:2px 6px;background:var(--fsharp-error-bg);border-radius:3px;}");

        // Footer
        sb.Append(".verso-fsharp-output .verso-fsharp-footer{padding:6px 0;color:var(--fsharp-muted);font-size:12px;}");

        sb.Append("</style>");
    }

    // --- Helpers ---

    private static readonly HashSet<Type> PrimitiveLikeTypes = new()
    {
        typeof(string), typeof(int), typeof(long), typeof(float), typeof(double),
        typeof(decimal), typeof(bool), typeof(DateTime), typeof(DateTimeOffset),
        typeof(char), typeof(Guid), typeof(byte), typeof(short), typeof(ushort),
        typeof(uint), typeof(ulong), typeof(sbyte)
    };

    private static bool IsPrimitiveLike(Type type)
    {
        return type.IsPrimitive || type.IsEnum || PrimitiveLikeTypes.Contains(type);
    }
}
