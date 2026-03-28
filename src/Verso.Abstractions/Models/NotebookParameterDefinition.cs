namespace Verso.Abstractions;

/// <summary>
/// Defines a typed parameter declared in notebook metadata.
/// Used for parameterized notebook execution via the CLI or UI.
/// </summary>
public sealed class NotebookParameterDefinition
{
    /// <summary>
    /// The parameter type identifier: <c>string</c>, <c>int</c>, <c>float</c>, <c>bool</c>, <c>date</c>, or <c>datetime</c>.
    /// </summary>
    public string Type { get; set; } = "string";

    /// <summary>
    /// Human-readable description shown in interactive prompts and the parameters cell form.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Default value used when no override is provided. Must match the declared type.
    /// </summary>
    public object? Default { get; set; }

    /// <summary>
    /// When <c>true</c> and no default is defined, the parameter must be supplied or execution fails.
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// Display and injection ordering. Parameters are sorted ascending by this value,
    /// with unordered parameters appearing after ordered ones in alphabetical order.
    /// </summary>
    public int? Order { get; set; }
}
