using System.Net;
using System.Text;
using Verso.Abstractions;
using Verso.Ado.MagicCommands;

namespace Verso.Ado.CellType;

/// <summary>
/// Renders SQL cell input (connection indicator) and output (pass-through).
/// Owned by <see cref="SqlCellType"/>; not independently registered.
/// </summary>
public sealed class SqlCellRenderer : ICellRenderer
{
    // --- IExtension ---

    public string ExtensionId => "verso.ado.renderer.sql";
    public string Name => "SQL Renderer";
    public string Version => "0.1.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Renders SQL cells with connection indicator badges.";

    // --- ICellRenderer ---

    public string CellTypeId => "sql";
    public string DisplayName => "SQL";
    public bool CollapsesInputOnExecute => false;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<RenderResult> RenderInputAsync(string source, ICellRenderContext context)
    {
        // Determine active connection name
        string? connectionName = null;

        // Check if the first line has a --connection directive
        if (!string.IsNullOrEmpty(source))
        {
            var firstLine = source.Split('\n')[0].TrimStart();
            if (firstLine.StartsWith("--connection", StringComparison.OrdinalIgnoreCase))
            {
                var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                    connectionName = parts[1];
            }
        }

        // Fall back to default connection
        connectionName ??= context.Variables.Get<string>(SqlConnectMagicCommand.DefaultConnectionStoreKey);

        string html;
        if (!string.IsNullOrEmpty(connectionName))
        {
            html = $"<div class=\"verso-sql-connection-indicator\">Connected: {WebUtility.HtmlEncode(connectionName)}</div>";
        }
        else
        {
            html = "<div class=\"verso-sql-connection-indicator verso-sql-disconnected\">No connection</div>";
        }

        return Task.FromResult(new RenderResult("text/html", html));
    }

    public Task<RenderResult> RenderOutputAsync(CellOutput output, ICellRenderContext context)
    {
        // Pass through - kernel already produces formatted HTML via ResultSetFormatter
        return Task.FromResult(new RenderResult(output.MimeType, output.Content));
    }

    public string? GetEditorLanguage() => "sql";
}
