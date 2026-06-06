namespace Verso.Blazor.Services;

/// <summary>
/// Host-level configuration for <see cref="ServerNotebookService"/>. Populated
/// by the hosting layer (CLI <c>verso serve</c> or the standalone Blazor app)
/// and injected as a singleton.
/// </summary>
public sealed record NotebookServiceOptions
{
    /// <summary>
    /// Optional single directory scanned for additional extension assemblies after
    /// built-in extensions are loaded. Retained for callers that configure one path;
    /// new callers should prefer <see cref="ExtensionsDirectories"/>.
    /// </summary>
    public string? ExtensionsDirectory { get; init; }

    /// <summary>
    /// Optional list of directories scanned for additional extension assemblies after
    /// built-in extensions are loaded. Combined with <see cref="ExtensionsDirectory"/>
    /// via <see cref="GetAllExtensionsDirectories"/>.
    /// </summary>
    public IReadOnlyList<string>? ExtensionsDirectories { get; init; }

    /// <summary>
    /// When true, saving a notebook that was loaded from a non-.verso file (e.g.
    /// <c>.ipynb</c>) writes back to the original format instead of converting
    /// to <c>.verso</c>. Defaults to false.
    /// </summary>
    public bool PreserveFormat { get; init; }

    /// <summary>
    /// Returns the combined, de-duplicated list of configured extension directories,
    /// merging the single <see cref="ExtensionsDirectory"/> with
    /// <see cref="ExtensionsDirectories"/>.
    /// </summary>
    public IReadOnlyList<string> GetAllExtensionsDirectories()
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            var trimmed = dir.Trim();
            if (seen.Add(trimmed))
                result.Add(trimmed);
        }

        Add(ExtensionsDirectory);
        if (ExtensionsDirectories is not null)
        {
            foreach (var dir in ExtensionsDirectories)
                Add(dir);
        }

        return result;
    }
}
