using Verso.Blazor.Shared.Resources;

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

    /// <summary>Panel id of the view panel, which is offered only when there is something to choose.</summary>
    public const string View = "view";

    /// <summary>Panel id of the compare panel.</summary>
    public const string Compare = "compare";

    /// <summary>
    /// The built-in panels in display order. Orders leave room between entries so a
    /// panel can be placed among them rather than only after them.
    /// </summary>
    /// <remarks>
    /// The descriptions are the second line of each toggle's tooltip. A toggle is an
    /// icon at rest, so this is usually the only chance to explain a panel before
    /// someone decides whether to open it. Each one says what the panel shows rather
    /// than restating its name, which a tooltip on a one-word control cannot do.
    /// <para>
    /// Built on each access rather than held in a field. A list built once would keep
    /// whichever language happened to be current the first time anything touched it,
    /// and on the server that is whoever opened a notebook first.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<NotebookPanelInfo> All => new List<NotebookPanelInfo>
    {
        new("metadata",   "", UI.Panel_Metadata,   "document", null, 100, IsHostPanel: true,
            Description: UI.Panel_Metadata_Description),
        new("extensions", "", UI.Panel_Extensions, "puzzle",   null, 200, IsHostPanel: true,
            Description: UI.Panel_Extensions_Description),
        new("variables",  "", UI.Panel_Variables,  "braces",   null, 300, IsHostPanel: true,
            Description: UI.Panel_Variables_Description),
        new("settings",   "", UI.Panel_Settings,   "gear",     null, 400, IsHostPanel: true,
            Description: UI.Panel_Settings_Description),
        new(Properties,   "", UI.Panel_Properties, "list",     null, 500, IsHostPanel: true,
            Description: UI.Panel_Properties_Description),
        new(View,         "", UI.Panel_View,       "layout",   null, 600, IsHostPanel: true,
            Description: UI.Panel_View_Description),
        new(Compare,      "", UI.Panel_Compare,    "compare",  null, 700, IsHostPanel: true,
            Description: UI.Panel_Compare_Description),
    };

    /// <summary>
    /// The built-in panels available in the current state. A panel omitted here is the
    /// host-panel equivalent of an extension panel reporting itself unavailable: the
    /// properties panel when the active layout has no cell properties, the view panel
    /// when there is only one way to view the notebook.
    /// </summary>
    public static IEnumerable<NotebookPanelInfo> Available(bool supportsPropertiesPanel, bool hasViewChoices)
        => All.Where(p =>
            (supportsPropertiesPanel || p.PanelId != Properties)
            && (hasViewChoices || p.PanelId != View));

    /// <summary>
    /// Whether the view panel has anything to offer. Themes come from the host in the
    /// embedded shell, so only the layout list is offered there.
    /// </summary>
    public static bool HasViewChoices(int layoutCount, int themeCount, bool isEmbedded)
        => layoutCount > 1 || (themeCount > 1 && !isEmbedded);

    /// <summary>Looks up a host panel by id, or <c>null</c> when the id is not one.</summary>
    public static NotebookPanelInfo? Find(string panelId)
        => All.FirstOrDefault(p => string.Equals(p.PanelId, panelId, StringComparison.OrdinalIgnoreCase));
}
