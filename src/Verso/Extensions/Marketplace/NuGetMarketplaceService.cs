using System.Reflection;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
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
/// The result of installing a sideloaded local file. Carries the package id derived from
/// the file (assembly name for a loose <c>.dll</c>, package identity for a <c>.nupkg</c>)
/// in addition to the resolved version, managed directory, and assembly paths to load.
/// </summary>
public sealed record LocalInstallResult(
    string PackageId,
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
        var copied = CopyAssembliesToManaged(result.AssemblyPaths, targetDir);

        NuGetRuntimeResolver.AddManagedSearchDirectory(targetDir);
        return new MarketplaceInstallResult(result.ResolvedVersion, targetDir, copied);
    }

    /// <summary>
    /// Returns the assembly paths for a package already laid down in the managed directory,
    /// without contacting any feed. Used to reload sideloaded local extensions on open: a
    /// local file cannot be re-fetched, so a missing managed directory yields <c>null</c>
    /// rather than a download attempt. Returns <c>null</c> when the version is unknown or no
    /// assemblies are present.
    /// </summary>
    public MarketplaceInstallResult? TryResolveInstalled(string packageId, string? version, string managedDir)
    {
        if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(managedDir))
            return null;

        var dir = Path.Combine(managedDir, packageId, version);
        if (!Directory.Exists(dir))
            return null;

        var dlls = Directory.GetFiles(dir, "*.dll");
        if (dlls.Length == 0)
            return null;

        NuGetRuntimeResolver.AddManagedSearchDirectory(dir);
        return new MarketplaceInstallResult(version, dir, dlls);
    }

    /// <summary>
    /// Reads the package id and version a local file would install as, without loading any
    /// code: a <c>.nupkg</c> reports its package identity; a <c>.dll</c> reports its assembly
    /// name and version. Returns <c>null</c> for a missing or unsupported file. Used to drive
    /// the consent prompt before the assembly is loaded.
    /// </summary>
    public static (string Id, string? Version)? PeekLocalIdentity(string localFilePath)
    {
        if (string.IsNullOrWhiteSpace(localFilePath) || !File.Exists(localFilePath))
            return null;

        var ext = Path.GetExtension(localFilePath);
        try
        {
            if (string.Equals(ext, ".nupkg", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new PackageArchiveReader(localFilePath);
                var identity = reader.GetIdentity();
                return (identity.Id, identity.Version.ToNormalizedString());
            }

            if (string.Equals(ext, ".dll", StringComparison.OrdinalIgnoreCase))
            {
                var name = AssemblyName.GetAssemblyName(localFilePath);
                return (name.Name ?? Path.GetFileNameWithoutExtension(localFilePath), FormatAssemblyVersion(name.Version));
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// Installs a sideloaded local extension file into the managed directory. A loose
    /// <c>.dll</c> is copied into <c>{managedDir}/{assemblyName}/{assemblyVersion}/</c>; a
    /// <c>.nupkg</c> is resolved like any other NuGet package (its containing folder is added
    /// as a source so the package itself is found locally while transitive dependencies still
    /// resolve from configured feeds) and the resulting closure is copied into
    /// <c>{managedDir}/{id}/{version}/</c>. The package directory is registered with the
    /// runtime resolver so co-located dependencies resolve when the extension loads.
    /// </summary>
    public async Task<LocalInstallResult> InstallFromFileAsync(string localFilePath, string managedDir, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(managedDir);

        if (!File.Exists(localFilePath))
            throw new FileNotFoundException("Extension file not found.", localFilePath);

        var ext = Path.GetExtension(localFilePath);

        if (string.Equals(ext, ".nupkg", StringComparison.OrdinalIgnoreCase))
            return await InstallNupkgAsync(localFilePath, managedDir, ct).ConfigureAwait(false);

        if (string.Equals(ext, ".dll", StringComparison.OrdinalIgnoreCase))
            return InstallLooseDll(localFilePath, managedDir);

        throw new NotSupportedException(
            $"Unsupported extension file '{Path.GetFileName(localFilePath)}'. Choose a .dll or .nupkg.");
    }

    private static async Task<LocalInstallResult> InstallNupkgAsync(string nupkgPath, string managedDir, CancellationToken ct)
    {
        string id;
        string version;
        using (var reader = new PackageArchiveReader(nupkgPath))
        {
            var identity = reader.GetIdentity();
            id = identity.Id;
            version = identity.Version.ToNormalizedString();
        }

        var resolver = new NuGetPackageResolver();
        var folder = Path.GetDirectoryName(Path.GetFullPath(nupkgPath));
        if (!string.IsNullOrEmpty(folder))
            resolver.AddSource(folder);

        var result = await resolver.ResolvePackageAsync(id, version, ct).ConfigureAwait(false);

        var targetDir = Path.Combine(managedDir, id, result.ResolvedVersion);
        var copied = CopyAssembliesToManaged(result.AssemblyPaths, targetDir);

        NuGetRuntimeResolver.AddManagedSearchDirectory(targetDir);
        return new LocalInstallResult(id, result.ResolvedVersion, targetDir, copied);
    }

    private static LocalInstallResult InstallLooseDll(string dllPath, string managedDir)
    {
        var name = AssemblyName.GetAssemblyName(dllPath);
        var id = name.Name ?? Path.GetFileNameWithoutExtension(dllPath);
        var version = FormatAssemblyVersion(name.Version);

        var targetDir = Path.Combine(managedDir, id, version);
        Directory.CreateDirectory(targetDir);

        var dest = Path.Combine(targetDir, Path.GetFileName(dllPath));
        File.Copy(dllPath, dest, overwrite: true);

        NuGetRuntimeResolver.AddManagedSearchDirectory(targetDir);
        return new LocalInstallResult(id, version, targetDir, new[] { dest });
    }

    /// <summary>Copies resolved assemblies into a managed package directory, skipping any that fail.</summary>
    private static List<string> CopyAssembliesToManaged(IReadOnlyList<string> sources, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        var copied = new List<string>();
        foreach (var source in sources)
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

        return copied;
    }

    /// <summary>
    /// Formats an assembly version as a three-part NuGet-style string for the managed
    /// directory path. A null version (rare, but possible for malformed assemblies) maps to
    /// <c>0.0.0</c>.
    /// </summary>
    private static string FormatAssemblyVersion(Version? version)
    {
        if (version is null)
            return "0.0.0";

        var build = version.Build < 0 ? 0 : version.Build;
        return $"{version.Major}.{version.Minor}.{build}";
    }
}
