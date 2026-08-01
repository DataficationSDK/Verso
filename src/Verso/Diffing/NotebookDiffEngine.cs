using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Verso.Abstractions;

namespace Verso.Diffing;

/// <summary>
/// Computes a cell-level structural diff between two notebook versions.
/// </summary>
public static class NotebookDiffEngine
{
    /// <summary>
    /// Compares <paramref name="baseline"/> against <paramref name="current"/> without a
    /// cell-type registry, so every cell's outputs take part in the comparison.
    /// </summary>
    public static NotebookDiffResult Compute(NotebookModel baseline, NotebookModel current, string baselineLabel)
        => Compute(baseline, current, baselineLabel, cellTypes: null);

    /// <summary>
    /// Compares <paramref name="baseline"/> against <paramref name="current"/> and returns the
    /// aligned cell entries, notebook-level metadata changes, and summary counts.
    /// </summary>
    /// <remarks>
    /// The modified timestamp and format version are intentionally excluded from the metadata
    /// comparison: both change on every save and would report noise rather than meaningful edits.
    /// Parameters, extension settings, and per-layout state are compared through a canonical
    /// JSON form (recursively key-sorted), so a baseline holding <see cref="JsonElement"/>
    /// values compares equal to a live model holding CLR values of the same shape. Reported
    /// values are truncated for display; callers must not parse them back.
    /// <para>
    /// Outputs are excluded for the same reason, for cell types in <paramref name="cellTypes"/>
    /// that declare <see cref="ICellType.PersistsOutputs"/> as <c>false</c>. Those outputs are
    /// derived from the source and never written to disk, but hosts render them when a notebook
    /// opens, so a baseline read from a file has none where the live notebook has one. Comparing
    /// them would report every such cell as modified on an untouched notebook. When no registry
    /// is supplied all outputs take part, which is the safe reading for a caller that cannot say
    /// which types are transient.
    /// </para>
    /// </remarks>
    public static NotebookDiffResult Compute(
        NotebookModel baseline,
        NotebookModel current,
        string baselineLabel,
        IReadOnlyList<ICellType>? cellTypes)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        var cells = CellAligner.Align(baseline.Cells, current.Cells, TransientOutputTypes(cellTypes));
        var summary = new NotebookDiffSummary();
        foreach (var entry in cells)
        {
            switch (entry.Kind)
            {
                case CellDiffKind.Added: summary.Added++; break;
                case CellDiffKind.Removed: summary.Removed++; break;
                case CellDiffKind.Modified: summary.Modified++; break;
                case CellDiffKind.Moved: summary.Moved++; break;
                default: summary.Unchanged++; break;
            }
        }

