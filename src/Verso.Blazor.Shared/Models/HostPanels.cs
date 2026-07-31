namespace Verso.Blazor.Shared.Models;

/// <summary>
/// The panels this host draws itself, described in the same terms as the ones
/// extensions contribute so both kinds share one ordered list.
/// </summary>
/// <remarks>
/// These are not extensions. Each is a component with direct access to notebook
/// state, which is why they are declared here rather than implementing
/// <c>INotebookPanel</c>. What they share with extension panels is the chrome:
/// the toggle control, the ordering, and the panel shell.
/// </remarks>
public static class HostPanels
{
    /// <summary>Panel id of the cell properties panel, which not every layout supports.</summary>
    public const string Properties = "properties";

    /// <summary>
    /// The built-in panels in display order. Orders leave room between entries so a
    /// panel can be placed among them rather than only after them.
    /// </summary>
    public static readonly IReadOnlyList<NotebookPanelInfo> All = new List<NotebookPanelInfo>
    {
        new("metadata",   "", "Metadata",   "document", null, 100, IsHostPanel: true),
        new("extensions", "", "Extensions", "puzzle",   null, 200, IsHostPanel: true),
        new("variables",  "", "Variables",  "braces",   null, 300, IsHostPanel: true),
        new("settings",   "", "Settings",   "gear",     null, 400, IsHostPanel: true),
        new(Properties,   "", "Properties", "list",     null, 500, IsHostPanel: true),
    };

    /// <summary>
    /// The built-in panels available in the current state. The properties panel is
    /// omitted when the active layout does not support it, which is the host-panel
    /// equivalent of an extension panel reporting itself unavailable.
    /// </summary>
    public static IEnumerable<NotebookPanelInfo> Available(bool supportsPropertiesPanel)
        => supportsPropertiesPanel
            ? All
            : All.Where(p => p.PanelId != Properties);

    /// <summary>Looks up a host panel by id, or <c>null</c> when the id is not one.</summary>
    public static NotebookPanelInfo? Find(string panelId)
        => All.FirstOrDefault(p => string.Equals(p.PanelId, panelId, StringComparison.OrdinalIgnoreCase));
}
