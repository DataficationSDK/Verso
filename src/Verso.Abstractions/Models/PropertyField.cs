namespace Verso.Abstractions;

/// <summary>
/// Defines a single editable property within a <see cref="PropertySection"/>.
/// </summary>
/// <param name="Name">The programmatic name used to identify this field in change callbacks.</param>
/// <param name="DisplayName">The human-readable label shown in the properties panel.</param>
/// <param name="FieldType">The input control type for this field.</param>
/// <param name="CurrentValue">The current value of the property, or null if unset.</param>
/// <param name="Description">Optional tooltip or help text for the field.</param>
/// <param name="Options">Available options for <see cref="PropertyFieldType.Select"/> and <see cref="PropertyFieldType.MultiSelect"/> fields.</param>
/// <param name="IsReadOnly">When true, the field is displayed but cannot be edited.</param>
public sealed record PropertyField(
    string Name,
    string DisplayName,
    PropertyFieldType FieldType,
    object? CurrentValue,
    string? Description = null,
    IReadOnlyList<PropertyFieldOption>? Options = null,
    bool IsReadOnly = false);
