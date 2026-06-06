using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using Verso.Kernels;

namespace Verso.Extensions.Marketplace;

/// <summary>
/// A single package returned by a marketplace search.
/// </summary>
public sealed record MarketplaceSearchItem(
    string Id,
    string? Version,
    string? Description,
    string? Authors,
    long? DownloadCount,
    string? IconUrl,
    string? ProjectUrl);

/// <summary>
/// The result of ensuring a package is installed on disk: the concrete resolved version
/// and the managed-directory assembly paths to load.
/// </summary>
public sealed record MarketplaceInstallResult(
    string ResolvedVersion,
    string PackageDirectory,
    IReadOnlyList<string> AssemblyPaths);

/// <summary>
/// Searches NuGet for extension packages and installs them into the managed extensions
/// directory. Search uses the NuGet v3 <see cref="PackageSearchResource"/>; install
/// delegates downloading and transitive resolution to <see cref="NuGetPackageResolver"/>
/// and then copies the resolved assemblies into a stable per-package directory so they
/// persist across sessions and can be reloaded offline.
/// </summary>
public sealed class NuGetMarketplaceService
{
    private readonly List<SourceRepository> _sources;

    public NuGetMarketplaceService()
    {
        _sources = new List<SourceRepository>();

        // Mirror NuGetPackageResolver's source loading so search and install agree on
        // which feeds are in play. Additional, user-configured feeds can be layered in
        // later; for now the standard NuGet.Config chain (with the nuget.org fallback)
        // is used.
        try
        {
            var settings = Settings.LoadDefaultSettings(root: Directory.GetCurrentDirectory());
            var provider = new PackageSourceProvider(settings);
            foreach (var source in provider.LoadPackageSources().Where(s => s.IsEnabled))
                _sources.Add(Repository.Factory.GetCoreV3(source));
        }
        catch
        {
            // Config loading must never prevent search.
        }

        if (_sources.Count == 0)
            _sources.Add(Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json"));
    }

    /// <summary>
    /// Searches the configured sources for packages matching <paramref name="query"/>.
    /// The first source that exposes a search resource is used (local-directory feeds do
    /// not support search; the nuget.org fallback always does).
    /// </summary>
    public async Task<IReadOnlyList<MarketplaceSearchItem>> SearchAsync(
        string query, int skip, int take, bool includePrerelease, CancellationToken ct)
    {
        if (take <= 0) take = 20;
        if (skip < 0) skip = 0;

        var filter = new SearchFilter(includePrerelease);

        foreach (var source in _sources)
        {
            PackageSearchResource? resource;
            try
            {
                resource = await source.GetResourceAsync<PackageSearchResource>(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                continue; // Source has no search endpoint; try the next.
            }

            if (resource is null)
                continue;

            try
            {
                var results = await resource
                    .SearchAsync(query ?? string.Empty, filter, skip, take, NullLogger.Instance, ct)
                    .ConfigureAwait(false);

                return results.Select(r => new MarketplaceSearchItem(
                    r.Identity.Id,
                    r.Identity.Version?.ToNormalizedString(),
                    r.Description,
                    r.Authors,
                    r.DownloadCount,
                    r.IconUrl?.ToString(),
                    r.ProjectUrl?.ToString())).ToList();
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Search failed on this source; fall through to the next.
            }
        }

        return Array.Empty<MarketplaceSearchItem>();
    }

    /// <summary>
    /// Ensures a package is present in the managed extensions directory and returns the
    /// assembly paths to load. When the requested version is already installed on disk this
    /// is offline and fast; otherwise the package and its transitive dependencies are
    /// downloaded, then copied into <c>{managedDir}/{id}/{version}/</c>. The package
    /// directory is registered with the runtime resolver so co-located dependency
    /// assemblies resolve when the extension is loaded.
    /// </summary>
    public async Task<MarketplaceInstallResult> EnsureInstalledAsync(
        string packageId, string? version, string managedDir, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(managedDir);

        // Fast path: a pinned version already laid down on disk.
        if (!string.IsNullOrWhiteSpace(version))
        {
            var existingDir = Path.Combine(managedDir, packageId, version);
            if (Directory.Exists(existingDir))
            {
                var existingDlls = Directory.GetFiles(existingDir, "*.dll");
                if (existingDlls.Length > 0)
                {
                    NuGetRuntimeResolver.AddManagedSearchDirectory(existingDir);
                    return new MarketplaceInstallResult(version, existingDir, existingDlls);
                }
            }
        }

        var resolver = new NuGetPackageResolver();
        var result = await resolver.ResolvePackageAsync(packageId, version, ct).ConfigureAwait(false);

        var targetDir = Path.Combine(managedDir, packageId, result.ResolvedVersion);
        Directory.CreateDirectory(targetDir);

        var copied = new List<string>();
        foreach (var source in result.AssemblyPaths)
        {
            try
            {
                var dest = Path.Combine(targetDir, Path.GetFileName(source));
                File.Copy(source, dest, overwrite: true);
                copied.Add(dest);
            }
            catch
            {
                // Skip files that can't be copied; the loader will report missing extensions.
            }
        }

        NuGetRuntimeResolver.AddManagedSearchDirectory(targetDir);
        return new MarketplaceInstallResult(result.ResolvedVersion, targetDir, copied);
    }
}
