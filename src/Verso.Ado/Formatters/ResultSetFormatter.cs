using System.Data;
using System.Net;
using System.Text;
using System.Text.Json;
using Verso.Abstractions;
using Verso.Ado.Models;

namespace Verso.Ado.Formatters;

/// <summary>
/// Formats <see cref="SqlResultSet"/> and <see cref="DataTable"/> objects as paginated HTML tables.
/// </summary>
[VersoExtension]
public sealed class ResultSetFormatter : IDataFormatter
{
    private const int DefaultPageSize = 50;

    // --- IExtension ---

    public string ExtensionId => "verso.ado.formatter.resultset";
    public string Name => "Result Set Formatter";
    public string Version => "0.1.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Formats SQL result sets and DataTables as paginated HTML tables.";

    // --- IDataFormatter ---

    public IReadOnlyList<Type> SupportedTypes { get; } = new[] { typeof(DataTable), typeof(SqlResultSet) };
    public int Priority => 30;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public bool CanFormat(object value, IFormatterContext context)
    {
        return value is DataTable or SqlResultSet;
    }

    public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
    {
        var html = value switch
        {
            SqlResultSet rs => FormatResultSetHtml(rs, context.Theme),
            DataTable dt => FormatDataTableHtml(dt, context.Theme),
            _ => "<em>Unsupported type</em>"
        };

        return Task.FromResult(new CellOutput("text/html", html));
    }

    // --- Internal static helpers (called directly by SqlKernel) ---

