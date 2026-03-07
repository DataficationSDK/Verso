using System.Collections;
using System.Net;
using System.Reflection;
using System.Text;

namespace Verso.Extensions.Formatters;

/// <summary>
/// Shared rendering engine for expandable tree view output of objects and collections.
/// Used by <see cref="ObjectFormatter"/> and <see cref="CollectionFormatter"/>.
/// </summary>
internal static class ObjectTreeRenderer
{
    internal const int MaxDepth = 6;
    internal const int NestedCollectionLimit = 20;
    internal const int RootCollectionLimit = 100;
    internal const int MaxOutputSize = 512 * 1024; // 512 KB hard cap on rendered HTML

    private static readonly HashSet<Type> PrimitiveLikeTypes = new()
    {
        typeof(string), typeof(int), typeof(long), typeof(float), typeof(double),
        typeof(decimal), typeof(bool), typeof(DateTime), typeof(DateTimeOffset),
        typeof(char), typeof(Guid), typeof(byte), typeof(short), typeof(ushort),
        typeof(uint), typeof(ulong), typeof(sbyte)
    };

    // --- Public entry points ---

    /// <summary>
    /// Renders a single object as an expandable tree view. Called by <see cref="ObjectFormatter"/>.
    /// </summary>
    internal static string RenderObject(object value, Type type)
    {
        var sb = new StringBuilder();
        AppendStyles(sb);
        sb.Append("<div class=\"verso-obj-tree\">");

        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        RenderNestedObject(sb, value, type, 0, seen);

        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>
    /// Renders a collection as an expandable tree view. Called by <see cref="CollectionFormatter"/>.
    /// </summary>
    internal static string RenderCollection(IEnumerable enumerable)
    {
        var sb = new StringBuilder();
        AppendStyles(sb);
        sb.Append("<div class=\"verso-obj-tree\">");

        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        RenderNestedCollection(sb, enumerable, 0, seen);

        sb.Append("</div>");
        return sb.ToString();
    }

    // --- Recursive dispatch ---

    private static void RenderValue(StringBuilder sb, object? value, int depth, HashSet<object> seen)
    {
        if (value is null)
        {
            sb.Append("<span class=\"verso-obj-null\">null</span>");
            return;
        }

        if (depth > MaxDepth || sb.Length > MaxOutputSize)
        {
            sb.Append(WebUtility.HtmlEncode(value.ToString() ?? ""));
            return;
        }

        var type = value.GetType();

        if (IsPrimitiveLike(type))
        {
            sb.Append(WebUtility.HtmlEncode(value.ToString() ?? ""));
            return;
        }

        // Cycle detection — skip value types (can't form reference cycles)
        if (!type.IsValueType && !seen.Add(value))
        {
            sb.Append("<span class=\"verso-obj-cycle\">[circular reference]</span>");
            return;
        }

        if (value is IEnumerable enumerable and not string)
        {
            RenderNestedCollection(sb, enumerable, depth, seen);
            if (!type.IsValueType) seen.Remove(value);
            return;
        }

        var members = GetPublicMemberValues(type, value);
        if (members.Length > 0)
        {
            RenderNestedObject(sb, value, type, depth, seen);
            if (!type.IsValueType) seen.Remove(value);
            return;
        }

        // Fallback
        if (!type.IsValueType) seen.Remove(value);
        sb.Append(WebUtility.HtmlEncode(value.ToString() ?? ""));
    }

    // --- Object rendering ---

    private static void RenderNestedObject(StringBuilder sb, object value, Type type, int depth, HashSet<object> seen)
    {
        var members = GetPublicMemberValues(type, value);
        var openAttr = depth == 0 ? " open" : "";
        var typeName = WebUtility.HtmlEncode(GetFriendlyTypeName(type));

        sb.Append($"<details class=\"verso-obj-node\"{openAttr}>");
        sb.Append("<summary class=\"verso-obj-summary\">");
        sb.Append($"<span class=\"verso-obj-type\">{typeName}</span>");
        sb.Append($"<span class=\"verso-obj-count\"> ({members.Length} {(members.Length == 1 ? "member" : "members")})</span>");
        sb.Append("</summary>");
        sb.Append("<table class=\"verso-obj-table\"><tbody>");

        foreach (var (name, memberValue) in members)
        {
            sb.Append("<tr><td class=\"verso-obj-member\">");
            sb.Append(WebUtility.HtmlEncode(name));
            sb.Append("</td><td class=\"verso-obj-value\">");
            RenderValue(sb, memberValue, depth + 1, seen);
            sb.Append("</td></tr>");
        }

        sb.Append("</tbody></table></details>");
    }

    // --- Collection rendering ---

    private static void RenderNestedCollection(StringBuilder sb, IEnumerable enumerable, int depth, HashSet<object> seen)
    {
        int limit = depth == 0 ? RootCollectionLimit : NestedCollectionLimit;
        var items = Materialize(enumerable, limit + 1);

        if (items.Count == 0)
        {
            sb.Append("<span class=\"verso-obj-empty\">Empty collection</span>");
            return;
        }

        bool truncated = items.Count > limit;
        int displayCount = Math.Min(items.Count, limit);

        var firstNonNull = items.FirstOrDefault(i => i is not null);
        if (firstNonNull is null)
        {
            sb.Append("<span class=\"verso-obj-empty\">Empty collection</span>");
            return;
        }

        var elementType = firstNonNull.GetType();
        bool isPrimitive = IsPrimitiveLike(elementType);
        var typeName = WebUtility.HtmlEncode(GetFriendlyTypeName(enumerable.GetType()));
        var openAttr = depth == 0 ? " open" : "";

        sb.Append($"<details class=\"verso-obj-node\"{openAttr}>");
        sb.Append("<summary class=\"verso-obj-summary\">");
        sb.Append($"<span class=\"verso-obj-type\">{typeName}</span>");
        sb.Append($"<span class=\"verso-obj-count\"> ({displayCount}{(truncated ? "+" : "")} items)</span>");
        sb.Append("</summary>");
        sb.Append("<table class=\"verso-obj-table\">");

        if (isPrimitive)
        {
            sb.Append("<thead><tr><th class=\"verso-obj-th\">Value</th></tr></thead><tbody>");

            for (int i = 0; i < displayCount; i++)
            {
                sb.Append("<tr><td class=\"verso-obj-value\">");
                RenderValue(sb, items[i], depth + 1, seen);
                sb.Append("</td></tr>");
            }
        }
        else
        {
            var columns = GetPublicMembers(elementType);

            sb.Append("<thead><tr>");
            foreach (var col in columns)
                sb.Append("<th class=\"verso-obj-th\">").Append(WebUtility.HtmlEncode(col.Name)).Append("</th>");
            sb.Append("</tr></thead><tbody>");

            for (int i = 0; i < displayCount; i++)
            {
                sb.Append("<tr>");
                var item = items[i];

                if (isPrimitive || columns.Length == 0)
                {
                    sb.Append("<td class=\"verso-obj-value\">");
                    RenderValue(sb, item, depth + 1, seen);
                    sb.Append("</td>");
                }
                else
                {
                    foreach (var col in columns)
                    {
                        var val = item is not null ? GetMemberValue(col, item) : null;
                        sb.Append("<td class=\"verso-obj-value\">");
                        RenderValue(sb, val, depth + 1, seen);
                        sb.Append("</td>");
                    }
                }

                sb.Append("</tr>");
            }
        }

        sb.Append("</tbody></table>");

        if (truncated)
            sb.Append($"<div class=\"verso-obj-footer\">Showing {displayCount} of more items</div>");

        sb.Append("</details>");
    }

    // --- Type utilities ---

    internal static bool IsPrimitiveLike(Type type)
    {
        return type.IsPrimitive || type.IsEnum || PrimitiveLikeTypes.Contains(type);
    }

    internal static bool HasPublicMembers(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                   .Any(p => p.CanRead && p.GetIndexParameters().Length == 0)
            || type.GetFields(BindingFlags.Public | BindingFlags.Instance).Length > 0;
    }

    private static (string Name, object? Value)[] GetPublicMemberValues(Type type, object value)
    {
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Select(p =>
            {
                object? val;
                try { val = p.GetValue(value); }
                catch { val = null; }
                return (p.Name, Value: val);
            });

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Select(f =>
            {
                object? val;
                try { val = f.GetValue(value); }
                catch { val = null; }
                return (f.Name, Value: val);
            });

        return props.Concat(fields).ToArray();
    }

    private static MemberInfo[] GetPublicMembers(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Cast<MemberInfo>()
            .Concat(type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            .ToArray();
    }

    private static object? GetMemberValue(MemberInfo member, object target)
    {
        try
        {
            return member switch
            {
                PropertyInfo p => p.GetValue(target),
                FieldInfo f => f.GetValue(target),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static List<object?> Materialize(IEnumerable enumerable, int maxCount)
    {
        var items = new List<object?>();
        foreach (var item in enumerable)
        {
            items.Add(item);
            if (items.Count >= maxCount)
                break;
        }
        return items;
    }

    internal static string GetFriendlyTypeName(Type type)
    {
        if (type.IsArray)
            return GetFriendlyTypeName(type.GetElementType()!) + "[]";

        if (!type.IsGenericType)
            return type.Name;

        var name = type.Name;
        var backtick = name.IndexOf('`');
        if (backtick > 0)
            name = name[..backtick];

        var args = type.GetGenericArguments();
        var argNames = string.Join(", ", args.Select(GetFriendlyTypeName));
        return $"{name}<{argNames}>";
    }

    // --- CSS ---

    private static void AppendStyles(StringBuilder sb)
    {
        sb.Append("<style>");

        // Root container: CSS custom properties with fallback chains
        sb.Append(".verso-obj-tree{");
        sb.Append("--obj-bg:var(--vscode-editor-background,var(--verso-cell-output-background,#fff));");
        sb.Append("--obj-fg:var(--vscode-editor-foreground,var(--verso-cell-output-foreground,#1e1e1e));");
        sb.Append("--obj-border:var(--vscode-editorWidget-border,var(--verso-border-default,#e0e0e0));");
        sb.Append("--obj-header-bg:var(--vscode-editorWidget-background,var(--verso-cell-background,#f5f5f5));");
        sb.Append("--obj-hover:var(--vscode-list-hoverBackground,var(--verso-cell-hover-background,#f0f0f0));");
        sb.Append("--obj-muted:var(--vscode-descriptionForeground,var(--verso-editor-line-number,#858585));");
        sb.Append("--obj-accent:var(--vscode-textLink-foreground,var(--verso-accent-primary,#0078d4));");
        sb.Append("--obj-summary-bg:var(--vscode-sideBar-background,var(--verso-cell-background,#f5f5f5));");
        sb.Append("font-family:var(--verso-code-output-font-family,monospace);font-size:13px;");
        sb.Append("color:var(--obj-fg);}");

        // details/summary tree nodes
        sb.Append(".verso-obj-tree details.verso-obj-node{");
        sb.Append("border:1px solid var(--obj-border);border-radius:3px;margin:2px 0;}");

        sb.Append(".verso-obj-tree details.verso-obj-node>summary.verso-obj-summary{");
        sb.Append("padding:3px 8px;cursor:pointer;list-style:none;");
        sb.Append("background:var(--obj-summary-bg);");
        sb.Append("display:flex;align-items:center;gap:4px;");
        sb.Append("user-select:none;}");

        // Hide default WebKit marker
        sb.Append(".verso-obj-tree details.verso-obj-node>summary.verso-obj-summary::-webkit-details-marker{display:none;}");

        // Custom disclosure triangle
        sb.Append(".verso-obj-tree details.verso-obj-node>summary.verso-obj-summary::before{");
        sb.Append("content:'\\25B6';font-size:9px;color:var(--obj-muted);");
        sb.Append("transition:transform 0.1s;}");

        sb.Append(".verso-obj-tree details.verso-obj-node[open]>summary.verso-obj-summary::before{");
        sb.Append("transform:rotate(90deg);}");

        sb.Append(".verso-obj-tree summary.verso-obj-summary:hover{background:var(--obj-hover);}");

        // Type name and count
        sb.Append(".verso-obj-tree .verso-obj-type{font-weight:600;color:var(--obj-accent);}");
        sb.Append(".verso-obj-tree .verso-obj-count{color:var(--obj-muted);font-size:11px;}");

        // Table
        sb.Append(".verso-obj-tree table.verso-obj-table{");
        sb.Append("border-collapse:collapse;width:100%;background:var(--obj-bg);color:var(--obj-fg);}");

        sb.Append(".verso-obj-tree table.verso-obj-table th.verso-obj-th{");
        sb.Append("text-align:left;padding:4px 10px;");
        sb.Append("border-bottom:2px solid var(--obj-border);");
        sb.Append("background:var(--obj-header-bg);font-weight:600;white-space:nowrap;}");

        sb.Append(".verso-obj-tree table.verso-obj-table td.verso-obj-member{");
        sb.Append("padding:3px 10px;border-bottom:1px solid var(--obj-border);");
        sb.Append("color:var(--obj-muted);white-space:nowrap;vertical-align:top;font-size:12px;}");

        sb.Append(".verso-obj-tree table.verso-obj-table td.verso-obj-value{");
        sb.Append("padding:3px 10px;border-bottom:1px solid var(--obj-border);vertical-align:top;}");

        sb.Append(".verso-obj-tree table.verso-obj-table tbody tr:hover{background:var(--obj-hover);}");

        // Sentinels
        sb.Append(".verso-obj-tree .verso-obj-null{color:var(--obj-muted);font-style:italic;}");
        sb.Append(".verso-obj-tree .verso-obj-cycle{color:var(--obj-muted);font-style:italic;}");
        sb.Append(".verso-obj-tree .verso-obj-empty{color:var(--obj-muted);font-style:italic;}");

        // Truncation footer
        sb.Append(".verso-obj-tree .verso-obj-footer{");
        sb.Append("padding:4px 10px;color:var(--obj-muted);font-size:11px;");
        sb.Append("border-top:1px solid var(--obj-border);}");

        sb.Append("</style>");
    }
}
