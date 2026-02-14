using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Verso.Kernels;

/// <summary>
/// Downloads NuGet packages and extracts DLLs from the appropriate TFM folder.
/// Caches packages in a temp directory.
/// </summary>
internal sealed class NuGetPackageResolver
{
    private static readonly string CacheRoot =
        Path.Combine(Path.GetTempPath(), "verso-nuget-packages");

    /// <summary>
    /// Parses a NuGet reference string in the format "PackageId, Version" or "PackageId".
    /// </summary>
    public static (string PackageId, string? Version)? ParseNuGetReference(string? directive)
    {
        if (string.IsNullOrWhiteSpace(directive))
            return null;

        var text = directive.Trim();
        var commaIndex = text.IndexOf(',');

        if (commaIndex >= 0)
        {
            var packageId = text.Substring(0, commaIndex).Trim();
            var version = text.Substring(commaIndex + 1).Trim();
            if (string.IsNullOrEmpty(packageId))
                return null;
            return (packageId, string.IsNullOrEmpty(version) ? null : version);
        }

        var spaceIndex = text.IndexOf(' ');
        if (spaceIndex >= 0)
        {
            var packageId = text.Substring(0, spaceIndex).Trim();
            var version = text.Substring(spaceIndex + 1).Trim();
            if (string.IsNullOrEmpty(packageId))
                return null;
            return (packageId, string.IsNullOrEmpty(version) ? null : version);
        }

        return (text, null);
    }

    /// <summary>
    /// Resolves a NuGet package, downloading it if not cached, and returns paths to the extracted DLLs.
    /// </summary>
    public async Task<IReadOnlyList<string>> ResolvePackageAsync(
        string packageId, string? version, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(packageId);

        var repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
        var resource = await repository.GetResourceAsync<FindPackageByIdResource>(ct).ConfigureAwait(false);

        var logger = NullLogger.Instance;
        var cache = new SourceCacheContext();

        // Resolve version if not specified
        NuGetVersion resolvedVersion;
        if (version is not null && NuGetVersion.TryParse(version, out var parsed))
        {
            resolvedVersion = parsed;
        }
        else
        {
            var versions = await resource.GetAllVersionsAsync(packageId, cache, logger, ct).ConfigureAwait(false);
            resolvedVersion = versions
                .Where(v => !v.IsPrerelease)
                .OrderByDescending(v => v)
                .FirstOrDefault()
                ?? throw new InvalidOperationException($"No versions found for package '{packageId}'.");
        }

        var packageDir = Path.Combine(CacheRoot, packageId, resolvedVersion.ToString());

        // Check cache
        if (Directory.Exists(packageDir))
        {
            var cachedDlls = Directory.GetFiles(packageDir, "*.dll");
            if (cachedDlls.Length > 0)
                return cachedDlls;
        }

        Directory.CreateDirectory(packageDir);

        // Download
        var tempNupkg = Path.Combine(packageDir, $"{packageId}.{resolvedVersion}.nupkg");
        using (var fileStream = File.Create(tempNupkg))
        {
            var downloaded = await resource.CopyNupkgToStreamAsync(
                packageId, resolvedVersion, fileStream, cache, logger, ct).ConfigureAwait(false);

            if (!downloaded)
                throw new InvalidOperationException($"Failed to download package '{packageId}' v{resolvedVersion}.");
        }

        // Extract DLLs from appropriate TFM
        var assemblyPaths = new List<string>();
        using var reader = new PackageArchiveReader(tempNupkg);

        var libItems = (await reader.GetLibItemsAsync(ct).ConfigureAwait(false)).ToList();

        // Prefer net8.0, then net6.0, then netstandard2.1, then netstandard2.0
        var preferredFrameworks = new[] { "net8.0", "net6.0", "netstandard2.1", "netstandard2.0" };
        FrameworkSpecificGroup? selectedGroup = null;

        foreach (var preferred in preferredFrameworks)
        {
            selectedGroup = libItems.FirstOrDefault(g =>
                g.TargetFramework.GetShortFolderName()
                    .Equals(preferred, StringComparison.OrdinalIgnoreCase));
            if (selectedGroup is not null)
                break;
        }

        // Fall back to first group with any DLLs
        selectedGroup ??= libItems.FirstOrDefault(g => g.Items.Any(i => i.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)));

        if (selectedGroup is not null)
        {
            foreach (var item in selectedGroup.Items)
            {
                if (!item.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    continue;

                var entry = reader.GetEntry(item);
                if (entry is null) continue;

                var fileName = Path.GetFileName(item);
                var destPath = Path.Combine(packageDir, fileName);

                using var entryStream = entry.Open();
                using var destStream = File.Create(destPath);
                await entryStream.CopyToAsync(destStream, ct).ConfigureAwait(false);

                assemblyPaths.Add(destPath);
            }
        }

        // Clean up nupkg
        try { File.Delete(tempNupkg); } catch { /* best effort */ }

        return assemblyPaths;
    }
}
