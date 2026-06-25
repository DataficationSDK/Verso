namespace Verso.Abstractions;

/// <summary>
/// Canonical version values and parsing for the <c>.verso</c> document schema. The format version
/// is independent of the product/assembly version and is bumped only when the on-disk schema
/// changes.
/// </summary>
public static class NotebookFormatVersion
{
    /// <summary>
    /// The format version this build reads and writes. Stamped onto every saved <c>.verso</c>
    /// document, and the version a freshly constructed <see cref="NotebookModel"/> carries.
    /// </summary>
    public const string Current = "1.1";

    /// <summary>
    /// The version assumed for a document that carries no version field. Such files predate the
    /// version stamp and are treated as the earliest known format so the migration chain runs.
    /// </summary>
    public const string Initial = "1.0";

    /// <summary>
    /// Parses a format version string such as <c>"1.1"</c> into a comparable <see cref="Version"/>.
    /// Returns <see langword="false"/> for null, blank, or malformed input.
    /// </summary>
    public static bool TryParse(string? value, out Version version)
    {
        if (!string.IsNullOrWhiteSpace(value) && Version.TryParse(value, out var parsed))
        {
            version = parsed;
            return true;
        }

        version = new Version(0, 0);
        return false;
    }
}
