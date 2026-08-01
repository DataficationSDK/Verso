using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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
/// persist across sessions and can be reloaded offline. Because the managed directory is
/// shared by every Verso host on the machine and hosts can run on different .NET runtime
/// majors (the notebook host rolls forward to the newest installed runtime), each install
/// is partitioned into a per-runtime subfolder so one host's assets never poison another's.
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
    /// The configured package sources, in the order they are searched. Surfaced so the
    /// extension panel can say where its results came from rather than assuming nuget.org,
    /// which is wrong for anyone with a private feed in their NuGet configuration.
    /// </summary>
    public IReadOnlyList<string> SourceNames => _sources
        .Select(s => string.IsNullOrWhiteSpace(s.PackageSource.Name)
            ? s.PackageSource.Source
            : s.PackageSource.Name)
        .ToList();

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
    /// Lists the versions available to install for <paramref name="packageId"/>, newest first.
    /// Drives the marketplace's version picker so a user can install a version other than the
    /// latest. The first source that can enumerate the id wins; sources without an all-versions
    /// endpoint, or that don't carry the package, are skipped. Returns an empty list when no
    /// source can resolve the id. Pre-release versions are excluded unless
    /// <paramref name="includePrerelease"/> is set.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetAvailableVersionsAsync(
        string packageId, bool includePrerelease, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return Array.Empty<string>();

        using var cache = new SourceCacheContext();

        foreach (var source in _sources)
        {
            FindPackageByIdResource? resource;
            try
            {
                resource = await source.GetResourceAsync<FindPackageByIdResource>(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                continue; // Source can't enumerate versions; try the next.
            }

            if (resource is null)
                continue;

            try
            {
                var versions = await resource
                    .GetAllVersionsAsync(packageId, cache, NullLogger.Instance, ct)
                    .ConfigureAwait(false);

                var filtered = versions
                    .Where(v => includePrerelease || !v.IsPrerelease)
                    .OrderByDescending(v => v)
                    .Select(v => v.ToNormalizedString())
                    .ToList();

                if (filtered.Count > 0)
                    return filtered;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Enumeration failed on this source; fall through to the next.
            }
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Resolves the concrete version a reference would install as, without downloading. A
    /// pinned reference returns its own version; an unpinned ("latest") reference is resolved
    /// against the configured sources. Returns <c>null</c> when a latest reference cannot be
    /// resolved (offline or package not found), so the caller can fall back or report it.
    /// </summary>
    public async Task<string?> ResolveVersionAsync(string packageId, string? version, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(version))
            return version;
        return await new NuGetPackageResolver().ResolveLatestStableVersionAsync(packageId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Rejects a package id or version that contains anything other than the characters legal
    /// in NuGet identifiers and versions, before it is used to compose a managed-directory
    /// path. This blocks path traversal (e.g. a version of <c>../../foo</c>) from a crafted
    /// notebook or assembly name reaching the file system.
    /// </summary>
    private static bool IsSafePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        foreach (var c in value)
        {
            if (!(char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or '+'))
                return false;
        }
        return true;
    }

    /// <summary>
    /// The runtime major of the current process. Package assets are selected for the
    /// running runtime at extraction time, so this is the newest asset generation this
    /// process can load.
    /// </summary>
    private static readonly int HostRuntimeMajor = Environment.Version.Major;

    /// <summary>
    /// Floor for runtime subfolder labels. Assets that demand nothing newer (netstandard,
    /// net5 and older) are labeled net6.0, the oldest TFM package resolution considers,
    /// and load on every supported host.
    /// </summary>
    private const int MinRuntimeLabelMajor = 6;

    /// <summary>
    /// Picks the assemblies to load for one installed package version, or <c>null</c> when
    /// nothing this runtime can load is present. Installs live in per-runtime subfolders
    /// (<c>net8.0</c>, <c>net10.0</c>, ...) whose name records the minimum runtime major
    /// the assemblies inside require; the newest folder at or below the current runtime
    /// wins. The partition exists because every host on the machine shares the managed
    /// directory while extracting assets for its own runtime: without it, a host running
    /// on .NET 10 lays down net10.0 assets that a .NET 8 host later trusts and fails to
    /// load. A flat set of assemblies directly in the version folder (written before
    /// installs were partitioned) is used only after probing that this runtime meets what
    /// they require.
    /// </summary>
    private static (string Directory, string[] Assemblies)? TryPickCompatibleAssemblies(string versionDir)
    {
        if (!Directory.Exists(versionDir))
            return null;

        var bestMajor = -1;
        string? bestDir = null;
        string[]? bestDlls = null;
        foreach (var subDir in Directory.GetDirectories(versionDir))
        {
            if (!TryParseRuntimeLabel(Path.GetFileName(subDir), out var major) ||
                major > HostRuntimeMajor || major <= bestMajor)
                continue;

            var dlls = Directory.GetFiles(subDir, "*.dll");
            if (dlls.Length == 0)
                continue;

            bestMajor = major;
            bestDir = subDir;
            bestDlls = dlls;
        }

        if (bestDir is not null)
            return (bestDir, bestDlls!);

        var flat = Directory.GetFiles(versionDir, "*.dll");
        if (flat.Length > 0 && GetRequiredRuntimeMajor(flat) <= HostRuntimeMajor)
            return (versionDir, flat);

        return null;
    }

    /// <summary>Runtime subfolder a freshly resolved set of assemblies is stored under.</summary>
    private static string RuntimeLabelFor(IReadOnlyList<string> assemblyPaths)
        => $"net{Math.Max(GetRequiredRuntimeMajor(assemblyPaths), MinRuntimeLabelMajor)}.0";

    /// <summary>Parses a per-runtime subfolder name of the form <c>net8.0</c> or <c>net10.0</c>.</summary>
    private static bool TryParseRuntimeLabel(string name, out int major)
    {
        major = 0;
        if (name.Length < 6 ||
            !name.StartsWith("net", StringComparison.OrdinalIgnoreCase) ||
            !name.EndsWith(".0", StringComparison.Ordinal))
            return false;
        return int.TryParse(name.AsSpan(3, name.Length - 5), out major) && major > 0;
    }

    /// <summary>Highest runtime major any assembly in the set requires; 0 when none declares one.</summary>
    private static int GetRequiredRuntimeMajor(IReadOnlyList<string> assemblyPaths)
    {
        var required = 0;
        foreach (var path in assemblyPaths)
            required = Math.Max(required, GetRequiredRuntimeMajor(path));
        return required;
    }

    /// <summary>
    /// The runtime major an assembly requires, read from its core-library reference without
    /// loading any code. net5 and newer assemblies reference System.Runtime at their target
    /// major, which recovers the TFM the assets were extracted for. Native or unreadable
    /// files report 0 so they never disqualify the set they sit in.
    /// </summary>
    internal static int GetRequiredRuntimeMajor(string assemblyPath)
    {
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
                return 0;

            var metadata = pe.GetMetadataReader();
            var required = 0;
            foreach (var handle in metadata.AssemblyReferences)
            {
                var reference = metadata.GetAssemblyReference(handle);
                var name = metadata.GetString(reference.Name);
                if ((string.Equals(name, "System.Runtime", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(name, "System.Private.CoreLib", StringComparison.OrdinalIgnoreCase)) &&
                    reference.Version.Major >= 5)
                {
                    required = Math.Max(required, reference.Version.Major);
                }
            }
            return required;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Ensures a package is present in the managed extensions directory and returns the
    /// assembly paths to load. When a copy this runtime can load is already installed this
    /// is offline and fast; otherwise the package and its transitive dependencies are
    /// downloaded, then copied into <c>{managedDir}/{id}/{version}/{netN.0}/</c>, where the
    /// last segment records the minimum runtime the assets require (see
    /// <see cref="TryPickCompatibleAssemblies"/>). The package directory is registered with
    /// the runtime resolver so co-located dependency assemblies resolve when the extension
    /// is loaded.
    /// </summary>
    public async Task<MarketplaceInstallResult> EnsureInstalledAsync(
        string packageId, string? version, string managedDir, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(managedDir);
        if (!IsSafePathSegment(packageId))
            throw new ArgumentException($"Invalid package id '{packageId}'.", nameof(packageId));
        if (version is not null && !IsSafePathSegment(version))
            throw new ArgumentException($"Invalid package version '{version}'.", nameof(version));

        // Fast path: a pinned version already laid down on disk in a copy this runtime
        // can load.
        if (!string.IsNullOrWhiteSpace(version))
        {
            if (TryPickCompatibleAssemblies(Path.Combine(managedDir, packageId, version)) is { } existing)
            {
                NuGetRuntimeResolver.AddManagedSearchDirectory(existing.Directory);
                return new MarketplaceInstallResult(version, existing.Directory, existing.Assemblies);
            }
        }

        var resolver = new NuGetPackageResolver();
        var result = await resolver.ResolvePackageAsync(packageId, version, ct).ConfigureAwait(false);

        var targetDir = Path.Combine(
            managedDir, packageId, result.ResolvedVersion, RuntimeLabelFor(result.AssemblyPaths));
        var copied = CopyAssembliesToManaged(result.AssemblyPaths, targetDir);
        CopyIconToManaged(packageId, result.ResolvedVersion, managedDir);

        NuGetRuntimeResolver.AddManagedSearchDirectory(targetDir);
        return new MarketplaceInstallResult(result.ResolvedVersion, targetDir, copied);
    }

    /// <summary>
    /// Returns the assembly paths for a package already laid down in the managed directory,
    /// without contacting any feed. Used to reload sideloaded local extensions on open: a
    /// local file cannot be re-fetched, so a missing managed directory yields <c>null</c>
    /// rather than a download attempt. Returns <c>null</c> when the version is unknown or no
    /// assemblies this runtime can load are present.
    /// </summary>
    public MarketplaceInstallResult? TryResolveInstalled(string packageId, string? version, string managedDir)
    {
        if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(managedDir))
            return null;
        if (!IsSafePathSegment(packageId) || !IsSafePathSegment(version))
            return null;

        if (TryPickCompatibleAssemblies(Path.Combine(managedDir, packageId, version)) is not { } picked)
            return null;

        NuGetRuntimeResolver.AddManagedSearchDirectory(picked.Directory);
        return new MarketplaceInstallResult(version, picked.Directory, picked.Assemblies);
    }

    /// <summary>
    /// Returns the highest version of a package already laid down in the managed directory,
    /// without contacting any feed. Used as an offline fallback for an unpinned ("latest")
    /// required extension when the feed cannot be reached: a previously downloaded copy still
    /// loads. Returns <c>null</c> when nothing this runtime can load is installed for the
    /// package.
    /// </summary>
    public MarketplaceInstallResult? TryResolveInstalledLatest(string packageId, string managedDir)
    {
        if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(managedDir))
            return null;
        if (!IsSafePathSegment(packageId))
            return null;

        var packageRoot = Path.Combine(managedDir, packageId);
        if (!Directory.Exists(packageRoot))
            return null;

        var best = Directory.GetDirectories(packageRoot)
            .Select(d => (Dir: d, Name: Path.GetFileName(d)))
            .Where(t => NuGet.Versioning.NuGetVersion.TryParse(t.Name, out _))
            .OrderByDescending(t => NuGet.Versioning.NuGetVersion.Parse(t.Name))
            .FirstOrDefault();

        return best.Dir is null ? null : TryResolveInstalled(packageId, best.Name, managedDir);
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
    /// <c>.dll</c> is copied into <c>{managedDir}/{assemblyName}/{assemblyVersion}/{netN.0}/</c>;
    /// a <c>.nupkg</c> is resolved like any other NuGet package (its containing folder is added
    /// as a source so the package itself is found locally while transitive dependencies still
    /// resolve from configured feeds) and the resulting closure is copied into
    /// <c>{managedDir}/{id}/{version}/{netN.0}/</c>. The trailing segment records the minimum
    /// runtime the assemblies require (see <see cref="TryPickCompatibleAssemblies"/>). The
    /// package directory is registered with the runtime resolver so co-located dependencies
    /// resolve when the extension loads.
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

        var targetDir = Path.Combine(
            managedDir, id, result.ResolvedVersion, RuntimeLabelFor(result.AssemblyPaths));
        var copied = CopyAssembliesToManaged(result.AssemblyPaths, targetDir);
        CopyIconToManaged(id, result.ResolvedVersion, managedDir);

        NuGetRuntimeResolver.AddManagedSearchDirectory(targetDir);
        return new LocalInstallResult(id, result.ResolvedVersion, targetDir, copied);
    }

    private static LocalInstallResult InstallLooseDll(string dllPath, string managedDir)
    {
        var name = AssemblyName.GetAssemblyName(dllPath);
        var id = name.Name ?? Path.GetFileNameWithoutExtension(dllPath);
        var version = FormatAssemblyVersion(name.Version);

        // The id comes from the assembly's own metadata, which a crafted file controls. Reject
        // anything that isn't a plain identifier so it can't traverse out of the managed dir.
        if (!IsSafePathSegment(id))
            throw new InvalidOperationException($"Assembly name '{id}' is not a valid extension id.");

        var targetDir = Path.Combine(managedDir, id, version, RuntimeLabelFor(new[] { dllPath }));
        Directory.CreateDirectory(targetDir);

        var dest = Path.Combine(targetDir, Path.GetFileName(dllPath));
        File.Copy(dllPath, dest, overwrite: true);

        NuGetRuntimeResolver.AddManagedSearchDirectory(targetDir);
        return new LocalInstallResult(id, version, targetDir, new[] { dest });
    }

    /// <summary>
    /// Copies the downloaded package icon next to the installed version, one level above the
    /// runtime-specific assembly directory because an icon does not vary by runtime. The
    /// managed directory is the durable store: the download cache can be cleared at any time,
    /// and a sideloaded package can never be fetched again.
    /// </summary>
    private static void CopyIconToManaged(string packageId, string version, string managedDir)
    {
        try
        {
            if (NuGetPackageResolver.TryGetCachedIconPath(packageId, version) is not { } source)
                return;

            var targetDir = Path.Combine(managedDir, packageId, version);
            Directory.CreateDirectory(targetDir);
            File.Copy(source, Path.Combine(targetDir, Path.GetFileName(source)), overwrite: true);
        }
        catch
        {
            // An icon is decoration; failing to place one must not fail an install.
        }
    }

    /// <summary>
    /// Upper bound on an icon inlined into a row. Well above a conventional package icon, which
    /// is a few tens of kilobytes, but low enough that a pane listing many packages cannot turn
    /// into megabytes of base64 travelling to the client. A larger icon is treated as absent and
    /// the row falls back to its lettered tile.
    /// </summary>
    private const long MaxEmbeddedIconBytes = 256 * 1024;

    /// <summary>
    /// Reads the installed icon for a package as a <c>data:</c> URI, or returns <c>null</c> when
    /// the package has no icon, was installed before icons were kept, or is a loose assembly
    /// with no package to carry one. Reading locally rather than fetching the feed's icon URL
    /// means the pane makes no network request, works offline, and shows the icon for sideloaded
    /// packages that no feed knows about.
    /// </summary>
    /// <param name="packageId">The package to read the icon for.</param>
    /// <param name="version">
    /// The pinned version, or <c>null</c> for an unpinned reference, in which case the highest
    /// installed version supplies the icon.
    /// </param>
    /// <param name="managedDir">The managed extension directory holding installed packages.</param>
    public static string? TryReadPackageIconDataUri(string packageId, string? version, string managedDir)
    {
        if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(managedDir))
            return null;
        if (!IsSafePathSegment(packageId))
            return null;
        if (version is not null && !IsSafePathSegment(version))
            return null;

        try
        {
            var packageRoot = Path.Combine(managedDir, packageId);
            if (!Directory.Exists(packageRoot))
                return null;

            var versionDir = version is not null
                ? Path.Combine(packageRoot, version)
                : Directory.GetDirectories(packageRoot)
                    .Where(d => NuGet.Versioning.NuGetVersion.TryParse(Path.GetFileName(d), out _))
                    .OrderByDescending(d => NuGet.Versioning.NuGetVersion.Parse(Path.GetFileName(d)))
                    .FirstOrDefault();

            if (versionDir is null || !Directory.Exists(versionDir))
                return null;

            var iconPath = Directory.EnumerateFiles(versionDir, "icon.*").FirstOrDefault();
            if (iconPath is null)
                return null;

            var info = new FileInfo(iconPath);
            if (info.Length == 0 || info.Length > MaxEmbeddedIconBytes)
                return null;

            var bytes = File.ReadAllBytes(iconPath);
            if (SniffImageMediaType(bytes) is not { } mediaType)
                return null;

            return $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Identifies an image from its leading bytes. The media type is taken from the content
    /// rather than the file extension so a package cannot get arbitrary text rendered by naming
    /// it <c>icon.png</c>, and anything unrecognised is reported as not an image at all.
    /// </summary>
    private static string? SniffImageMediaType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            return "image/png";

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";

        if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 &&
            bytes[3] == 0x38 && (bytes[4] == 0x37 || bytes[4] == 0x39) && bytes[5] == 0x61)
            return "image/gif";

        return null;
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
