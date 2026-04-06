namespace Verso.Abstractions;

/// <summary>
/// Specifies the input control type for a property field in the cell properties panel.
/// </summary>
public enum PropertyFieldType
{
    /// <summary>
    /// A free-form text input.
    /// </summary>
    Text,

    /// <summary>
    /// A numeric input.
    /// </summary>
    Number,

    /// <summary>
    /// A boolean toggle (checkbox or switch).
    /// </summary>
    Toggle,

    /// <summary>
    /// A single-selection dropdown.
    /// </summary>
    Select,

    /// <summary>
    /// A multi-selection control.
    /// </summary>
    MultiSelect,

    /// <summary>
    /// A color picker input.
    /// </summary>
    Color,

    /// <summary>
    /// A tag input with add and remove capability.
    /// </summary>
    Tags
}