    internal static string FormatResultSetHtml(SqlResultSet resultSet, IThemeContext? theme, int pageSize = DefaultPageSize)
    {
        if (resultSet.Columns.Count == 0)
            return "<div class=\"verso-sql-result\"><em>No columns returned.</em></div>";

        if (resultSet.Rows.Count == 0)
            return "<div class=\"verso-sql-result\"><em>Query returned no rows.</em></div>";

        var sb = new StringBuilder();

        AppendStyles(sb, theme);

        sb.Append("<div class=\"verso-sql-result\">");

        // Build table header
        sb.Append("<table><thead><tr>");
        foreach (var col in resultSet.Columns)
        {
            sb.Append("<th title=\"").Append(WebUtility.HtmlEncode(col.DataTypeName)).Append("\">");
            sb.Append(WebUtility.HtmlEncode(col.Name));
            sb.Append("</th>");
        }
        sb.Append("</tr></thead>");

        // Build table body - render all rows into tbody with data-row-index for paging
        sb.Append("<tbody id=\"verso-sql-tbody\">");
        for (int r = 0; r < resultSet.Rows.Count; r++)
        {
            sb.Append("<tr data-row-index=\"").Append(r).Append("\">");
            var row = resultSet.Rows[r];
            for (int c = 0; c < row.Length; c++)
            {
                sb.Append("<td>");
                if (row[c] is null || row[c] is DBNull)
                {
                    sb.Append("<span class=\"verso-sql-null\">NULL</span>");
                }
                else
                {
                    sb.Append(WebUtility.HtmlEncode(row[c]!.ToString() ?? ""));
                }
                sb.Append("</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");

        // Paging controls and footer
        int totalRows = resultSet.Rows.Count;
        if (totalRows > pageSize)
        {
            AppendPagingScript(sb, totalRows, pageSize);
        }
        else
        {
            sb.Append("<div class=\"verso-sql-footer\">Showing 1-")
              .Append(totalRows.ToString("N0"))
              .Append(" of ")
              .Append(totalRows.ToString("N0"))
              .Append(" rows</div>");
        }

        // Truncation warning
        if (resultSet.WasTruncated)
        {
            sb.Append("<div class=\"verso-sql-truncation\">Results truncated at ")
              .Append(resultSet.Rows.Count.ToString("N0"))
              .Append(" of ")
              .Append(resultSet.TotalRowCount.ToString("N0"))
              .Append(" total rows. Use WHERE or LIMIT to narrow your query.</div>");
        }

        sb.Append("</div>");
        return sb.ToString();
    }

    internal static string FormatDataTableHtml(DataTable dt, IThemeContext? theme, int pageSize = DefaultPageSize)
    {
        if (dt.Columns.Count == 0)
            return "<div class=\"verso-sql-result\"><em>No columns returned.</em></div>";

        if (dt.Rows.Count == 0)
            return "<div class=\"verso-sql-result\"><em>Query returned no rows.</em></div>";

        // Convert DataTable to column/row data and reuse the same rendering
        var columns = new List<SqlColumnMetadata>();
        for (int i = 0; i < dt.Columns.Count; i++)
        {
            var col = dt.Columns[i];
            columns.Add(new SqlColumnMetadata(
                col.ColumnName,
                col.DataType.Name,
                col.DataType,
                col.AllowDBNull));
        }

        var rows = new List<object?[]>();
        foreach (DataRow dr in dt.Rows)
        {
            var row = new object?[dt.Columns.Count];
            for (int i = 0; i < dt.Columns.Count; i++)
            {
                row[i] = dr[i] is DBNull ? null : dr[i];
            }
            rows.Add(row);
        }

        var resultSet = new SqlResultSet(columns, rows, dt.Rows.Count, false);
        return FormatResultSetHtml(resultSet, theme, pageSize);
    }

    internal static string FormatNonQueryHtml(int rowsAffected, long elapsedMs, IThemeContext? theme)
    {
        var sb = new StringBuilder();
        AppendStyles(sb, theme);
        sb.Append("<div class=\"verso-sql-result\">");
        sb.Append("<div class=\"verso-sql-footer\">");
        sb.Append(rowsAffected.ToString("N0")).Append(" row(s) affected");
        sb.Append(" <span style=\"opacity:0.7;\">(").Append(elapsedMs).Append(" ms)</span>");
        sb.Append("</div></div>");
        return sb.ToString();
    }

    // --- Private rendering helpers ---

    private static void AppendStyles(StringBuilder sb, IThemeContext? theme)
    {
        var bg = theme?.GetColor("CellOutputBackground") ?? "#ffffff";
        var fg = theme?.GetColor("CellOutputForeground") ?? "#1e1e1e";
        var border = theme?.GetColor("BorderColor") ?? "#e0e0e0";
        var headerBg = theme?.GetColor("CellBackground") ?? "#f5f5f5";

        sb.Append("<style>");
        sb.Append(".verso-sql-result { font-family: monospace; font-size: 13px; }");
        sb.Append(".verso-sql-result table { border-collapse: collapse; width: 100%; background: ").Append(bg).Append("; color: ").Append(fg).Append("; }");
        sb.Append(".verso-sql-result th { background: ").Append(headerBg).Append("; text-align: left; padding: 4px 8px; border: 1px solid ").Append(border).Append("; }");
        sb.Append(".verso-sql-result td { padding: 4px 8px; border: 1px solid ").Append(border).Append("; }");
        sb.Append(".verso-sql-null { color: #999; font-style: italic; }");
        sb.Append(".verso-sql-pager { padding: 6px 0; }");
        sb.Append(".verso-sql-pager button { margin-right: 4px; }");
        sb.Append(".verso-sql-footer { padding: 6px 0; opacity: 0.8; font-size: 12px; }");
        sb.Append(".verso-sql-truncation { padding: 6px 8px; margin-top: 4px; background: #fff3cd; color: #856404; border: 1px solid #ffc107; border-radius: 4px; font-size: 12px; }");
        sb.Append("</style>");
    }

    private static void AppendPagingScript(StringBuilder sb, int totalRows, int pageSize)
    {
        sb.Append("<div class=\"verso-sql-pager\">");
        sb.Append("<button id=\"verso-sql-prev\">Previous</button>");
        sb.Append("<button id=\"verso-sql-next\">Next</button>");
        sb.Append("<span id=\"verso-sql-page-info\" class=\"verso-sql-footer\"></span>");
        sb.Append("</div>");

        sb.Append("<script>(function(){");
        sb.Append("var pageSize=").Append(pageSize).Append(",totalRows=").Append(totalRows).Append(",page=0;");
        sb.Append("var maxPage=Math.ceil(totalRows/pageSize)-1;");
        sb.Append("var tbody=document.getElementById('verso-sql-tbody');");
        sb.Append("var rows=tbody.querySelectorAll('tr[data-row-index]');");
        sb.Append("var info=document.getElementById('verso-sql-page-info');");
        sb.Append("function render(){");
        sb.Append("var start=page*pageSize,end=Math.min(start+pageSize,totalRows);");
        sb.Append("for(var i=0;i<rows.length;i++){");
        sb.Append("rows[i].style.display=(i>=start&&i<end)?'':'none';}");
        sb.Append("info.textContent='Showing '+(start+1)+'-'+end+' of '+totalRows.toLocaleString()+' rows';");
        sb.Append("document.getElementById('verso-sql-prev').disabled=page===0;");
        sb.Append("document.getElementById('verso-sql-next').disabled=page>=maxPage;}");
        sb.Append("document.getElementById('verso-sql-prev').onclick=function(){if(page>0){page--;render();}};");
        sb.Append("document.getElementById('verso-sql-next').onclick=function(){if(page<maxPage){page++;render();}};");
        sb.Append("render();");
        sb.Append("})();</script>");
    }
}
