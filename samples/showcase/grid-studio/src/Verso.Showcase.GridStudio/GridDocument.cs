using System.Text.Json;

namespace Verso.Showcase.GridStudio;

/// <summary>
/// The layout's persisted document: which kernel variable the grid is bound to. The grid's
/// contents are not stored here — they live in the bound <c>DataBlock</c> in the kernel's
/// variable store, which is the notebook's real source of truth. Persisting only the binding
/// keeps the layout reproducible: reopen the notebook, re-run the cell that builds the
/// DataBlock, and the grid rebinds to it.
/// </summary>
internal sealed class GridDocument
{
    /// <summary>Name of the kernel variable holding the bound <c>DataBlock</c>.</summary>
    public string SourceVar { get; set; } = "data";

    public Dictionary<string, object> ToMetadata() => new(StringComparer.Ordinal)
    {
        ["sourceVar"] = SourceVar,
    };

    public static GridDocument FromMetadata(Dictionary<string, object> metadata)
    {
        var document = new GridDocument();
        if (metadata.TryGetValue("sourceVar", out var value) && AsString(value) is { } sourceVar
            && !string.IsNullOrWhiteSpace(sourceVar))
        {
            document.SourceVar = sourceVar.Trim();
        }
        return document;
    }

    // Metadata round-trips through JSON, so a value may arrive as a string (in-memory) or a
    // JsonElement (rehydrated from the notebook file). Accept both.
    private static string? AsString(object? value) => value switch
    {
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        _ => value?.ToString(),
    };
}
