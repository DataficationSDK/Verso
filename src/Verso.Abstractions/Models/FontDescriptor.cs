namespace Verso.Abstractions;

/// <summary>
/// Describes the font settings used to render text within a notebook cell.
/// </summary>
/// <param name="Family">The CSS font-family name (e.g. "Cascadia Code", "Consolas").</param>
/// <param name="SizePx">The font size in pixels.</param>
/// <param name="Weight">The font weight (100-900). Defaults to 400 (normal).</param>
/// <param name="LineHeight">The line-height multiplier relative to the font size. Defaults to 1.4.</param>
public sealed record FontDescriptor(
    string Family,
    double SizePx,
    int Weight = 400,
    double LineHeight = 1.4);
