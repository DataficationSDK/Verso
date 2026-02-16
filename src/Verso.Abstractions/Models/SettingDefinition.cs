namespace Verso.Abstractions;

/// <summary>
/// Declares a single configurable setting exposed by an extension.
/// </summary>
/// <param name="Name">The programmatic name of the setting (e.g. "warningLevel").</param>
/// <param name="DisplayName">A human-readable label shown in the settings UI.</param>
/// <param name="Description">A longer description of what the setting controls.</param>
/// <param name="SettingType">The data type of the setting value.</param>
/// <param name="DefaultValue">The default value used when no override is persisted. Defaults to <see langword="null"/>.</param>
/// <param name="Category">An optional grouping category for organizing settings in the UI (e.g. "Compiler", "Editor").</param>
/// <param name="Constraints">Optional validation constraints for the setting value.</param>
/// <param name="Order">Display order within the category. Lower values appear first. Defaults to 0.</param>
public sealed record SettingDefinition(
    string Name,
    string DisplayName,
    string Description,
    SettingType SettingType,
    object? DefaultValue = null,
    string? Category = null,
    SettingConstraints? Constraints = null,
    int Order = 0);
