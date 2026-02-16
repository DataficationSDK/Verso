namespace Verso.Abstractions;

/// <summary>
/// Defines optional validation constraints for an extension setting.
/// All properties are nullable; a <see langword="null"/> value means the constraint is not applied.
/// </summary>
/// <param name="MinValue">The minimum numeric value (inclusive). Applies to <see cref="SettingType.Integer"/> and <see cref="SettingType.Double"/> settings.</param>
/// <param name="MaxValue">The maximum numeric value (inclusive). Applies to <see cref="SettingType.Integer"/> and <see cref="SettingType.Double"/> settings.</param>
/// <param name="Pattern">A regular expression pattern that the value must match. Applies to <see cref="SettingType.String"/> settings.</param>
/// <param name="Choices">The set of allowed values. Applies to <see cref="SettingType.StringChoice"/> settings.</param>
/// <param name="MaxLength">The maximum string length. Applies to <see cref="SettingType.String"/> settings.</param>
/// <param name="MaxItems">The maximum number of items. Applies to <see cref="SettingType.StringList"/> settings.</param>
public sealed record SettingConstraints(
    double? MinValue = null,
    double? MaxValue = null,
    string? Pattern = null,
    IReadOnlyList<string>? Choices = null,
    int? MaxLength = null,
    int? MaxItems = null);
