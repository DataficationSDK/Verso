using System.Data;
using System.Net;
using System.Text;
using Verso.Abstractions;
using Verso.Ado.Formatters;
using Verso.Ado.Helpers;
using Verso.Ado.Kernel;
using Verso.Ado.Localization;
using Verso.Ado.Resources;

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
    string IExtension.Name => Strings.Magic_Schema_Name;
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string Description => Strings.Magic_Schema_Description;

    // --- IMagicCommand ---
    public string Name => "sql-schema";

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new[]
    {
        new ParameterDefinition("connection", Strings.Magic_Schema_Param_Connection, typeof(string)),
        new ParameterDefinition("table", Strings.Magic_Schema_Param_Table, typeof(string)),
        new ParameterDefinition("refresh", Strings.Magic_Schema_Param_Refresh, typeof(bool)),
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
                    ? CellText.Error(string.Format(Strings.Magic_Schema_NotFound, connectionName))
                    : CellText.Error(Strings.Magic_Schema_NoConnection),
                IsError: true)).ConfigureAwait(false);
            return;
        }

        if (connInfo.Connection is null || connInfo.Connection.State != ConnectionState.Open)
        {
            await context.WriteOutputAsync(new CellOutput("text/plain",
                CellText.Error(string.Format(Strings.Magic_Schema_ConnectionClosed, connInfo.Name)),
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
                string.Format(Strings.Magic_Schema_LoadFailed, ex.Message), IsError: true)).ConfigureAwait(false);
            return;
        }

        // Render output
        if (args.TryGetValue("table", out var tableName) && !string.IsNullOrWhiteSpace(tableName))
        {
            // Show column details for a specific table
            if (!entry.Columns.TryGetValue(tableName, out var columns))
            {
                await context.WriteOutputAsync(new CellOutput("text/plain",
                    CellText.Error(string.Format(Strings.Magic_Schema_TableNotFound, tableName)), IsError: true)).ConfigureAwait(false);
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
        var sb = new StringBuilder();
        ResultSetFormatter.AppendStyles(sb, theme);

        sb.Append("<div class=\"verso-sql-result\">");
        sb.Append("<div class=\"verso-sql-header\"><strong>")
          .Append(WebUtility.HtmlEncode(string.Format(Strings.Magic_Schema_Heading, connectionName)))
          .Append("</strong> <span class=\"verso-sql-badge\">")
          .Append(WebUtility.HtmlEncode(string.Format(Strings.Magic_Schema_TableCount, tables.Count)))
          .Append("</span></div>");

        sb.Append("<table><thead><tr>");
        sb.Append("<th>").Append(WebUtility.HtmlEncode(Strings.Magic_Schema_ColumnName))
          .Append("</th><th>").Append(WebUtility.HtmlEncode(Strings.Magic_Schema_ColumnSchema))
          .Append("</th><th>").Append(WebUtility.HtmlEncode(Strings.Magic_Schema_ColumnType))
          .Append("</th>");
        sb.Append("</tr></thead><tbody>");

        foreach (var table in tables)
        {
            sb.Append("<tr>");
            sb.Append("<td>").Append(WebUtility.HtmlEncode(table.Name)).Append("</td>");
            sb.Append("<td>").Append(WebUtility.HtmlEncode(table.Schema ?? "")).Append("</td>");
            sb.Append("<td>").Append(WebUtility.HtmlEncode(table.TableType)).Append("</td>");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table></div>");
        return sb.ToString();
    }

    private static string RenderColumnTable(string tableLabel, List<ColumnInfo> columns, IThemeContext? theme)
    {
        var sb = new StringBuilder();
        ResultSetFormatter.AppendStyles(sb, theme);

        sb.Append("<div class=\"verso-sql-result\">");
        sb.Append("<div class=\"verso-sql-header\"><strong>")
          .Append(WebUtility.HtmlEncode(tableLabel))
          .Append("</strong> <span class=\"verso-sql-badge\">")
          .Append(WebUtility.HtmlEncode(string.Format(Strings.Magic_Schema_ColumnCount, columns.Count)))
          .Append("</span></div>");

        sb.Append("<table><thead><tr>");
        sb.Append("<th>").Append(WebUtility.HtmlEncode(Strings.Magic_Schema_ColumnName))
          .Append("</th><th>").Append(WebUtility.HtmlEncode(Strings.Magic_Schema_ColumnType))
          .Append("</th><th>").Append(WebUtility.HtmlEncode(Strings.Magic_Schema_ColumnNullable))
          .Append("</th><th>").Append(WebUtility.HtmlEncode(Strings.Magic_Schema_ColumnDefault))
          .Append("</th><th>").Append(WebUtility.HtmlEncode(Strings.Magic_Schema_ColumnKey))
          .Append("</th>");
        sb.Append("</tr></thead><tbody>");

        foreach (var col in columns)
        {
            sb.Append("<tr>");
            sb.Append("<td>").Append(WebUtility.HtmlEncode(col.Name)).Append("</td>");
            sb.Append("<td>").Append(WebUtility.HtmlEncode(col.DataType)).Append("</td>");
            sb.Append("<td>").Append(col.IsNullable ? "YES" : "NO").Append("</td>");
            sb.Append("<td>").Append(WebUtility.HtmlEncode(col.DefaultValue ?? "")).Append("</td>");
            sb.Append("<td>").Append(col.IsPrimaryKey ? "<span class=\"verso-sql-pk\">PK</span>" : "").Append("</td>");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table></div>");
        return sb.ToString();
    }
}
