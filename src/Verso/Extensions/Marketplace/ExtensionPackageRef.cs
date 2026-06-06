namespace Verso.Extensions.Marketplace;

/// <summary>
/// Parses and formats the <c>PackageId@version</c> reference strings stored in a
/// notebook's <see cref="Verso.Abstractions.NotebookModel.RequiredExtensions"/> list.
/// A plain <c>PackageId</c> with no <c>@version</c> suffix resolves to the latest
/// available version, so older entries authored without a version still load.
/// </summary>
public static class ExtensionPackageRef
{
    /// <summary>
    /// Splits a reference into its package id and optional version. NuGet package ids
    /// never contain an <c>@</c>, so the first <c>@</c> separates the two halves.
    /// </summary>
    public static (string Id, string? Version) Parse(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return (string.Empty, null);

        var trimmed = reference.Trim();
        var at = trimmed.IndexOf('@');
        if (at < 0)
            return (trimmed, null);

        var id = trimmed[..at].Trim();
        var version = trimmed[(at + 1)..].Trim();
        return (id, string.IsNullOrEmpty(version) ? null : version);
    }

    /// <summary>Returns just the package id portion of a reference.</summary>
    public static string ParseId(string reference) => Parse(reference).Id;

    /// <summary>Formats a package id and optional version into a reference string.</summary>
    public static string Format(string id, string? version) =>
        string.IsNullOrWhiteSpace(version) ? id : $"{id}@{version}";
}
