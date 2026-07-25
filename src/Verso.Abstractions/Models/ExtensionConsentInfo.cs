namespace Verso.Abstractions;

/// <summary>
/// What kind of thing a consent request covers, so a host can word its prompt for it.
/// </summary>
public enum ConsentKind
{
    /// <summary>An extension package that will be loaded into the notebook session.</summary>
    Extension,

    /// <summary>
    /// A language package that will be installed into an environment, such as a Python
    /// distribution a cell's import needs.
    /// </summary>
    Package,
}

/// <summary>
/// Describes something that requires user consent before it is loaded or installed.
/// </summary>
/// <param name="PackageId">The NuGet package ID, local assembly file name, or distribution name.</param>
/// <param name="Version">Optional requested version (null for latest or local files).</param>
/// <param name="Source">Where the request originated, e.g. "cell", "imported from foo.verso",
/// "session-generated local assembly", or "import cv2".</param>
public sealed record ExtensionConsentInfo(
    string PackageId,
    string? Version,
    string Source = "cell")
{
    // Set through an initializer rather than taking positional slots. A record's constructor and
    // Deconstruct are generated from its positional parameters, so adding one to a shipped type
    // removes the arity every existing caller was compiled against.

    /// <summary>What the request covers. Defaults to an extension package.</summary>
    public ConsentKind Kind { get; init; } = ConsentKind.Extension;

    /// <summary>
    /// Optional description of where the thing will go, such as the environment a distribution is
    /// installed into. Shown once for the whole request rather than per item.
    /// </summary>
    public string? Target { get; init; }
}
