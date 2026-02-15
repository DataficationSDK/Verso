using System.Data;
using System.Text;
using System.Text.Json;
using Verso.Abstractions;

namespace Verso.Ado.ToolbarActions;

/// <summary>
/// Toolbar action that exports a SQL cell's result set as a JSON file.
/// </summary>
[VersoExtension]
public sealed class ExportJsonAction : IToolbarAction
{
    // --- IExtension ---

    public string ExtensionId => "verso.ado.action.export-json";
    public string Name => "Export JSON";
    public string Version => "0.1.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Exports SQL result sets as JSON files.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- IToolbarAction ---

    public string ActionId => "verso.ado.action.export-json";
    public string DisplayName => "JSON";
    public string? Icon =>
        "<svg viewBox=\"0 0 16 16\" width=\"14\" height=\"14\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.3\" stroke-linecap=\"round\" stroke-linejoin=\"round\">"
        + "<path d=\"M5 2.5C3.8 2.5 3 3.3 3 4.2c0 .7.4 1.2.4 2s-.4 1.2-.9 1.8c.5.6.9 1 .9 1.8s-.4 1.3-.4 2c0 .9.8 1.7 2 1.7\"/>"
        + "<path d=\"M11 2.5c1.2 0 2 .8 2 1.7 0 .7-.4 1.2-.4 2s.4 1.2.9 1.8c-.5.6-.9 1-.9 1.8s.4 1.3.4 2c0 .9-.8 1.7-2 1.7\"/>"
        + "</svg>";
    public ToolbarPlacement Placement => ToolbarPlacement.CellToolbar;
    public int Order => 81;

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

            var json = BuildJson(dt);
            var data = Encoding.UTF8.GetBytes(json);
            var fileName = $"query_result_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";

            await context.RequestFileDownloadAsync(fileName, "application/json", data).ConfigureAwait(false);
            return;
        }
    }

    internal static string BuildJson(DataTable dt)
    {
        var rows = new List<Dictionary<string, object?>>();

        foreach (DataRow dr in dt.Rows)
        {
            var obj = new Dictionary<string, object?>();
            for (int i = 0; i < dt.Columns.Count; i++)
            {
                var value = dr[i];
                obj[dt.Columns[i].ColumnName] = value is DBNull ? null : value;
            }
            rows.Add(obj);
        }

        return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
    }

    private static DataTable? ResolveDataTable(Guid cellId, IVariableStore variables)
    {
        var variableName = variables.Get<string>($"__verso_ado_cellvar_{cellId}");
        if (variableName is null)
            return null;

        return variables.Get<DataTable>(variableName);
    }
}
