namespace Verso.Abstractions;

/// <summary>
/// Represents a selectable option for <see cref="PropertyFieldType.Select"/>
/// and <see cref="PropertyFieldType.MultiSelect"/> property fields.
/// </summary>
/// <param name="Value">The stored value when this option is selected.</param>
/// <param name="DisplayName">The human-readable label shown in the UI.</param>
public sealed record PropertyFieldOption(
    string Value,
    string DisplayName);
