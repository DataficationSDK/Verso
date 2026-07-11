using Verso.Abstractions;

namespace Verso.Diffing;

/// <summary>
/// Computes a cell-level structural diff between two notebook versions.
/// </summary>
public static class NotebookDiffEngine
{
    /// <summary>
    /// Compares <paramref name="baseline"/> against <paramref name="current"/> and returns the
    /// aligned cell entries, notebook-level metadata changes, and summary counts.
    /// </summary>
    /// <remarks>
    /// The modified timestamp and format version are intentionally excluded from the metadata
    /// comparison: both change on every save and would report noise rather than meaningful edits.
    /// Parameter definitions and per-extension settings are not compared yet.
    /// </remarks>
    public static NotebookDiffResult Compute(NotebookModel baseline, NotebookModel current, string baselineLabel)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        var cells = CellAligner.Align(baseline.Cells, current.Cells);
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

    private static List<MetadataChange> DiffMetadata(NotebookModel baseline, NotebookModel current)
    {
        var changes = new List<MetadataChange>();
        AddIfChanged(changes, "Title", baseline.Title, current.Title);
        AddIfChanged(changes, "Default kernel", baseline.DefaultKernelId, current.DefaultKernelId);
        AddLayoutChangeIfMeaningful(changes, baseline.ActiveLayout, current.ActiveLayout);
        AddIfChanged(changes, "Preferred theme", baseline.PreferredThemeId, current.PreferredThemeId);
        AddIfChanged(changes, "Required extensions", JoinSorted(baseline.RequiredExtensions), JoinSorted(current.RequiredExtensions));
        AddIfChanged(changes, "Optional extensions", JoinSorted(baseline.OptionalExtensions), JoinSorted(current.OptionalExtensions));
        return changes;
    }

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
