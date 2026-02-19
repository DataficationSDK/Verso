namespace Verso.Sample.Slides.Models;

/// <summary>
/// Tracks which slide a cell is assigned to and its presentation properties.
/// </summary>
/// <param name="SlideNumber">The 1-based slide number this cell belongs to.</param>
/// <param name="Transition">The CSS transition style for this slide (e.g. "none", "fade").</param>
/// <param name="IsTitle">Whether this cell should be rendered as a title slide.</param>
public sealed record SlideAssignment(
    int SlideNumber,
    string Transition = "none",
    bool IsTitle = false);
