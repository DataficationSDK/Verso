namespace Verso.Blazor.Shared.Models;

/// <summary>
/// Panel header titles. Extension panels supply their own display name, so this
/// covers the host panels and falls back to the raw id for anything else.
/// </summary>
public static class PanelDisplayNames
{
    // The properties panel header reads "CELL PROPERTIES" even though its toggle is
    // labelled "Properties", so that one entry keeps its own wording rather than
    // deriving from HostPanels.
    public static string For(string panelId) => panelId switch
    {
        HostPanels.Properties => "CELL PROPERTIES",
        _ => (HostPanels.Find(panelId)?.DisplayName ?? panelId).ToUpperInvariant()
    };
}
