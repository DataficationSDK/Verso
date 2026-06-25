namespace Verso.Extensions.Marketplace;

/// <summary>
/// Where a required extension came from, which determines how it is reloaded when a
/// notebook is opened. NuGet packages can be re-downloaded from a feed if missing from
/// the managed directory; local files (sideloaded <c>.dll</c>/<c>.nupkg</c>) can only be
/// loaded from the managed directory they were installed into.
/// </summary>
public enum ExtensionSource
{
    /// <summary>A package resolvable from a NuGet feed.</summary>
    NuGet,

    /// <summary>A file sideloaded from local disk; reloadable only from the managed directory.</summary>
    Local,
}

/// <summary>
/// Parses and formats the reference strings stored in a notebook's
/// <see cref="Verso.Abstractions.NotebookModel.RequiredExtensions"/> list.
/// A bare <c>PackageId</c> resolves to the latest available version, so older entries
/// authored without a version still load. A <c>local:</c> prefix marks an extension that
/// was sideloaded from disk; such entries reload from the managed directory only and are
/// never fetched from a NuGet feed.
/// </summary>
public static class ExtensionPackageRef
{
    private const string LocalPrefix = "local:";

    /// <summary>
    /// Splits a reference into its package id, optional version, and source. NuGet package
    /// ids never contain an <c>@</c>, so the first <c>@</c> separates id from version. A
    /// leading <c>local:</c> scheme (case-insensitive) marks the entry as a local sideload.
    /// </summary>
    public static (string Id, string? Version, ExtensionSource Source) Parse(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return (string.Empty, null, ExtensionSource.NuGet);

        var trimmed = reference.Trim();
        var source = ExtensionSource.NuGet;
        if (trimmed.StartsWith(LocalPrefix, StringComparison.OrdinalIgnoreCase))
        {
            source = ExtensionSource.Local;
            trimmed = trimmed[LocalPrefix.Length..].Trim();
        }

        var at = trimmed.IndexOf('@');
        if (at < 0)
            return (trimmed, null, source);

        var id = trimmed[..at].Trim();
        var version = trimmed[(at + 1)..].Trim();
        return (id, string.IsNullOrEmpty(version) ? null : version, source);
    }

    /// <summary>Returns just the package id portion of a reference, scheme and version stripped.</summary>
    public static string ParseId(string reference) => Parse(reference).Id;

    /// <summary>Formats a package id, optional version, and source into a reference string.</summary>
    public static string Format(string id, string? version, ExtensionSource source = ExtensionSource.NuGet)
    {
        var body = string.IsNullOrWhiteSpace(version) ? id : $"{id}@{version}";
        return source == ExtensionSource.Local ? LocalPrefix + body : body;
    }
}
