namespace Verso.Abstractions;

/// <summary>
/// Describes a required extension that a notebook declared but could not be loaded on open,
/// so the hosting layer can surface a non-fatal notice rather than failing the open. The
/// notebook still opens; layouts or cells that depend on the extension may not work until
/// it becomes available.
/// </summary>
/// <param name="PackageId">The NuGet package id or local assembly file name.</param>
/// <param name="Version">The version that was attempted, or null when it could not be resolved.</param>
/// <param name="Reason">A short, user-facing explanation (e.g. "could not reach the package source").</param>
public sealed record UnavailableExtensionInfo(
    string PackageId, string? Version, string Reason);
