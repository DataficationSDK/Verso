namespace Verso.Abstractions;

/// <summary>
/// Augmentation interface for extensions that expose configurable settings.
/// An extension implements this alongside its primary capability interface
/// (e.g. <c>ILanguageKernel + IExtensionSettings</c>). Implementing
/// <see cref="IExtensionSettings"/> alone is not a valid extension capability.
/// </summary>
public interface IExtensionSettings
{
    /// <summary>
    /// Gets the static list of setting definitions declared by this extension.
    /// Each definition describes a single configurable value with its type, default, and constraints.
    /// </summary>
    IReadOnlyList<SettingDefinition> SettingDefinitions { get; }

    /// <summary>
    /// Gets the current values of all settings as a name-to-value dictionary.
    /// Only includes settings whose current value differs from the default,
    /// or all settings if the extension prefers to return the full set.
    /// </summary>
    IReadOnlyDictionary<string, object?> GetSettingValues();

    /// <summary>
    /// Batch-restores settings from persisted values (e.g. when opening a .verso file).
    /// Called by the engine during extension initialization with the values stored in the file.
    /// Unknown setting names should be silently ignored for forward compatibility.
    /// </summary>
    /// <param name="values">A dictionary of setting name to persisted value.</param>
    Task ApplySettingsAsync(IReadOnlyDictionary<string, object?> values);

    /// <summary>
    /// Called when a single setting is changed interactively from the UI.
    /// The extension should validate and apply the new value.
    /// </summary>
    /// <param name="name">The setting name.</param>
    /// <param name="value">The new value.</param>
    Task OnSettingChangedAsync(string name, object? value);
}
