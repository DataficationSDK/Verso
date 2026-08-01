using System.Collections;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;

namespace Verso.DataFrame.Formatters;

internal static class DataFrameHtmlRenderer
{
    internal const int DefaultMaxRows = 100;

    public static string Render(
        object dataFrame,
        CancellationToken cancellationToken,
        int maxRows = DefaultMaxRows,
        double maxHeight = 600)
    {
        ArgumentNullException.ThrowIfNull(dataFrame);
        if (maxRows <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRows));

        var boundedMaxHeight = double.IsFinite(maxHeight)
            ? Math.Clamp(maxHeight, 160, 2000)
            : 600;

        var columnsValue = GetRequiredProperty(dataFrame, "Columns");
        var rowsValue = GetRequiredProperty(dataFrame, "Rows");
        var columns = ReadColumns(columnsValue, cancellationToken);
        var totalRows = TryGetCount(rowsValue);

        var sb = new StringBuilder();
        AppendStyles(sb);
        sb.Append("<div class=\"verso-dataframe\" style=\"--df-max-height:")
          .Append(boundedMaxHeight.ToString("0.##", CultureInfo.InvariantCulture))
          .Append("px\">");

        if (columns.Count == 0)
        {
            sb.Append("<div class=\"verso-dataframe-empty\"><em>DataFrame has no columns.</em></div></div>");
            return sb.ToString();
        }

        sb.Append("<div class=\"verso-dataframe-scroll\"><table><thead><tr>");
        foreach (var column in columns)
        {
            sb.Append("<th title=\"")
              .Append(Encode(column.FullTypeName))
              .Append("\"><span class=\"verso-dataframe-column-name\">")
              .Append(Encode(column.Name))
              .Append("</span><span class=\"verso-dataframe-column-type\">")
              .Append(Encode(column.DisplayTypeName))
              .Append("</span></th>");
        }
        sb.Append("</tr></thead><tbody>");

        var displayedRows = 0;
        var hasMoreRows = false;
        foreach (var row in Enumerate(rowsValue, "Rows"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (row is null)
                throw new InvalidOperationException("Rows contains a null DataFrame row.");

            if (displayedRows >= maxRows)
            {
                hasMoreRows = true;
                break;
            }

            AppendRow(sb, row, columns.Count);
            displayedRows++;
        }

        sb.Append("</tbody></table></div>");

        if (displayedRows == 0)
        {
            sb.Append("<div class=\"verso-dataframe-empty\"><em>DataFrame has no rows.</em></div>");
        }

        AppendFooter(sb, displayedRows, totalRows, hasMoreRows);
        sb.Append("</div>");
        return sb.ToString();
    }

    public static string RenderError(string message)
    {
        var sb = new StringBuilder();
        AppendStyles(sb);
        sb.Append("<div class=\"verso-dataframe verso-dataframe-error\"><strong>Unable to render DataFrame.</strong> ")
          .Append(Encode(message))
          .Append("</div>");
        return sb.ToString();
    }

    private static List<ColumnInfo> ReadColumns(object columnsValue, CancellationToken cancellationToken)
    {
        var columns = new List<ColumnInfo>();
        foreach (var column in Enumerate(columnsValue, "Columns"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (column is null)
                throw new InvalidOperationException("Columns contains a null DataFrame column.");

            var name = GetRequiredProperty(column, "Name").ToString() ?? string.Empty;
            var dataType = GetRequiredProperty(column, "DataType");
            var fullTypeName = dataType is Type type
                ? type.FullName ?? type.Name
                : dataType.ToString() ?? string.Empty;
            var displayTypeName = dataType is Type displayType
                ? displayType.Name
                : fullTypeName.Split('.').LastOrDefault() ?? fullTypeName;

            columns.Add(new ColumnInfo(name, displayTypeName, fullTypeName));
        }

        return columns;
    }

    private static void AppendRow(StringBuilder sb, object row, int columnCount)
    {
        var values = Enumerate(row, "DataFrame row").GetEnumerator();
        try
        {
            sb.Append("<tr>");
            for (var index = 0; index < columnCount; index++)
            {
                var hasValue = values.MoveNext();
                AppendCell(sb, hasValue ? values.Current : null);
            }
            sb.Append("</tr>");
        }
        finally
        {
            (values as IDisposable)?.Dispose();
        }
    }

    private static void AppendCell(StringBuilder sb, object? value)
    {
        sb.Append("<td>");
        if (value is null || value is DBNull)
        {
            sb.Append("<span class=\"verso-dataframe-null\">null</span>");
        }
        else
        {
            var text = value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value.ToString();
            sb.Append(Encode(text ?? string.Empty));
        }
        sb.Append("</td>");
    }

    private static void AppendFooter(
        StringBuilder sb,
        int displayedRows,
        long? totalRows,
        bool hasMoreRows)
    {
        sb.Append("<div class=\"verso-dataframe-footer\">");

        if (totalRows is not null)
        {
            sb.Append("Showing ")
              .Append(displayedRows.ToString("N0", CultureInfo.InvariantCulture))
              .Append(" of ")
              .Append(totalRows.Value.ToString("N0", CultureInfo.InvariantCulture))
              .Append(" rows");
        }
        else if (hasMoreRows)
        {
            sb.Append("Showing first ")
              .Append(displayedRows.ToString("N0", CultureInfo.InvariantCulture))
              .Append(" rows");
        }
        else
        {
            sb.Append(displayedRows.ToString("N0", CultureInfo.InvariantCulture))
              .Append(" rows");
        }

        sb.Append("</div>");
    }

    private static object GetRequiredProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);
        if (property is null)
            throw new InvalidOperationException($"{instance.GetType().FullName} has no public {propertyName} property.");

