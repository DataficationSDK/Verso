namespace Verso.Abstractions;

/// <summary>
/// Defines the complete set of color tokens for a Verso notebook theme.
/// All values are CSS hex color strings (e.g. <c>#FFFFFF</c>).
/// </summary>
public sealed record ThemeColorTokens
{
    // Editor

    /// <summary>Background color of code editor regions.</summary>
    public string EditorBackground { get; init; } = "#FFFFFF";

    /// <summary>Default text color within code editors.</summary>
    public string EditorForeground { get; init; } = "#1E1E1E";

    /// <summary>Color of line-number labels in the editor gutter.</summary>
    public string EditorLineNumber { get; init; } = "#858585";

    /// <summary>Color of the blinking text cursor in editors.</summary>
    public string EditorCursor { get; init; } = "#000000";

    /// <summary>Background highlight for selected text in editors.</summary>
    public string EditorSelection { get; init; } = "#ADD6FF";

    /// <summary>Background color of the editor gutter area.</summary>
    public string EditorGutter { get; init; } = "#F5F5F5";

    /// <summary>Color used to render whitespace indicator characters.</summary>
    public string EditorWhitespace { get; init; } = "#D3D3D3";

    // Cell

    /// <summary>Default background color of notebook cells.</summary>
    public string CellBackground { get; init; } = "#FFFFFF";

    /// <summary>Border color for inactive cells.</summary>
    public string CellBorder { get; init; } = "#E0E0E0";

    /// <summary>Border color for the currently focused cell.</summary>
    public string CellActiveBorder { get; init; } = "#0078D4";

    /// <summary>Background color applied when hovering over a cell.</summary>
    public string CellHoverBackground { get; init; } = "#F8F8F8";

    /// <summary>Background color of cell output regions.</summary>
    public string CellOutputBackground { get; init; } = "#F5F5F5";

    /// <summary>Text color within cell output regions.</summary>
    public string CellOutputForeground { get; init; } = "#1E1E1E";

    /// <summary>Background color for cell error output.</summary>
    public string CellErrorBackground { get; init; } = "#FDE7E9";

    /// <summary>Text color for cell error output.</summary>
    public string CellErrorForeground { get; init; } = "#A1260D";

    /// <summary>Color of the animated indicator shown while a cell is executing.</summary>
    public string CellRunningIndicator { get; init; } = "#0078D4";

    // Toolbar

    /// <summary>Background color of the main toolbar.</summary>
    public string ToolbarBackground { get; init; } = "#F3F3F3";

    /// <summary>Text and icon color within the toolbar.</summary>
    public string ToolbarForeground { get; init; } = "#1E1E1E";

    /// <summary>Background color of toolbar buttons on hover.</summary>
    public string ToolbarButtonHover { get; init; } = "#E0E0E0";

    /// <summary>Color of vertical separator lines between toolbar groups.</summary>
    public string ToolbarSeparator { get; init; } = "#D4D4D4";

    /// <summary>Foreground color for disabled toolbar buttons.</summary>
    public string ToolbarDisabledForeground { get; init; } = "#A0A0A0";

    // Sidebar

    /// <summary>Background color of the sidebar panel.</summary>
    public string SidebarBackground { get; init; } = "#F3F3F3";

    /// <summary>Text color within the sidebar.</summary>
    public string SidebarForeground { get; init; } = "#1E1E1E";

    /// <summary>Background color of sidebar items on hover.</summary>
    public string SidebarItemHover { get; init; } = "#E0E0E0";

    /// <summary>Background color of the currently active sidebar item.</summary>
    public string SidebarItemActive { get; init; } = "#D0D0D0";

    // Borders

    /// <summary>Default border color used across general UI elements.</summary>
    public string BorderDefault { get; init; } = "#E0E0E0";

    /// <summary>Border color applied to focused or active input elements.</summary>
    public string BorderFocused { get; init; } = "#0078D4";

    // Accent / Highlight

    /// <summary>Primary accent color used for interactive elements and links.</summary>
    public string AccentPrimary { get; init; } = "#0078D4";

    /// <summary>Secondary accent color for hover states and emphasis.</summary>
    public string AccentSecondary { get; init; } = "#005A9E";

    /// <summary>Background color for highlighted or marked content.</summary>
    public string HighlightBackground { get; init; } = "#FFF3CD";

    /// <summary>Text color rendered over highlighted backgrounds.</summary>
    public string HighlightForeground { get; init; } = "#664D03";

    // Status

    /// <summary>Color representing a successful or positive status.</summary>
    public string StatusSuccess { get; init; } = "#28A745";

    /// <summary>Color representing a warning or caution status.</summary>
    public string StatusWarning { get; init; } = "#FFC107";

    /// <summary>Color representing an error or failure status.</summary>
    public string StatusError { get; init; } = "#DC3545";

    /// <summary>Color representing an informational status.</summary>
    public string StatusInfo { get; init; } = "#17A2B8";

    // Scrollbar

    /// <summary>Color of the scrollbar thumb (draggable handle).</summary>
    public string ScrollbarThumb { get; init; } = "#C1C1C1";

    /// <summary>Background color of the scrollbar track.</summary>
    public string ScrollbarTrack { get; init; } = "#F1F1F1";

    /// <summary>Color of the scrollbar thumb when hovered.</summary>
    public string ScrollbarThumbHover { get; init; } = "#A8A8A8";

    // Overlay / Dropdown / Tooltip

    /// <summary>Background color of modal overlays and popups.</summary>
    public string OverlayBackground { get; init; } = "#FFFFFF";

    /// <summary>Border color of overlay panels.</summary>
    public string OverlayBorder { get; init; } = "#E0E0E0";

    /// <summary>Background color of dropdown menus.</summary>
    public string DropdownBackground { get; init; } = "#FFFFFF";

    /// <summary>Background color of dropdown items on hover.</summary>
    public string DropdownHover { get; init; } = "#F0F0F0";

    /// <summary>Background color of tooltip popups.</summary>
    public string TooltipBackground { get; init; } = "#333333";

    /// <summary>Text color within tooltip popups.</summary>
    public string TooltipForeground { get; init; } = "#FFFFFF";
}
