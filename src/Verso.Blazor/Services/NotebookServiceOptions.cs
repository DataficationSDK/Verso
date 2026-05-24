namespace Verso.Blazor.Services;

/// <summary>
/// Host-level configuration for <see cref="ServerNotebookService"/>. Populated
/// by the hosting layer (CLI <c>verso serve</c> or the standalone Blazor app)
/// and injected as a singleton.
/// </summary>
public sealed record NotebookServiceOptions
{
    /// <summary>
    /// Optional directory scanned for additional extension assemblies after
    /// built-in extensions are loaded. When null or missing on disk, only the
    /// built-in extensions are loaded.
    /// </summary>
    public string? ExtensionsDirectory { get; init; }

    /// <summary>
    /// When true, saving a notebook that was loaded from a non-.verso file (e.g.
    /// <c>.ipynb</c>) writes back to the original format instead of converting
    /// to <c>.verso</c>. Defaults to false.
    /// </summary>
    public bool PreserveFormat { get; init; }
}
