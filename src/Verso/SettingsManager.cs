using Verso.Abstractions;
using Verso.Extensions;

namespace Verso;

/// <summary>
/// Orchestrates the extension settings lifecycle: restore on open, save on close, update on change.
/// Parallels <see cref="LayoutManager"/> in managing persisted metadata through the <see cref="NotebookModel"/>.
/// </summary>
public sealed class SettingsManager
{
    private readonly IReadOnlyList<IExtensionSettings> _settableExtensions;

    public SettingsManager(IReadOnlyList<IExtensionSettings> settableExtensions)
    {
        _settableExtensions = settableExtensions ?? throw new ArgumentNullException(nameof(settableExtensions));
    }

    /// <summary>Raised when any extension setting changes.</summary>
    public event Action<string, string, object?>? OnSettingsChanged;

    /// <summary>
    /// Gets all setting definitions from all extensions that implement <see cref="IExtensionSettings"/>.
    /// </summary>
    public IReadOnlyList<(string ExtensionId, IReadOnlyList<SettingDefinition> Definitions)> GetAllDefinitions()
    {
        var result = new List<(string, IReadOnlyList<SettingDefinition>)>();
        foreach (var ext in _settableExtensions)
        {
            if (ext is IExtension extension)
            {
                result.Add((extension.ExtensionId, ext.SettingDefinitions));
            }
        }
        return result;
    }

    /// <summary>
    /// Restores persisted settings from the notebook model into each matching extension.
    /// Called during notebook open, after extensions are loaded.
    /// </summary>
    public async Task RestoreSettingsAsync(NotebookModel notebook)
    {
        ArgumentNullException.ThrowIfNull(notebook);

        foreach (var ext in _settableExtensions)
        {
            if (ext is not IExtension extension) continue;

            if (notebook.ExtensionSettings.TryGetValue(extension.ExtensionId, out var values) && values.Count > 0)
            {
                await ext.ApplySettingsAsync(values).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Saves current settings from all extensions into the notebook model.
    /// Only persists values that differ from defaults.
    /// </summary>
    public Task SaveSettingsAsync(NotebookModel notebook)
    {
        ArgumentNullException.ThrowIfNull(notebook);

        foreach (var ext in _settableExtensions)
        {
            if (ext is not IExtension extension) continue;

            var values = ext.GetSettingValues();
            var overrides = GetOverriddenValues(ext, values);

            if (overrides.Count > 0)
                notebook.ExtensionSettings[extension.ExtensionId] = overrides;
            else
                notebook.ExtensionSettings.Remove(extension.ExtensionId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Updates a single setting on the target extension and raises the change event.
    /// </summary>
    public async Task UpdateSettingAsync(string extensionId, string settingName, object? value)
    {
        ArgumentNullException.ThrowIfNull(extensionId);
        ArgumentNullException.ThrowIfNull(settingName);

        var ext = FindExtension(extensionId)
            ?? throw new InvalidOperationException($"Extension '{extensionId}' does not implement IExtensionSettings.");

        await ext.OnSettingChangedAsync(settingName, value).ConfigureAwait(false);
        OnSettingsChanged?.Invoke(extensionId, settingName, value);
    }

    /// <summary>
    /// Resets a single setting to its default value.
    /// </summary>
    public async Task ResetSettingAsync(string extensionId, string settingName)
    {
        ArgumentNullException.ThrowIfNull(extensionId);
        ArgumentNullException.ThrowIfNull(settingName);

        var ext = FindExtension(extensionId)
            ?? throw new InvalidOperationException($"Extension '{extensionId}' does not implement IExtensionSettings.");

        var definition = ext.SettingDefinitions.FirstOrDefault(d =>
            string.Equals(d.Name, settingName, StringComparison.OrdinalIgnoreCase));

        var defaultValue = definition?.DefaultValue;
        await ext.OnSettingChangedAsync(settingName, defaultValue).ConfigureAwait(false);
        OnSettingsChanged?.Invoke(extensionId, settingName, defaultValue);
    }

    private IExtensionSettings? FindExtension(string extensionId)
    {
        return _settableExtensions.FirstOrDefault(e =>
            e is IExtension ext && string.Equals(ext.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, object?> GetOverriddenValues(
        IExtensionSettings ext, IReadOnlyDictionary<string, object?> currentValues)
    {
        var overrides = new Dictionary<string, object?>();
        foreach (var (name, value) in currentValues)
        {
            var definition = ext.SettingDefinitions.FirstOrDefault(d =>
                string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

            if (definition is null) continue;

            // Only persist values that differ from the default
            if (!Equals(value, definition.DefaultValue))
                overrides[name] = value;
        }
        return overrides;
    }
}
