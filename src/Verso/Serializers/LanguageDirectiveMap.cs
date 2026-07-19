namespace Verso.Serializers;

/// <summary>
/// Shared alias table mapping a language token to a cell (Type, Language) pair. The tokens match
/// the Polyglot Notebook <c>#!</c> directive names, and the same set is recognized in the info
/// string of a fenced code block when importing Markdown notebooks.
/// </summary>
public static class LanguageDirectiveMap
{
    /// <summary>
    /// Maps a language token (case-insensitive) to the cell type and kernel language it produces.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Type, string? Language)> Aliases =
        new Dictionary<string, (string Type, string? Language)>(StringComparer.OrdinalIgnoreCase)
        {
            ["markdown"] = ("markdown", null),
            ["csharp"] = ("code", "csharp"),
            ["cs"] = ("code", "csharp"),
            ["c#"] = ("code", "csharp"),
            ["fsharp"] = ("code", "fsharp"),
            ["fs"] = ("code", "fsharp"),
            ["f#"] = ("code", "fsharp"),
            ["pwsh"] = ("code", "powershell"),
            ["powershell"] = ("code", "powershell"),
            ["python"] = ("code", "python"),
            ["py"] = ("code", "python"),
            ["javascript"] = ("code", "javascript"),
            ["js"] = ("code", "javascript"),
            ["typescript"] = ("code", "typescript"),
            ["ts"] = ("code", "typescript"),
            ["html"] = ("html", null),
            ["mermaid"] = ("mermaid", null),
            ["sql"] = ("sql", null),
            ["value"] = ("code", "value"),
        };

    /// <summary>
    /// Canonical fence tag for a cell's type and language, used when a cell has no remembered
    /// opening fence line to reuse (for example a newly created cell). Falls back to the language,
    /// then the type, verbatim.
    /// </summary>
    public static string ToFenceTag(string type, string? language) =>
        (type.ToLowerInvariant(), language?.ToLowerInvariant()) switch
        {
            ("code", "csharp") => "csharp",
            ("code", "fsharp") => "fsharp",
            ("code", "powershell") => "powershell",
            ("code", "python") => "python",
            ("code", "javascript") => "javascript",
            ("code", "typescript") => "typescript",
            ("html", _) => "html",
            ("mermaid", _) => "mermaid",
            ("sql", _) => "sql",
            _ => language ?? type,
        };
}
