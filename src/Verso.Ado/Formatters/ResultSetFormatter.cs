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
        => FormatNonQueryHtml(rowsAffected, 1, elapsedMs, theme);

    internal static string FormatNonQueryHtml(int rowsAffected, int statementCount, long elapsedMs, IThemeContext? theme)
    {
        var sb = new StringBuilder();
        AppendStyles(sb, theme);
        sb.Append("<div class=\"verso-sql-result\">");
        sb.Append("<div class=\"verso-sql-footer\">");
        sb.Append(rowsAffected.ToString("N0")).Append(" row(s) affected");
        if (statementCount > 1)
            sb.Append(" (").Append(statementCount).Append(" statements)");
        sb.Append(" <span style=\"opacity:0.7;\">(").Append(elapsedMs).Append(" ms)</span>");
        sb.Append("</div></div>");
        return sb.ToString();
    }

    // --- Private rendering helpers ---

    /// <summary>
    /// Emits the shared CSS block used by all SQL output tables (results, schema, etc.).
    /// Colours are resolved at render time via CSS custom properties with a three-tier
    /// fallback chain so the same HTML adapts to any host environment:
    /// <list type="number">
    ///   <item><c>--vscode-*</c> — VS Code notebook output webview</item>
    ///   <item><c>--verso-*</c>  — Blazor shell / HTML export (set by ThemeProvider / ThemeCssGenerator)</item>
    ///   <item>literal value     — safety net for isolated HTML</item>
    /// </list>
    /// The <paramref name="theme"/> parameter is retained for API compatibility but
    /// is no longer consulted; all colour decisions happen client-side.
    /// </summary>
    internal static void AppendStyles(StringBuilder sb, IThemeContext? theme)
    {
        sb.Append("<style>");

        // Scoped custom-property definitions
        sb.Append(".verso-sql-result{");
        sb.Append("--sql-bg:var(--vscode-editor-background,var(--verso-cell-output-background,#fff));");
        sb.Append("--sql-fg:var(--vscode-editor-foreground,var(--verso-cell-output-foreground,#1e1e1e));");
        sb.Append("--sql-border:var(--vscode-editorWidget-border,var(--verso-border-default,#e0e0e0));");
        sb.Append("--sql-header-bg:var(--vscode-editorWidget-background,var(--verso-cell-background,#f5f5f5));");
        sb.Append("--sql-hover:var(--vscode-list-hoverBackground,var(--verso-cell-hover-background,#f0f0f0));");
        sb.Append("--sql-accent:var(--vscode-textLink-foreground,var(--verso-accent-primary,#0078d4));");
        sb.Append("--sql-muted:var(--vscode-descriptionForeground,var(--verso-editor-line-number,#858585));");
        sb.Append("--sql-warn-bg:var(--vscode-inputValidation-warningBackground,var(--verso-highlight-background,#fff3cd));");
        sb.Append("--sql-warn-fg:var(--vscode-editorWarning-foreground,var(--verso-highlight-foreground,#664d03));");
        sb.Append("--sql-warn-border:var(--vscode-inputValidation-warningBorder,var(--verso-status-warning,#ffc107));");
        sb.Append("font-family:var(--verso-code-output-font-family,monospace);font-size:13px;color:var(--sql-fg);}");

        // Table
        sb.Append(".verso-sql-result table{border-collapse:collapse;width:auto;background:var(--sql-bg);color:var(--sql-fg);}");
        sb.Append(".verso-sql-result th{text-align:left;padding:6px 12px;border-bottom:2px solid var(--sql-border);background:var(--sql-header-bg);font-weight:600;}");
        sb.Append(".verso-sql-result td{padding:5px 12px;border-bottom:1px solid var(--sql-border);}");
        sb.Append(".verso-sql-result tbody tr:hover{background:var(--sql-hover);}");

        // Special elements
        sb.Append(".verso-sql-result .verso-sql-null{color:var(--sql-muted);font-style:italic;}");
        sb.Append(".verso-sql-result .verso-sql-pk{color:var(--sql-accent);font-weight:600;}");

        // Header / badge
        sb.Append(".verso-sql-result .verso-sql-header{margin-bottom:6px;font-size:13px;}");
        sb.Append(".verso-sql-result .verso-sql-header strong{font-size:14px;}");
        sb.Append(".verso-sql-result .verso-sql-header .verso-sql-badge{opacity:0.7;font-weight:normal;}");

        // Pager controls
        sb.Append(".verso-sql-pager{padding:6px 0;}");
        sb.Append(".verso-sql-pager button{margin-right:4px;padding:2px 10px;background:var(--sql-header-bg);color:var(--sql-fg);border:1px solid var(--sql-border);border-radius:3px;cursor:pointer;font-family:inherit;font-size:12px;}");
        sb.Append(".verso-sql-pager button:hover:not(:disabled){background:var(--sql-hover);}");
        sb.Append(".verso-sql-pager button:disabled{opacity:0.4;cursor:default;}");

        // Footer & truncation warning
        sb.Append(".verso-sql-footer{padding:6px 0;color:var(--sql-muted);font-size:12px;}");
        sb.Append(".verso-sql-truncation{padding:6px 8px;margin-top:4px;background:var(--sql-warn-bg);color:var(--sql-warn-fg);border:1px solid var(--sql-warn-border);border-radius:4px;font-size:12px;}");

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
