namespace Verso.Abstractions;

/// <summary>
/// Describes a variable for display in the variable explorer panel.
/// </summary>
/// <param name="Name">The variable name.</param>
/// <param name="TypeName">The display name of the variable's CLR type.</param>
/// <param name="ValuePreview">A truncated string preview of the variable's value.</param>
/// <param name="IsExpandable">Whether the variable has child members that can be inspected.</param>
public sealed record VariableExplorerEntry(
    string Name,
    string TypeName,
    string ValuePreview,
    bool IsExpandable);
