namespace Verso.Abstractions;

/// <summary>
/// Mutable model representing a single cell within a Verso notebook.
/// </summary>
public sealed class CellModel
{
    /// <summary>
    /// Gets or sets the unique identifier for this cell.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the cell type (e.g. "code", "markdown").
    /// </summary>
    public string Type { get; set; } = "code";

    /// <summary>
    /// Gets or sets the programming language identifier for the cell, or <see langword="null"/> if unspecified.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Gets or sets the source text content of the cell.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the list of outputs produced by executing this cell.
    /// </summary>
    public List<CellOutput> Outputs { get; set; } = new();

    /// <summary>
    /// Gets or sets arbitrary key-value metadata associated with this cell.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}
