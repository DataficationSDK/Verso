using System.Data;
using System.Net;
using System.Text;
using Verso.Abstractions;
using Verso.Ado.Helpers;
using Verso.Ado.Kernel;

namespace Verso.Ado.MagicCommands;

/// <summary>
/// <c>#!sql-schema [--connection name] [--table name] [--refresh]</c>
/// — displays database schema information (tables, columns).
/// </summary>
[VersoExtension]
public sealed class SqlSchemaMagicCommand : IMagicCommand
{
    // --- IExtension ---
    public string ExtensionId => "verso.ado.magic.sql-schema";
    string IExtension.Name => "SQL Schema Magic Command";
    public string Version => "0.1.0";
    public string? Author => "Verso Contributors";
    public string Description => "Displays database schema information (tables, views, columns).";

    // --- IMagicCommand ---
    public string Name => "sql-schema";

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new[]
    {
        new ParameterDefinition("connection", "Name of the connection to inspect.", typeof(string)),
        new ParameterDefinition("table", "Show column details for a specific table.", typeof(string)),
        new ParameterDefinition("refresh", "Force refresh of the schema cache.", typeof(bool)),
    };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
    {
        context.SuppressExecution = true;

        var args = ArgumentParser.Parse(arguments);

        args.TryGetValue("connection", out var connectionName);
        var connInfo = ConnectionResolver.Resolve(connectionName, context.Variables);

        if (connInfo is null)
        {
            await context.WriteOutputAsync(new CellOutput("text/plain",
                connectionName is not null
                    ? $"Error: Connection '{connectionName}' not found. Use #!sql-connect to establish a connection."
                    : "Error: No database connection. Use #!sql-connect to establish a connection.",
                IsError: true)).ConfigureAwait(false);
            return;
        }

        if (connInfo.Connection is null || connInfo.Connection.State != ConnectionState.Open)
        {
            await context.WriteOutputAsync(new CellOutput("text/plain",
                $"Error: Connection '{connInfo.Name}' is not open. Reconnect with #!sql-connect.",
                IsError: true)).ConfigureAwait(false);
            return;
        }

        var schemaCache = SchemaCache.Instance;

        // Handle --refresh
        if (args.ContainsKey("refresh"))
        {
            schemaCache.Invalidate(connInfo.Name);
        }

        SchemaCacheEntry entry;
        try
        {
            entry = await schemaCache.GetOrRefreshAsync(
                connInfo.Name, connInfo.Connection, context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await context.WriteOutputAsync(new CellOutput("text/plain",
                $"Error loading schema: {ex.Message}", IsError: true)).ConfigureAwait(false);
            return;
        }

        // Render output
        if (args.TryGetValue("table", out var tableName) && !string.IsNullOrWhiteSpace(tableName))
        {
            // Show column details for a specific table
            if (!entry.Columns.TryGetValue(tableName, out var columns))
            {
                await context.WriteOutputAsync(new CellOutput("text/plain",
                    $"Error: Table '{tableName}' not found in schema.", IsError: true)).ConfigureAwait(false);
                return;
            }

            var table = entry.Tables.FirstOrDefault(t =>
                string.Equals(t.Name, tableName, StringComparison.OrdinalIgnoreCase));
            var tableLabel = table is not null
                ? $"{table.TableType}: {(table.Schema is not null ? $"{table.Schema}.{table.Name}" : table.Name)}"
                : tableName;

            var html = RenderColumnTable(tableLabel, columns, context.Theme);
            await context.WriteOutputAsync(new CellOutput("text/html", html)).ConfigureAwait(false);
        }
        else
        {
            // Show all tables
            var html = RenderTableList(connInfo.Name, entry.Tables, context.Theme);
            await context.WriteOutputAsync(new CellOutput("text/html", html)).ConfigureAwait(false);
        }
    }

    private static string RenderTableList(string connectionName, List<TableInfo> tables, IThemeContext? theme)
    {
        var bg = theme?.GetColor("CellOutputBackground") ?? "#1e1e1e";
        var fg = theme?.GetColor("CellOutputForeground") ?? "#d4d4d4";
        var border = theme?.GetColor("BorderColor") ?? "#404040";

        var sb = new StringBuilder();
        sb.AppendLine($"<div style=\"font-family:monospace;color:{fg};background:{bg};padding:8px;\">");
        sb.AppendLine($"<div style=\"margin-bottom:8px;\"><strong>Schema for connection: {WebUtility.HtmlEncode(connectionName)}</strong> ({tables.Count} tables/views)</div>");
        sb.AppendLine($"<table style=\"border-collapse:collapse;width:100%;\">");
        sb.AppendLine($"<thead><tr style=\"border-bottom:2px solid {border};\">");
        sb.AppendLine($"<th style=\"text-align:left;padding:4px 8px;\">Name</th>");
        sb.AppendLine($"<th style=\"text-align:left;padding:4px 8px;\">Schema</th>");
        sb.AppendLine($"<th style=\"text-align:left;padding:4px 8px;\">Type</th>");
        sb.AppendLine("</tr></thead><tbody>");

        foreach (var table in tables)
        {
            sb.AppendLine($"<tr style=\"border-bottom:1px solid {border};\">");
            sb.AppendLine($"<td style=\"padding:4px 8px;\">{WebUtility.HtmlEncode(table.Name)}</td>");
            sb.AppendLine($"<td style=\"padding:4px 8px;\">{WebUtility.HtmlEncode(table.Schema ?? "")}</td>");
            sb.AppendLine($"<td style=\"padding:4px 8px;\">{WebUtility.HtmlEncode(table.TableType)}</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</tbody></table></div>");
        return sb.ToString();
    }

    private static string RenderColumnTable(string tableLabel, List<ColumnInfo> columns, IThemeContext? theme)
    {
        var bg = theme?.GetColor("CellOutputBackground") ?? "#1e1e1e";
        var fg = theme?.GetColor("CellOutputForeground") ?? "#d4d4d4";
        var border = theme?.GetColor("BorderColor") ?? "#404040";

        var sb = new StringBuilder();
        sb.AppendLine($"<div style=\"font-family:monospace;color:{fg};background:{bg};padding:8px;\">");
        sb.AppendLine($"<div style=\"margin-bottom:8px;\"><strong>{WebUtility.HtmlEncode(tableLabel)}</strong> ({columns.Count} columns)</div>");
        sb.AppendLine($"<table style=\"border-collapse:collapse;width:100%;\">");
        sb.AppendLine($"<thead><tr style=\"border-bottom:2px solid {border};\">");
        sb.AppendLine($"<th style=\"text-align:left;padding:4px 8px;\">Name</th>");
        sb.AppendLine($"<th style=\"text-align:left;padding:4px 8px;\">Type</th>");
        sb.AppendLine($"<th style=\"text-align:left;padding:4px 8px;\">Nullable</th>");
        sb.AppendLine($"<th style=\"text-align:left;padding:4px 8px;\">Default</th>");
        sb.AppendLine($"<th style=\"text-align:left;padding:4px 8px;\">Key</th>");
        sb.AppendLine("</tr></thead><tbody>");

        foreach (var col in columns)
        {
            sb.AppendLine($"<tr style=\"border-bottom:1px solid {border};\">");
            sb.AppendLine($"<td style=\"padding:4px 8px;\">{WebUtility.HtmlEncode(col.Name)}</td>");
            sb.AppendLine($"<td style=\"padding:4px 8px;\">{WebUtility.HtmlEncode(col.DataType)}</td>");
            sb.AppendLine($"<td style=\"padding:4px 8px;\">{(col.IsNullable ? "YES" : "NO")}</td>");
            sb.AppendLine($"<td style=\"padding:4px 8px;\">{WebUtility.HtmlEncode(col.DefaultValue ?? "")}</td>");
            sb.AppendLine($"<td style=\"padding:4px 8px;\">{(col.IsPrimaryKey ? "PK" : "")}</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</tbody></table></div>");
        return sb.ToString();
    }
}
