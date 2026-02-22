namespace Verso.Abstractions;

/// <summary>
/// Describes a NuGet-based extension package that requires user consent before loading.
/// </summary>
/// <param name="PackageId">The NuGet package ID.</param>
/// <param name="Version">Optional requested version (null for latest).</param>
/// <param name="Source">Where the directive originated, e.g. "cell" or "imported from foo.verso".</param>
public sealed record ExtensionConsentInfo(
    string PackageId, string? Version, string Source = "cell");
