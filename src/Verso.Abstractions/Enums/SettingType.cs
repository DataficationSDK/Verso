namespace Verso.Abstractions;

/// <summary>
/// Specifies the data type of an extension setting value.
/// </summary>
public enum SettingType
{
    /// <summary>
    /// A free-form text value.
    /// </summary>
    String,

    /// <summary>
    /// A whole number value (System.Int32).
    /// </summary>
    Integer,

    /// <summary>
    /// A floating-point number value (System.Double).
    /// </summary>
    Double,

    /// <summary>
    /// A true/false value.
    /// </summary>
    Boolean,

    /// <summary>
    /// A string value restricted to a fixed set of choices.
    /// </summary>
    StringChoice,

    /// <summary>
    /// An ordered list of string values.
    /// </summary>
    StringList
}
