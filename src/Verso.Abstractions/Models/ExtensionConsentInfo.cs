namespace Verso.Abstractions;

/// <summary>
/// Describes an extension that requires user consent before loading.
/// </summary>
/// <param name="PackageId">The NuGet package ID or local assembly file name.</param>
/// <param name="Version">Optional requested version (null for latest or local files).</param>
/// <param name="Source">Where the directive originated, e.g. "cell", "imported from foo.verso",
/// or "session-generated local assembly".</param>
public sealed record ExtensionConsentInfo(
    string PackageId, string? Version, string Source = "cell");
