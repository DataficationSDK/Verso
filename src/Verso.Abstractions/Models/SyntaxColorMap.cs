namespace Verso.Abstractions;

/// <summary>
/// Mutable mapping from syntax token type names to their display colors, expressed as hex strings.
/// </summary>
public sealed class SyntaxColorMap
{
    private readonly Dictionary<string, string> _colors = new();

    /// <summary>
    /// Sets or overwrites the hex color for a given syntax token type.
    /// </summary>
    /// <param name="tokenType">The syntax token type name (e.g. "keyword", "comment").</param>
    /// <param name="hexColor">The hex color string to associate with the token type (e.g. "#FF5733").</param>
    public void Set(string tokenType, string hexColor) => _colors[tokenType] = hexColor;

    /// <summary>
    /// Gets the hex color for a given syntax token type, or <see langword="null"/> if not mapped.
    /// </summary>
    /// <param name="tokenType">The syntax token type name to look up.</param>
    /// <returns>The hex color string, or <see langword="null"/> if the token type has no mapping.</returns>
    public string? Get(string tokenType) => _colors.TryGetValue(tokenType, out var color) ? color : null;

    /// <summary>
    /// Returns a read-only view of all token-type-to-color mappings.
    /// </summary>
    /// <returns>A read-only dictionary of token type names to hex color strings.</returns>
    public IReadOnlyDictionary<string, string> GetAll() => _colors;

    /// <summary>
    /// Gets the number of token type mappings in this color map.
    /// </summary>
    public int Count => _colors.Count;
}
