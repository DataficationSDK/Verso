using System.Text.Json.Serialization;

namespace Verso.Abstractions;

/// <summary>
/// Classifies how a cell differs between a baseline notebook and the current notebook.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CellDiffKind
{
    /// <summary>The cell is present on both sides with identical content and position.</summary>
    Unchanged,

    /// <summary>The cell exists only in the current notebook.</summary>
    Added,

    /// <summary>The cell exists only in the baseline notebook.</summary>
    Removed,

    /// <summary>The cell is present on both sides but its content differs.</summary>
    Modified,

    /// <summary>The cell is present on both sides with identical content but a different position.</summary>
    Moved,
}

/// <summary>
/// The result of comparing a baseline notebook against the current notebook:
/// an ordered list of per-cell diff entries plus notebook-level metadata changes.
/// </summary>
public sealed class NotebookDiffResult
{
    /// <summary>
    /// Gets or sets a human-readable label describing the baseline (e.g. "Git: HEAD", "Last Saved").
    /// </summary>
    public string BaselineLabel { get; set; } = "";

    /// <summary>
    /// Gets or sets the per-cell diff entries, ordered to follow the current notebook,
    /// with removed cells spliced in near their former neighbors.
    /// </summary>
    public List<CellDiffEntry> Cells { get; set; } = new();

    /// <summary>
    /// Gets or sets the notebook-level metadata changes (title, default kernel, layout, theme, extensions).
    /// </summary>
    public List<MetadataChange> MetadataChanges { get; set; } = new();

    /// <summary>
    /// Gets or sets the tallied counts of each diff kind.
    /// </summary>
    public NotebookDiffSummary Summary { get; set; } = new();
}

/// <summary>
/// A single cell's participation in a notebook diff.
/// </summary>
/// <remarks>
/// <see cref="BaselineCell"/> and <see cref="CurrentCell"/> carry full <see cref="CellModel"/>
/// instances so consumers can render sources and outputs directly. When a diff result crosses a
/// JSON-RPC boundary, <see cref="CellModel.Metadata"/> values arrive as raw JSON elements rather
/// than the primitive types produced by notebook deserialization; consumers should rely on the
/// precomputed change flags on this entry instead of reading into cell metadata values.
/// </remarks>
public sealed class CellDiffEntry
{
    /// <summary>
    /// Gets or sets how this cell differs between the two sides.
    /// </summary>
    public CellDiffKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the cell's index in the baseline notebook, or <see langword="null"/> when <see cref="Kind"/> is <see cref="CellDiffKind.Added"/>.
    /// </summary>
    public int? BaselineIndex { get; set; }

    /// <summary>
    /// Gets or sets the cell's index in the current notebook, or <see langword="null"/> when <see cref="Kind"/> is <see cref="CellDiffKind.Removed"/>.
    /// </summary>
    public int? CurrentIndex { get; set; }

    /// <summary>
    /// Gets or sets the baseline side of the cell, or <see langword="null"/> when <see cref="Kind"/> is <see cref="CellDiffKind.Added"/>.
    /// </summary>
    public CellModel? BaselineCell { get; set; }

    /// <summary>
    /// Gets or sets the current side of the cell, or <see langword="null"/> when <see cref="Kind"/> is <see cref="CellDiffKind.Removed"/>.
    /// </summary>
    public CellModel? CurrentCell { get; set; }

    /// <summary>
    /// True when the pairing came from content-based alignment rather than a matching cell id.
    /// Always true for baselines from formats without stable cell ids (e.g. Jupyter imports).
    /// </summary>
    public bool MatchedByContent { get; set; }

    /// <summary>True when the cell source text differs between the two sides.</summary>
    public bool SourceChanged { get; set; }

    /// <summary>True when the cell outputs differ between the two sides.</summary>
    public bool OutputsChanged { get; set; }

    /// <summary>True when the cell type or language differs between the two sides.</summary>
    public bool TypeOrLanguageChanged { get; set; }

    /// <summary>True when the cell metadata differs between the two sides.</summary>
    public bool CellMetadataChanged { get; set; }
}

/// <summary>
/// A single notebook-level metadata difference, with both values pre-rendered as display strings.
/// </summary>
public sealed class MetadataChange
{
    /// <summary>
    /// Gets or sets the metadata field name (e.g. "Title", "Default kernel").
    /// </summary>
    public string Field { get; set; } = "";

    /// <summary>
    /// Gets or sets the baseline value as a display string, or <see langword="null"/> when unset on the baseline side.
    /// </summary>
    public string? BaselineValue { get; set; }

    /// <summary>
    /// Gets or sets the current value as a display string, or <see langword="null"/> when unset on the current side.
    /// </summary>
    public string? CurrentValue { get; set; }
}

/// <summary>
/// Tallied counts of each <see cref="CellDiffKind"/> in a diff result.
/// </summary>
public sealed class NotebookDiffSummary
{
    /// <summary>Gets or sets the number of added cells.</summary>
    public int Added { get; set; }

    /// <summary>Gets or sets the number of removed cells.</summary>
    public int Removed { get; set; }

    /// <summary>Gets or sets the number of modified cells.</summary>
    public int Modified { get; set; }

    /// <summary>Gets or sets the number of moved cells.</summary>
    public int Moved { get; set; }

    /// <summary>Gets or sets the number of unchanged cells.</summary>
    public int Unchanged { get; set; }
}
