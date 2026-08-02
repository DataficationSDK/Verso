using Verso.Blazor.Shared.Resources;

namespace Verso.Blazor.Shared.Models;

/// <summary>
/// Panel header titles. Extension panels supply their own display name, so this
/// covers the host panels and falls back to the raw id for anything else.
/// </summary>
/// <remarks>
/// Headers read in capitals, and that is done in the stylesheet rather than here.
/// Changing a word's case is a language-specific operation: Japanese and Chinese have
/// no case at all, and doing it in code with one fixed set of rules gets some languages
/// wrong. The browser knows which language the page is in and can leave the text alone
/// where that is the right answer.
/// </remarks>
public static class PanelDisplayNames
{
    // The properties panel header says which properties, because a header has room to.
    // Its toggle, an icon with a word under it, does not.
    public static string For(string panelId) => panelId switch
    {
        HostPanels.Properties => UI.Panel_PropertiesHeading,
        _ => HostPanels.Find(panelId)?.DisplayName ?? panelId
    };
}