        return new NotebookDiffResult
        {
            BaselineLabel = baselineLabel,
            Cells = cells,
            MetadataChanges = DiffMetadata(baseline, current),
            Summary = summary,
        };
    }

    /// <summary>
    /// Collects the ids of cell types whose outputs are transient. Matching is ordinal-ignore-case
    /// to agree with how cell types are resolved everywhere else.
    /// </summary>
    private static IReadOnlySet<string> TransientOutputTypes(IReadOnlyList<ICellType>? cellTypes)
        => cellTypes is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(
                cellTypes.Where(t => !t.PersistsOutputs).Select(t => t.CellTypeId),
                StringComparer.OrdinalIgnoreCase);

    private static List<MetadataChange> DiffMetadata(NotebookModel baseline, NotebookModel current)
    {
        var changes = new List<MetadataChange>();
        AddIfChanged(changes, "Title", baseline.Title, current.Title);
        AddIfChanged(changes, "Default kernel", baseline.DefaultKernelId, current.DefaultKernelId);
        AddLayoutChangeIfMeaningful(changes, baseline.ActiveLayout, current.ActiveLayout);
        AddIfChanged(changes, "Preferred theme", baseline.PreferredThemeId, current.PreferredThemeId);
        AddIfChanged(changes, "Required extensions", JoinSorted(baseline.RequiredExtensions), JoinSorted(current.RequiredExtensions));
        AddIfChanged(changes, "Optional extensions", JoinSorted(baseline.OptionalExtensions), JoinSorted(current.OptionalExtensions));
        AddEntryChanges(changes, "Parameter", ToCanonicalMap(baseline.Parameters), ToCanonicalMap(current.Parameters));
        AddExtensionSettingChanges(changes, baseline.ExtensionSettings, current.ExtensionSettings);
        AddLayoutStateChanges(changes, baseline.Layouts, current.Layouts);
        return changes;
    }

    private static void AddExtensionSettingChanges(
        List<MetadataChange> changes,
        Dictionary<string, Dictionary<string, object?>> baseline,
        Dictionary<string, Dictionary<string, object?>> current)
    {
        var extensionIds = baseline.Keys.Union(current.Keys, StringComparer.Ordinal);
        foreach (var extensionId in extensionIds.OrderBy(k => k, StringComparer.Ordinal))
        {
            baseline.TryGetValue(extensionId, out var baselineSettings);
            current.TryGetValue(extensionId, out var currentSettings);
            AddEntryChanges(
                changes, $"Extension setting '{extensionId}'",
                ToCanonicalMap(baselineSettings), ToCanonicalMap(currentSettings),
                key => $"Extension setting '{extensionId}.{key}'");
        }
    }

    private static void AddLayoutStateChanges(
        List<MetadataChange> changes,
        Dictionary<string, object> baseline,
        Dictionary<string, object> current)
    {
        var baselineMap = ToCanonicalMap(baseline);
        var currentMap = ToCanonicalMap(current);
        PromoteBareLayoutKeys(baselineMap, currentMap);
        PromoteBareLayoutKeys(currentMap, baselineMap);
        AddEntryChanges(changes, "Layout state", baselineMap, currentMap);
    }

    /// <summary>
    /// Renames a bare layout-state key to its qualified counterpart on the other side. Saving
    /// promotes legacy bare ids to extension:layout form, so a baseline from an older file
    /// stores the same state under a different key; comparing keys literally would report that
    /// promotion as a removal plus an addition on every old notebook.
    /// </summary>
    private static void PromoteBareLayoutKeys(Dictionary<string, string?> map, Dictionary<string, string?> other)
    {
        foreach (var bareKey in map.Keys.Where(k => !k.Contains(':')).ToList())
        {
            if (other.ContainsKey(bareKey)) continue;

            var qualified = other.Keys.Where(k =>
            {
                var separator = k.IndexOf(':');
                return separator >= 0
                    && string.Equals(k[(separator + 1)..], bareKey, StringComparison.OrdinalIgnoreCase);
            }).ToList();
            if (qualified.Count != 1 || map.ContainsKey(qualified[0])) continue;

            map[qualified[0]] = map[bareKey];
            map.Remove(bareKey);
        }
    }

    private static void AddEntryChanges(
        List<MetadataChange> changes,
        string fieldPrefix,
        Dictionary<string, string?> baseline,
        Dictionary<string, string?> current,
        Func<string, string>? fieldName = null)
    {
        var keys = baseline.Keys.Union(current.Keys, StringComparer.Ordinal);
        foreach (var key in keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            baseline.TryGetValue(key, out var baselineValue);
            current.TryGetValue(key, out var currentValue);
            if (string.Equals(baselineValue, currentValue, StringComparison.Ordinal)) continue;

            changes.Add(new MetadataChange
            {
                Field = fieldName?.Invoke(key) ?? $"{fieldPrefix} '{key}'",
                BaselineValue = TruncateForDisplay(baselineValue),
                CurrentValue = TruncateForDisplay(currentValue),
            });
        }
    }

    private static Dictionary<string, string?> ToCanonicalMap<TValue>(IReadOnlyDictionary<string, TValue>? source)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (source is null) return map;
        foreach (var (key, value) in source)
        {
            map[key] = Canonicalize(value);
        }
        return map;
    }

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Serializes a value to JSON with object properties recursively sorted, so equality is
    /// insensitive to key order and to whether a side holds <see cref="JsonElement"/> (a
    /// deserialized baseline) or CLR objects (the live model) for the same data.
    /// </summary>
    private static string? Canonicalize<TValue>(TValue value)
    {
        if (value is null) return null;
        var node = JsonSerializer.SerializeToNode(value, CanonicalJsonOptions);
        return SortNode(node)?.ToJsonString();
    }

    private static JsonNode? SortNode(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, SortNode(p.Value)))),
        JsonArray arr => new JsonArray(arr.Select(SortNode).ToArray()),
        null => null,
        _ => node.DeepClone(),
    };

    /// <summary>
    /// Caps a reported value's length. Layout state in particular can be arbitrarily large
    /// (image data, full layer documents); the settings panel needs a recognizable preview,
    /// not the payload, and the full value would otherwise travel over RPC with every diff.
    /// </summary>
    private const int MaxDisplayValueLength = 160;

    private static string? TruncateForDisplay(string? value)
        => value is { Length: > MaxDisplayValueLength }
            ? value[..MaxDisplayValueLength] + "..."
            : value;

    /// <summary>
    /// Compares active layouts by layout id when either side is unqualified. A baseline parsed
    /// straight from an older file keeps its bare-string layout reference (it is never wired to
    /// an extension host that could qualify it), while the live notebook's reference has been
    /// resolved to extension:layout form; comparing qualified strings would report that
    /// migration artifact as a change on every old notebook.
    /// </summary>
    private static void AddLayoutChangeIfMeaningful(List<MetadataChange> changes, LayoutReference? baseline, LayoutReference? current)
    {
        var equal = (baseline, current) switch
        {
            (null, null) => true,
            ({ } b, { } c) when b.IsUnqualified || c.IsUnqualified
                => string.Equals(b.LayoutId, c.LayoutId, StringComparison.OrdinalIgnoreCase),
            ({ } b, { } c) => string.Equals(b.Qualified, c.Qualified, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

        if (!equal)
        {
            changes.Add(new MetadataChange
            {
                Field = "Active layout",
                BaselineValue = FormatLayout(baseline),
                CurrentValue = FormatLayout(current),
            });
        }
    }

    private static string? FormatLayout(LayoutReference? reference)
        => reference is { } r ? (r.IsUnqualified ? r.LayoutId : r.Qualified) : null;

    private static void AddIfChanged(List<MetadataChange> changes, string field, string? baselineValue, string? currentValue)
    {
        if (!string.Equals(baselineValue, currentValue, StringComparison.Ordinal))
        {
            changes.Add(new MetadataChange { Field = field, BaselineValue = baselineValue, CurrentValue = currentValue });
        }
    }

    private static string? JoinSorted(List<string> values)
        => values.Count == 0 ? null : string.Join(", ", values.OrderBy(v => v, StringComparer.Ordinal));
}
