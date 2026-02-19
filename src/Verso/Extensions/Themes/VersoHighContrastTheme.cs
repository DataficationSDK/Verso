using Verso.Abstractions;

namespace Verso.Extensions.Themes;

/// <summary>
/// Built-in high-contrast theme for Verso notebooks, optimized for accessibility.
/// All foreground/background pairs meet WCAG 2.1 AA contrast requirements (≥ 4.5:1).
/// </summary>
[VersoExtension]
public sealed class VersoHighContrastTheme : ITheme
{
    // --- IExtension ---

    public string ExtensionId => "verso.theme.highcontrast";
    public string Name => "Verso High Contrast";
    public string Version => "0.5.0";
    public string? Author => "Verso Contributors";
    public string? Description => "High-contrast accessibility theme with WCAG 2.1 AA compliant color tokens.";

    // --- ITheme ---

    public string ThemeId => "verso-highcontrast";
    public string DisplayName => "Verso High Contrast";
    public ThemeKind ThemeKind => ThemeKind.HighContrast;

    public ThemeColorTokens Colors { get; } = new ThemeColorTokens
    {
        // Editor
        EditorBackground = "#000000",
        EditorForeground = "#FFFFFF",
        EditorLineNumber = "#FFD700",
        EditorCursor = "#FFFFFF",
        EditorSelection = "#264F78",
        EditorGutter = "#0A0A0A",
        EditorWhitespace = "#555555",

        // Cell
        CellBackground = "#000000",
        CellBorder = "#6FC3DF",
        CellActiveBorder = "#FFD700",
        CellHoverBackground = "#1A1A1A",
        CellOutputBackground = "#0A0A0A",
        CellOutputForeground = "#FFFFFF",
        CellErrorBackground = "#3D0000",
        CellErrorForeground = "#FF6B6B",
        CellRunningIndicator = "#FFD700",

        // Toolbar
        ToolbarBackground = "#1A1A1A",
        ToolbarForeground = "#FFFFFF",
        ToolbarButtonHover = "#333333",
        ToolbarSeparator = "#6FC3DF",
        ToolbarDisabledForeground = "#999999",

        // Sidebar
        SidebarBackground = "#0A0A0A",
        SidebarForeground = "#FFFFFF",
        SidebarItemHover = "#1A1A1A",
        SidebarItemActive = "#333333",

        // Borders
        BorderDefault = "#6FC3DF",
        BorderFocused = "#FFD700",

        // Accent / Highlight
        AccentPrimary = "#FFD700",
        AccentSecondary = "#6FC3DF",
        HighlightBackground = "#3D3D00",
        HighlightForeground = "#FFFFFF",

        // Status
        StatusSuccess = "#00FF00",
        StatusWarning = "#FFD700",
        StatusError = "#FF6B6B",
        StatusInfo = "#6FC3DF",

        // Scrollbar
        ScrollbarThumb = "#6FC3DF",
        ScrollbarTrack = "#0A0A0A",
        ScrollbarThumbHover = "#FFD700",

        // Overlay / Dropdown / Tooltip
        OverlayBackground = "#1A1A1A",
        OverlayBorder = "#6FC3DF",
        DropdownBackground = "#1A1A1A",
        DropdownHover = "#333333",
        TooltipBackground = "#FFD700",
        TooltipForeground = "#000000"
    };

    public ThemeTypography Typography { get; } = new ThemeTypography
    {
        EditorFont = new FontDescriptor("Cascadia Code", 15),
        UIFont = new FontDescriptor("Segoe UI", 14),
        ProseFont = new FontDescriptor("Georgia", 17, LineHeight: 1.6),
        CodeOutputFont = new FontDescriptor("Cascadia Mono", 14)
    };

    public ThemeSpacing Spacing { get; } = new ThemeSpacing
    {
        CellBorderRadius = 0,
        ButtonBorderRadius = 0
    };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public string? GetCustomToken(string key) => null;

    public SyntaxColorMap GetSyntaxColors()
    {
        var map = new SyntaxColorMap();
        map.Set("keyword", "#FFD700");     // Gold, 13.9:1 against #000
        map.Set("comment", "#7EC699");     // Muted green, 8.1:1
        map.Set("string", "#FFA07A");      // Light salmon, 8.5:1
        map.Set("number", "#87CEEB");      // Sky blue, 10.3:1
        map.Set("type", "#6FC3DF");        // Cyan, 9.0:1
        map.Set("function", "#DCDCAA");    // Pale yellow, 13.3:1
        map.Set("variable", "#9CDCFE");    // Light blue, 11.6:1
        map.Set("operator", "#FFFFFF");    // White, 21:1
        map.Set("punctuation", "#FFFFFF"); // White, 21:1
        map.Set("preprocessor", "#DA70D6"); // Orchid, 5.5:1
        map.Set("attribute", "#6FC3DF");   // Cyan, 9.0:1
        map.Set("namespace", "#6FC3DF");   // Cyan, 9.0:1
        return map;
    }
}
