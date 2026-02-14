using Verso.Abstractions;
using Verso.Export;

namespace Verso.Extensions.ToolbarActions;

/// <summary>
/// Toolbar action that exports the notebook as a self-contained HTML document.
/// </summary>
[VersoExtension]
public sealed class ExportHtmlAction : IToolbarAction
{
    // --- IExtension ---

    public string ExtensionId => "verso.action.export-html";
    public string Name => "Export HTML";
    public string Version => "0.1.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Exports the notebook as a self-contained HTML document.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- IToolbarAction ---

    public string ActionId => "verso.action.export-html";
    public string DisplayName => "HTML";
    public string? Icon => null;
    public ToolbarPlacement Placement => ToolbarPlacement.MainToolbar;
    public int Order => 60;

    public Task<bool> IsEnabledAsync(IToolbarActionContext context)
    {
        var enabled = context.NotebookCells.Count > 0;
        return Task.FromResult(enabled);
    }

    public async Task ExecuteAsync(IToolbarActionContext context)
    {
        var title = context.NotebookMetadata.Title;

        // Resolve the active theme
        ITheme? activeTheme = null;
        var themes = context.ExtensionHost.GetThemes();
        var themeKind = context.Theme.ThemeKind;
        activeTheme = themes.FirstOrDefault(t => t.ThemeKind == themeKind)
                      ?? themes.FirstOrDefault();

        var data = NotebookHtmlExporter.Export(title, context.NotebookCells, activeTheme);
        var fileName = SanitizeFileName(title, ".html");

        await context.RequestFileDownloadAsync(fileName, "text/html", data).ConfigureAwait(false);
    }

    internal static string SanitizeFileName(string? title, string extension)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "notebook" + extension;

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(title.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray()).Trim();

        return string.IsNullOrWhiteSpace(sanitized)
            ? "notebook" + extension
            : sanitized + extension;
    }
}
