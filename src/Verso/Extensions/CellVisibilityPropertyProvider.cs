using System.Text.Json;
using Verso.Abstractions;

namespace Verso.Extensions;

/// <summary>
/// Built-in property provider that surfaces per-layout cell visibility overrides
/// in the properties panel. Queries registered layouts, filters to those with
/// filtering capabilities, and builds Select fields for each qualifying layout.
/// </summary>
[VersoExtension]
public sealed class CellVisibilityPropertyProvider : ICellPropertyProvider
{
    private const string MetadataKey = "verso:visibility";
    private const string FieldPrefix = "visibility:";

    private IExtensionHostContext? _context;

    // --- IExtension ---

    public string ExtensionId => "verso.propertyprovider.visibility";
    public string Name => "Cell Visibility Properties";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Provides per-layout cell visibility overrides in the properties panel.";

    // --- ICellPropertyProvider ---

    public int Order => 0;

    public Task OnLoadedAsync(IExtensionHostContext context)
    {
        _context = context;
        return Task.CompletedTask;
    }

    public Task OnUnloadedAsync()
    {
        _context = null;
        return Task.CompletedTask;
    }

    public bool AppliesTo(CellModel cell, ICellRenderContext context) => true;

    public Task<PropertySection> GetPropertiesSectionAsync(CellModel cell, ICellRenderContext context)
    {
        var layouts = _context?.GetLayouts() ?? Array.Empty<ILayoutEngine>();
        var renderers = _context?.GetRenderers() ?? Array.Empty<ICellRenderer>();

        // Find the renderer for this cell type to determine the default hint
        var renderer = renderers.FirstOrDefault(r => r.CellTypeId == cell.Type);
        var defaultHint = renderer?.DefaultVisibility ?? CellVisibilityHint.Content;

        var fields = new List<PropertyField>();

        foreach (var layout in layouts)
        {
            var supported = layout.SupportedVisibilityStates;

            // Skip layouts that only support Visible (no filtering, e.g. notebook)
            if (supported.Count <= 1 && supported.Contains(CellVisibilityState.Visible))
                continue;

            var currentValue = ReadOverride(cell, layout.LayoutId);
            var defaultState = MapHintToDefaultStateName(defaultHint, supported);

            var options = supported
                .OrderBy(s => s)
                .Select(s => new PropertyFieldOption(s.ToString().ToLowerInvariant(), FormatStateName(s)))
                .ToList();

            fields.Add(new PropertyField(
                Name: $"{FieldPrefix}{layout.LayoutId}",
                DisplayName: layout.DisplayName,
                FieldType: PropertyFieldType.Select,
                CurrentValue: currentValue,
                Description: $"Default: {defaultState}",
                Options: options));
        }

        var section = new PropertySection("Visibility", null, fields);
        return Task.FromResult(section);
    }

    public Task OnPropertyChangedAsync(CellModel cell, string propertyName, object? value, ICellRenderContext context)
    {
        if (!propertyName.StartsWith(FieldPrefix, StringComparison.Ordinal))
            return Task.CompletedTask;

        var layoutId = propertyName.Substring(FieldPrefix.Length);
        var stringValue = value?.ToString();

        // Determine if this is the default so we can remove the override
        var renderers = _context?.GetRenderers() ?? Array.Empty<ICellRenderer>();
        var renderer = renderers.FirstOrDefault(r => r.CellTypeId == cell.Type);
        var defaultHint = renderer?.DefaultVisibility ?? CellVisibilityHint.Content;

        var layouts = _context?.GetLayouts() ?? Array.Empty<ILayoutEngine>();
        var layout = layouts.FirstOrDefault(l => l.LayoutId == layoutId);
        var supported = layout?.SupportedVisibilityStates
            ?? new HashSet<CellVisibilityState> { CellVisibilityState.Visible };
        var defaultStateName = MapHintToDefaultStateName(defaultHint, supported).ToLowerInvariant();

        var isDefault = string.IsNullOrEmpty(stringValue) ||
                        string.Equals(stringValue, defaultStateName, StringComparison.OrdinalIgnoreCase);

        // Get or create the visibility dictionary, handling all storage forms:
        // - Dictionary<string, string> from in-memory edits
        // - Dictionary<string, object> from some deserialization paths
        // - JsonElement from .verso file deserialization
        Dictionary<string, string> visibilityDict;

        if (cell.Metadata.TryGetValue(MetadataKey, out var existing))
        {
            visibilityDict = existing switch
            {
                Dictionary<string, string> dictStr => dictStr,
                Dictionary<string, object> dictObj => dictObj
                    .Where(kv => kv.Value is not null)
                    .ToDictionary(kv => kv.Key, kv => kv.Value!.ToString()!),
                JsonElement jsonEl when jsonEl.ValueKind == JsonValueKind.Object => jsonEl
                    .EnumerateObject()
                    .Where(p => p.Value.ValueKind == JsonValueKind.String)
                    .ToDictionary(p => p.Name, p => p.Value.GetString()!),
                _ => new Dictionary<string, string>(),
            };
            // Replace the stored value with the mutable dictionary
            cell.Metadata[MetadataKey] = visibilityDict;
        }
        else
        {
            visibilityDict = new Dictionary<string, string>();
            cell.Metadata[MetadataKey] = visibilityDict;
        }

        if (isDefault)
        {
            visibilityDict.Remove(layoutId);
            if (visibilityDict.Count == 0)
                cell.Metadata.Remove(MetadataKey);
        }
        else
        {
            visibilityDict[layoutId] = stringValue!;
        }

        return Task.CompletedTask;
    }

    // --- Private helpers ---

    private static string? ReadOverride(CellModel cell, string layoutId)
    {
        if (!cell.Metadata.TryGetValue(MetadataKey, out var visibilityObj))
            return null;

        switch (visibilityObj)
        {
            case Dictionary<string, string> dictStr:
                return dictStr.TryGetValue(layoutId, out var strVal) ? strVal : null;

            case Dictionary<string, object> dictObj:
                return dictObj.TryGetValue(layoutId, out var objVal) ? objVal?.ToString() : null;

            case JsonElement jsonEl when jsonEl.ValueKind == JsonValueKind.Object:
                if (jsonEl.TryGetProperty(layoutId, out var prop) && prop.ValueKind == JsonValueKind.String)
                    return prop.GetString();
                return null;

            default:
                return null;
        }
    }

    private static string MapHintToDefaultStateName(
        CellVisibilityHint hint, IReadOnlySet<CellVisibilityState> supported)
    {
        return hint switch
        {
            CellVisibilityHint.Infrastructure =>
                supported.Contains(CellVisibilityState.Hidden) ? FormatStateName(CellVisibilityState.Hidden) : FormatStateName(CellVisibilityState.Visible),
            CellVisibilityHint.OutputOnly =>
                supported.Contains(CellVisibilityState.OutputOnly) ? FormatStateName(CellVisibilityState.OutputOnly) : FormatStateName(CellVisibilityState.Visible),
            _ => FormatStateName(CellVisibilityState.Visible),
        };
    }

    private static string FormatStateName(CellVisibilityState state) => state switch
    {
        CellVisibilityState.Visible => "Visible",
        CellVisibilityState.Hidden => "Hidden",
        CellVisibilityState.OutputOnly => "Output Only",
        CellVisibilityState.Collapsed => "Collapsed",
        _ => state.ToString(),
    };
}