        return property.GetValue(instance)
            ?? throw new InvalidOperationException($"{propertyName} returned null.");
    }

    private static IEnumerable<object?> Enumerate(object value, string memberName)
    {
        if (value is not IEnumerable enumerable)
            throw new InvalidOperationException($"{memberName} does not implement IEnumerable.");

        foreach (var item in enumerable)
            yield return item;
    }

    private static long? TryGetCount(object rows)
    {
        try
        {
            var count = rows.GetType().GetProperty(
                "Count",
                BindingFlags.Instance | BindingFlags.Public)?.GetValue(rows);
            return count is null
                ? null
                : Convert.ToInt64(count, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static void AppendStyles(StringBuilder sb)
    {
        sb.Append("<style>");
        sb.Append(".verso-dataframe{");
        sb.Append("--df-bg:var(--vscode-editor-background,var(--verso-bg-default,#fff));");
        sb.Append("--df-fg:var(--vscode-editor-foreground,var(--verso-fg-default,#1e1e1e));");
        sb.Append("--df-muted:var(--vscode-descriptionForeground,var(--verso-fg-muted,#6b7280));");
        sb.Append("--df-border:var(--vscode-editorWidget-border,var(--verso-border-default,#d1d5db));");
        sb.Append("--df-header:var(--vscode-editorWidget-background,var(--verso-bg-elevated,#f3f4f6));");
        sb.Append("--df-hover:var(--vscode-list-hoverBackground,rgba(127,127,127,.12));");
        sb.Append("font-family:var(--verso-font-family-mono,monospace);font-size:13px;color:var(--df-fg);}");
        sb.Append(".verso-dataframe-scroll{max-width:100%;max-height:var(--df-max-height,600px);overflow:auto;border:1px solid var(--df-border);}");
        sb.Append(".verso-dataframe table{border-collapse:separate;border-spacing:0;width:100%;background:var(--df-bg);color:var(--df-fg);}");
        sb.Append(".verso-dataframe th{position:sticky;top:0;z-index:1;text-align:left;white-space:nowrap;padding:7px 12px;border-bottom:2px solid var(--df-border);background:var(--df-header);font-weight:600;}");
        sb.Append(".verso-dataframe td{max-width:360px;padding:6px 12px;border-bottom:1px solid var(--df-border);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}");
        sb.Append(".verso-dataframe tbody tr:hover{background:var(--df-hover);}");
        sb.Append(".verso-dataframe-column-type{display:block;margin-top:2px;color:var(--df-muted);font-size:11px;font-weight:400;}");
        sb.Append(".verso-dataframe-null{color:var(--df-muted);font-style:italic;}");
        sb.Append(".verso-dataframe-footer,.verso-dataframe-empty{padding:7px 2px;color:var(--df-muted);font-size:12px;}");
        sb.Append(".verso-dataframe-error{padding:8px;border:1px solid var(--df-border);}");
        sb.Append("</style>");
    }

    private sealed record ColumnInfo(string Name, string DisplayTypeName, string FullTypeName);
}
