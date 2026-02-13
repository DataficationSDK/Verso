using Verso.Abstractions;

namespace Verso.Extensions.Themes;

/// <summary>
/// Built-in dark theme for Verso notebooks, inspired by VS Code Dark+.
/// </summary>
[VersoExtension]
public sealed class VersoDarkTheme : ITheme
{
    // --- IExtension ---

    public string ExtensionId => "verso.theme.dark";
    public string Name => "Verso Dark";
    public string Version => "0.1.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Default dark theme for Verso notebooks.";

    // --- ITheme ---

    public string ThemeId => "verso-dark";
    public string DisplayName => "Verso Dark";
    public ThemeKind ThemeKind => ThemeKind.Dark;

    public ThemeColorTokens Colors { get; } = new ThemeColorTokens
    {
        // Editor
        EditorBackground = "#1E1E1E",
        EditorForeground = "#D4D4D4",
        EditorLineNumber = "#858585",
        EditorCursor = "#AEAFAD",
        EditorSelection = "#264F78",
        EditorGutter = "#1E1E1E",
        EditorWhitespace = "#3B3B3B",

        // Cell
        CellBackground = "#1E1E1E",
        CellBorder = "#3C3C3C",
        CellActiveBorder = "#007ACC",
        CellHoverBackground = "#2A2D2E",
        CellOutputBackground = "#252526",
        CellOutputForeground = "#D4D4D4",
        CellErrorBackground = "#5A1D1D",
        CellErrorForeground = "#F48771",
        CellRunningIndicator = "#007ACC",

        // Toolbar
        ToolbarBackground = "#333333",
        ToolbarForeground = "#CCCCCC",
        ToolbarButtonHover = "#454545",
        ToolbarSeparator = "#474747",
        ToolbarDisabledForeground = "#6B6B6B",

        // Sidebar
        SidebarBackground = "#252526",
        SidebarForeground = "#CCCCCC",
        SidebarItemHover = "#2A2D2E",
        SidebarItemActive = "#37373D",

        // Borders
        BorderDefault = "#3C3C3C",
        BorderFocused = "#007ACC",

        // Accent / Highlight
        AccentPrimary = "#007ACC",
        AccentSecondary = "#0098FF",
        HighlightBackground = "#613214",
        HighlightForeground = "#D4D4D4",

        // Status
        StatusSuccess = "#4EC9B0",
        StatusWarning = "#CCA700",
        StatusError = "#F14C4C",
        StatusInfo = "#3794FF",

        // Scrollbar
        ScrollbarThumb = "#4A4A4A",
        ScrollbarTrack = "#1E1E1E",
        ScrollbarThumbHover = "#5A5A5A",

        // Overlay / Dropdown / Tooltip
        OverlayBackground = "#252526",
        OverlayBorder = "#454545",
        DropdownBackground = "#252526",
        DropdownHover = "#094771",
        TooltipBackground = "#252526",
        TooltipForeground = "#CCCCCC"
    };

    public ThemeTypography Typography { get; } = new();
    public ThemeSpacing Spacing { get; } = new();

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public string? GetCustomToken(string key) => null;

    public SyntaxColorMap GetSyntaxColors()
    {
        var map = new SyntaxColorMap();
        map.Set("keyword", "#569CD6");
        map.Set("comment", "#6A9955");
        map.Set("string", "#CE9178");
        map.Set("number", "#B5CEA8");
        map.Set("type", "#4EC9B0");
        map.Set("function", "#DCDCAA");
        map.Set("variable", "#9CDCFE");
        map.Set("operator", "#D4D4D4");
        map.Set("punctuation", "#D4D4D4");
        map.Set("preprocessor", "#C586C0");
        map.Set("attribute", "#4EC9B0");
        map.Set("namespace", "#4EC9B0");
        return map;
    }
}
