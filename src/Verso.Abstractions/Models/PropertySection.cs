namespace Verso.Abstractions;

/// <summary>
/// A named group of property fields displayed as a collapsible section in the cell properties panel.
/// </summary>
/// <param name="Title">The section heading displayed in the panel.</param>
/// <param name="Description">Optional summary text shown below the heading.</param>
/// <param name="Fields">The property fields contained in this section.</param>
public sealed record PropertySection(
    string Title,
    string? Description,
    IReadOnlyList<PropertyField> Fields);
