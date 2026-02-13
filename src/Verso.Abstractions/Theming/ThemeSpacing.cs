namespace Verso.Abstractions;

/// <summary>
/// Defines the spacing and dimension constants for a Verso notebook theme.
/// All values are in device-independent pixels.
/// </summary>
public sealed record ThemeSpacing
{
    /// <summary>Inner padding within each notebook cell, in pixels.</summary>
    public double CellPadding { get; init; } = 12;

    /// <summary>Vertical gap between consecutive cells, in pixels.</summary>
    public double CellGap { get; init; } = 8;

    /// <summary>Height of the main toolbar, in pixels.</summary>
    public double ToolbarHeight { get; init; } = 40;

    /// <summary>Width of the sidebar panel, in pixels.</summary>
    public double SidebarWidth { get; init; } = 260;

    /// <summary>Horizontal margin around the main content area, in pixels.</summary>
    public double ContentMarginHorizontal { get; init; } = 24;

    /// <summary>Vertical margin around the main content area, in pixels.</summary>
    public double ContentMarginVertical { get; init; } = 16;

    /// <summary>Corner border radius for notebook cells, in pixels.</summary>
    public double CellBorderRadius { get; init; } = 4;

    /// <summary>Corner border radius for buttons, in pixels.</summary>
    public double ButtonBorderRadius { get; init; } = 4;

    /// <summary>Inner padding within cell output regions, in pixels.</summary>
    public double OutputPadding { get; init; } = 8;

    /// <summary>Width of scrollbar tracks, in pixels.</summary>
    public double ScrollbarWidth { get; init; } = 10;
}
