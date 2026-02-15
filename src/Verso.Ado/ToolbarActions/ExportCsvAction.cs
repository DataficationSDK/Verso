using System.Data;
using System.Text;
using Verso.Abstractions;

namespace Verso.Ado.ToolbarActions;

/// <summary>
/// Toolbar action that exports a SQL cell's result set as a CSV file.
/// </summary>
[VersoExtension]
public sealed class ExportCsvAction : IToolbarAction
{
    // --- IExtension ---

    public string ExtensionId => "verso.ado.action.export-csv";
    public string Name => "Export CSV";
    public string Version => "0.1.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Exports SQL result sets as CSV files.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- IToolbarAction ---

    public string ActionId => "verso.ado.action.export-csv";
    public string DisplayName => "CSV";
    public string? Icon =>
        "<svg viewBox=\"0 0 16 16\" width=\"14\" height=\"14\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.3\" stroke-linecap=\"round\" stroke-linejoin=\"round\">"
        + "<rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" rx=\"1\"/>"
        + "<line x1=\"2\" y1=\"6\" x2=\"14\" y2=\"6\"/>"
        + "<line x1=\"2\" y1=\"10\" x2=\"14\" y2=\"10\"/>"
        + "<line x1=\"6\" y1=\"2\" x2=\"6\" y2=\"14\"/>"
        + "<line x1=\"10\" y1=\"2\" x2=\"10\" y2=\"14\"/>"
        + "</svg>";
    public ToolbarPlacement Placement => ToolbarPlacement.CellToolbar;
    public int Order => 80;

    public Task<bool> IsEnabledAsync(IToolbarActionContext context)
    {
        foreach (var cellId in context.SelectedCellIds)
        {
            var dt = ResolveDataTable(cellId, context.Variables);
            if (dt is not null)
                return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public async Task ExecuteAsync(IToolbarActionContext context)
    {
        foreach (var cellId in context.SelectedCellIds)
        {
            var dt = ResolveDataTable(cellId, context.Variables);
            if (dt is null)
                continue;

            var csv = BuildCsv(dt);
            var data = Encoding.UTF8.GetBytes(csv);
            var fileName = $"query_result_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";

            await context.RequestFileDownloadAsync(fileName, "text/csv", data).ConfigureAwait(false);
            return;
        }
    }

    internal static string BuildCsv(DataTable dt)
    {
        var sb = new StringBuilder();

        // Header row
        for (int i = 0; i < dt.Columns.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(CsvEscape(dt.Columns[i].ColumnName));
        }
        sb.AppendLine();

        // Data rows
        foreach (DataRow row in dt.Rows)
        {
            for (int i = 0; i < dt.Columns.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var value = row[i];
                if (value is null || value is DBNull)
                {
                    // Empty field for null
                }
                else
                {
                    sb.Append(CsvEscape(value.ToString() ?? ""));
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    internal static string CsvEscape(string value)
    {
        // RFC 4180: quote if contains comma, double-quote, newline, or carriage return
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    private static DataTable? ResolveDataTable(Guid cellId, IVariableStore variables)
    {
        var variableName = variables.Get<string>($"__verso_ado_cellvar_{cellId}");
        if (variableName is null)
            return null;

        return variables.Get<DataTable>(variableName);
    }
}
