using System.IO.Compression;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Verso.Resources;

namespace Verso.Kernels;

/// <summary>
/// Result of resolving a NuGet package, including the resolved version, assembly paths,
/// and the full transitive set of packages that were resolved (id + version), useful
/// for diagnostics when a runtime assembly load fails and the user needs to see what
/// actually got pulled in.
/// </summary>
internal sealed record NuGetResolveResult(
    string PackageId,
    string ResolvedVersion,
    IReadOnlyList<string> AssemblyPaths,
    IReadOnlyList<(string Id, string Version)> ResolvedPackages);

/// <summary>
/// Downloads NuGet packages and their transitive dependencies, extracts DLLs from the
/// appropriate TFM folder, and caches results in a temp directory.
/// Loads package sources from the standard NuGet.Config settings chain and supports
/// session-scoped sources added via <c>#i</c> directives.
/// </summary>
internal sealed class NuGetPackageResolver
{
    // Cache path includes a schema suffix so a resolver schema change (e.g. extracting
    // runtimes/{rid}/lib/ assemblies in addition to lib/) invalidates older caches that
    // contain only the lib/ stubs.
    internal static readonly string CacheRoot =
        Path.Combine(Path.GetTempPath(), "verso-nuget-packages", $"net{Environment.Version.Major}.0-v2");

    private readonly List<SourceRepository> _sources;

    /// <summary>
    /// Target framework used for selecting lib groups and dependency groups from NuGet packages.
    /// Derived from the running runtime version so the correct TFM is always selected.
    /// </summary>
    private static readonly NuGetFramework TargetFramework =
        NuGetFramework.Parse($"net{Environment.Version.Major}.0");

    private static readonly string[] PreferredFrameworks = BuildPreferredFrameworks();

    private static string[] BuildPreferredFrameworks()
    {
        var runtimeMajor = Environment.Version.Major;
        var frameworks = new List<string>();
        for (var v = runtimeMajor; v >= 6; v--)
            frameworks.Add($"net{v}.0");
        frameworks.Add("netstandard2.1");
        frameworks.Add("netstandard2.0");
        return frameworks.ToArray();
    }

    /// <summary>
    /// Maximum depth for transitive dependency resolution. Prevents runaway expansion
    /// in deep dependency trees.  Depth 6 covers packages like EF Core whose transitive
    /// chain (e.g. EF.Sqlite → EF.Sqlite.Core → EF.Relational → EF → Extensions.Logging
    /// → Extensions.Logging.Abstractions) requires at least 5 hops.
    /// </summary>
    private const int MaxDependencyDepth = 6;

    public NuGetPackageResolver()
    {
        _sources = new List<SourceRepository>();

        try
        {
            var settings = Settings.LoadDefaultSettings(root: Directory.GetCurrentDirectory());
            var provider = new PackageSourceProvider(settings);

            foreach (var source in provider.LoadPackageSources().Where(s => s.IsEnabled))
                _sources.Add(Repository.Factory.GetCoreV3(source));
        }
        catch
        {
            // Config loading must never prevent package resolution
        }

        // nuget.org fallback when no NuGet.Config sources exist
        if (_sources.Count == 0)
            _sources.Add(Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json"));
    }

    /// <summary>
    /// Adds a package source at the highest priority (before NuGet.Config sources).
    /// Used for session-scoped <c>#i</c> directive sources.
    /// </summary>
    public void AddSource(string source)
    {
        var trimmed = source.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return;

        // Skip if this source URL is already in the list
        if (_sources.Any(s => string.Equals(s.PackageSource.Source, trimmed, StringComparison.OrdinalIgnoreCase)))
            return;

        _sources.Insert(0, Repository.Factory.GetCoreV3(trimmed));
    }

    /// <summary>
    /// Parses a <c>#i</c> source directive value, validating it is a URL, UNC path, or local directory.
    /// Returns <c>null</c> if the value is not a valid package source.
    /// </summary>
    internal static string? ParseSourceDirective(string? directive)
    {
        if (string.IsNullOrWhiteSpace(directive))
            return null;

        var source = directive.Trim();

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" or "file")
            return source;

        // UNC paths and local directories
        if (source.StartsWith(@"\\") || Directory.Exists(source))
            return source;

        return null;
    }

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
    /// Resolves a NuGet package and its transitive dependencies, downloading them if not cached,
    /// and returns the resolved version and combined assembly paths from all packages.
    /// </summary>
    public async Task<NuGetResolveResult> ResolvePackageAsync(
        string packageId, string? version, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(packageId);

        var allAssemblyPaths = new List<string>();
        var resolvedPackages = new List<(string Id, string Version)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Everything this call puts on disk belongs together, which is what lets a managed
        // assembly reach the natives of a package versioned separately from it.
        var resolution = NuGetRuntimeResolver.NewResolution();

        var resolvedVersion = await ResolveWithDependenciesAsync(
            packageId, version, allAssemblyPaths, resolvedPackages, visited, depth: 0, resolution, ct).ConfigureAwait(false);

        return new NuGetResolveResult(packageId, resolvedVersion, allAssemblyPaths, resolvedPackages);
    }

    /// <summary>
    /// Resolves the latest stable version of a package from the configured sources without
    /// downloading anything. Used to detect whether a notebook's "latest" required extension
    /// has drifted from the version the user previously approved, so consent can be requested
    /// before the new version loads. Returns <c>null</c> when no source can be reached or the
    /// package has no stable release.
    /// </summary>
    public async Task<string?> ResolveLatestStableVersionAsync(string packageId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return null;

        var cache = new SourceCacheContext();
        foreach (var source in _sources)
        {
            try
            {
                var res = await source.GetResourceAsync<FindPackageByIdResource>(ct).ConfigureAwait(false);
                var versions = await res.GetAllVersionsAsync(packageId, cache, NullLogger.Instance, ct).ConfigureAwait(false);
                var latest = versions
                    .Where(v => !v.IsPrerelease)
                    .OrderByDescending(v => v)
                    .FirstOrDefault();
                if (latest is not null)
                    return latest.ToString();
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Source unavailable, try the next one.
            }
        }
        return null;
    }

    /// <summary>
    /// Recursively resolves a package and its dependencies, collecting all assembly paths
    /// and a flat list of (id, version) pairs for every package that was actually resolved
    /// (i.e. not skipped by <see cref="IsFrameworkPackage"/> or already visited).
    /// </summary>
    private async Task<string> ResolveWithDependenciesAsync(
        string packageId, string? version, List<string> allPaths,
        List<(string Id, string Version)> resolvedPackages,
        HashSet<string> visited, int depth, int resolution, CancellationToken ct)
    {
        if (depth > MaxDependencyDepth) return version ?? "";
        if (!visited.Add(packageId)) return version ?? "";
        if (IsFrameworkPackage(packageId)) return version ?? "";

        var (resolvedVersion, assemblyPaths, dependencies) =
            await DownloadSinglePackageAsync(packageId, version, resolution, ct).ConfigureAwait(false);

        allPaths.AddRange(assemblyPaths);
        resolvedPackages.Add((packageId, resolvedVersion));

        foreach (var (depId, depMinVersion) in dependencies)
        {
            await ResolveWithDependenciesAsync(
                depId, depMinVersion, allPaths, resolvedPackages, visited, depth + 1, resolution, ct).ConfigureAwait(false);
        }

        return resolvedVersion;
    }

    /// <summary>
    /// Downloads a single NuGet package (with caching), extracts its DLLs, and reads its
    /// dependency list for the target framework.
    /// </summary>
    private async Task<(string ResolvedVersion, List<string> AssemblyPaths, List<(string Id, string? MinVersion)> Dependencies)>
        DownloadSinglePackageAsync(string packageId, string? version, int resolution, CancellationToken ct)
    {
        var logger = NullLogger.Instance;
        var cache = new SourceCacheContext();

        // If version is already known, check cache before hitting any source
        if (version is not null && NuGetVersion.TryParse(version, out var parsedVersion))
        {
            var cachedDir = Path.Combine(CacheRoot, packageId, parsedVersion.ToString());
            var cachedDepsFile = Path.Combine(cachedDir, ".deps");
            if (Directory.Exists(cachedDir))
            {
                var cachedDlls = Directory.GetFiles(cachedDir, "*.dll");
                var cachedDeps = ReadCachedDependencies(cachedDepsFile);
                if (cachedDeps is not null)
                {
                    RegisterCachedRuntimeDirs(cachedDir, resolution);
                    return (parsedVersion.ToString(), new List<string>(cachedDlls), cachedDeps);
                }
            }
        }

        // Try each configured source in priority order
        FindPackageByIdResource? resource = null;
        NuGetVersion? resolvedVersion = null;
        Exception? lastException = null;

        foreach (var source in _sources)
        {
            try
            {
                var res = await source.GetResourceAsync<FindPackageByIdResource>(ct).ConfigureAwait(false);

                if (version is not null && NuGetVersion.TryParse(version, out var parsed))
                {
                    // Specific version requested: verify it exists on this source
                    var versions = await res.GetAllVersionsAsync(packageId, cache, logger, ct).ConfigureAwait(false);
                    if (versions.Contains(parsed))
                    {
                        resolvedVersion = parsed;
                        resource = res;
                        break;
                    }
                }
                else
                {
                    // No version specified: find the latest stable
                    var versions = await res.GetAllVersionsAsync(packageId, cache, logger, ct).ConfigureAwait(false);
                    var latest = versions
                        .Where(v => !v.IsPrerelease)
                        .OrderByDescending(v => v)
                        .FirstOrDefault();
                    if (latest is not null)
                    {
                        resolvedVersion = latest;
                        resource = res;
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastException = ex;
                // Source unavailable, try next
            }
        }

        if (resource is null || resolvedVersion is null)
        {
            var sourceNames = string.Join(", ", _sources.Select(s => s.PackageSource.Source));
            var message = $"Package '{packageId}'{(version is not null ? $" v{version}" : "")} was not found on any configured source. Sources tried: {sourceNames}";
            if (lastException is not null)
                message += $" Last error: {lastException.GetType().Name}: {lastException.Message}";
            throw new InvalidOperationException(message);
        }

        var packageDir = Path.Combine(CacheRoot, packageId, resolvedVersion.ToString());
        var depsFile = Path.Combine(packageDir, ".deps");

        // Check cache — both DLLs and dependency list
        if (Directory.Exists(packageDir))
        {
            var cachedDlls = Directory.GetFiles(packageDir, "*.dll");
            var cachedDeps = ReadCachedDependencies(depsFile);

            // Cache hit: package was previously resolved (may legitimately have 0 DLLs for meta-packages)
            if (cachedDeps is not null)
            {
                RegisterCachedRuntimeDirs(packageDir, resolution);
                return (resolvedVersion.ToString(), new List<string>(cachedDlls), cachedDeps);
            }

            // Legacy cache entry (no .deps file) — re-extract if it has DLLs but no deps info
            if (cachedDlls.Length > 0)
            {
                RegisterCachedRuntimeDirs(packageDir, resolution);
                // Download just to read dependencies, then write the deps cache
                var deps = await DownloadAndReadDependenciesAsync(
                    packageId, resolvedVersion, resource, cache, logger, packageDir, ct).ConfigureAwait(false);
                WriteCachedDependencies(depsFile, deps);
                return (resolvedVersion.ToString(), new List<string>(cachedDlls), deps);
            }
        }

        Directory.CreateDirectory(packageDir);

        // Download and extract
        var tempNupkg = Path.Combine(packageDir, $"{packageId}.{resolvedVersion}.nupkg");
        using (var fileStream = File.Create(tempNupkg))
        {
            var downloaded = await resource.CopyNupkgToStreamAsync(
                packageId, resolvedVersion, fileStream, cache, logger, ct).ConfigureAwait(false);

            if (!downloaded)
                throw new InvalidOperationException(
                    string.Format(Strings.Error_PackageDownloadFailed, packageId, resolvedVersion));
        }

        var assemblyPaths = new List<string>();
        List<(string Id, string? MinVersion)> dependencies;

        using (var reader = new PackageArchiveReader(tempNupkg))
        {
            // Extract managed DLLs from lib/ (may be reference/stub assemblies for packages
            // that ship platform-specific implementations under runtimes/{rid}/lib/).
            assemblyPaths = await ExtractDllsAsync(reader, packageDir, ct).ConfigureAwait(false);

            // Read dependencies for our target framework
            dependencies = await ReadDependenciesAsync(reader, ct).ConfigureAwait(false);

            // Keep the package icon, if it has one. The archive is deleted below, so this is
            // the only moment it can be captured, and a caller that wants to show a package
            // identity later has no other local source for it.
            await ExtractIconAsync(reader, packageDir, ct).ConfigureAwait(false);
        }

        // Extract native and runtime-managed libraries after closing the PackageArchiveReader
        // to avoid file contention — uses ZipFile.OpenRead() directly for reliable extraction.
        ExtractNativeLibs(tempNupkg, packageDir, resolution);

        // Overwrite any lib/ stubs with the platform-specific runtime implementation
        // from runtimes/{rid}/lib/{tfm}/ when present. Required for packages like
        // Microsoft.Data.SqlClient whose lib/ DLL is a reference assembly only.
        var runtimeManagedPaths = ExtractRuntimeManagedLibs(tempNupkg, packageDir);
        foreach (var path in runtimeManagedPaths)
        {
            if (!assemblyPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                assemblyPaths.Add(path);
        }

        // Register the directory so cross-package assembly references resolve
        if (assemblyPaths.Count > 0)
            NuGetRuntimeResolver.AddManagedSearchDirectory(packageDir, resolution);

        // Cache the dependency list (so meta-packages with 0 DLLs are recognized as cached)
        WriteCachedDependencies(depsFile, dependencies);

        // Clean up nupkg
        try { File.Delete(tempNupkg); } catch { /* best effort */ }

        return (resolvedVersion.ToString(), assemblyPaths, dependencies);
    }

    /// <summary>
    /// Extracts DLLs from the best-matching TFM folder in a NuGet package.
    /// </summary>
    private static async Task<List<string>> ExtractDllsAsync(
        PackageArchiveReader reader, string packageDir, CancellationToken ct)
    {
        var assemblyPaths = new List<string>();
        var libItems = (await reader.GetLibItemsAsync(ct).ConfigureAwait(false)).ToList();

        FrameworkSpecificGroup? selectedGroup = null;

        foreach (var preferred in PreferredFrameworks)
        {
            selectedGroup = libItems.FirstOrDefault(g =>
                g.TargetFramework.GetShortFolderName()
                    .Equals(preferred, StringComparison.OrdinalIgnoreCase));
            if (selectedGroup is not null)
                break;
        }

        selectedGroup ??= libItems.FirstOrDefault(g =>
            g.Items.Any(i => i.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)));

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

        return assemblyPaths;
    }

    /// <summary>
    /// The file name a cached package icon is stored under, without an extension. The original
    /// extension is preserved so the file stays recognisable on disk.
    /// </summary>
    private const string IconFileStem = "icon";

    /// <summary>
    /// Upper bound on a cached package icon, matching the limit nuget.org enforces on embedded
    /// icons. Anything larger is treated as absent rather than copied around.
    /// </summary>
    internal const long MaxIconBytes = 1024 * 1024;

    /// <summary>
    /// Extracts the package icon declared by the nuspec into the cache directory. Packages
    /// without an embedded icon, and packages declaring one that is not actually in the
    /// archive, are both no-ops: an icon is decoration, so nothing here is allowed to fail an
    /// install.
    /// </summary>
    private static async Task ExtractIconAsync(
        PackageArchiveReader reader, string packageDir, CancellationToken ct)
    {
        try
        {
            var iconPath = reader.NuspecReader.GetIcon();
            if (string.IsNullOrWhiteSpace(iconPath))
                return;

            // The nuspec records the icon with the separator used when the package was built.
            var entry = reader.GetEntry(iconPath.Replace('\\', '/'));
            if (entry is null || entry.Length > MaxIconBytes)
                return;

            var dest = Path.Combine(packageDir, IconFileStem + Path.GetExtension(iconPath));
            using var entryStream = entry.Open();
            using var destStream = File.Create(dest);
            await entryStream.CopyToAsync(destStream, ct).ConfigureAwait(false);
        }
        catch
        {
            // A missing or unreadable icon must never fail a package install.
        }
    }

    /// <summary>
    /// Returns the path of the cached icon for a package already downloaded into the cache, or
    /// <c>null</c> when the package has no icon or was cached before icons were kept.
    /// </summary>
    internal static string? TryGetCachedIconPath(string packageId, string version)
    {
        if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(version))
            return null;

        try
        {
            var dir = Path.Combine(CacheRoot, packageId, version);
            return Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir, IconFileStem + ".*").FirstOrDefault()
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts native libraries from the <c>runtimes/{rid}/native/</c> folder of a NuGet package
    /// into a <c>native/</c> subdirectory and registers the directory with the
    /// <see cref="NuGetRuntimeResolver"/> so the runtime can find them.
    /// Uses <see cref="ZipFile"/> directly for reliable extraction (bypasses NuGet reader path matching).
    /// <para>
    /// Only a fresh extraction runs this; a cache hit registers what is already on disk. A cache
    /// populated under an older rule for choosing a runtime identifier therefore keeps whatever
    /// it holds, which on a machine running under emulation or on a musl C library can be a
    /// native this platform cannot load. Clearing the cache directory is the recovery.
    /// </para>
    /// </summary>
    private static void ExtractNativeLibs(string nupkgPath, string packageDir, int resolution)
    {
        var rids = NuGetRuntimeResolver.GetRidFallbacks();
        var nativeDir = Path.Combine(packageDir, "native");
        var extracted = false;

        // Per-filename best candidate: the earlier the RID appears in the fallback chain, the
        // more specific it is. Selecting rather than extracting every match matters because a
        // package can ship the same file name under several RIDs this platform accepts, and
        // SkiaSharp and SQLitePCLRaw both ship a musl build beside the glibc one. Extracting
        // them in archive order would leave whichever came last.
        var best = new Dictionary<string, (int RidIndex, ZipArchiveEntry Entry)>(
            StringComparer.OrdinalIgnoreCase);

        using var archive = ZipFile.OpenRead(nupkgPath);
        foreach (var entry in archive.Entries)
        {
            var fullName = entry.FullName;

            // Match runtimes/{rid}/native/{file}
            if (!fullName.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase))
                continue;

            // Parse the RID from the path: runtimes/{rid}/native/{file}
            var segments = fullName.Split('/');
            if (segments.Length < 4 ||
                !segments[2].Equals("native", StringComparison.OrdinalIgnoreCase))
                continue;

            var entryRid = segments[1];

            // Check if this RID matches our platform
            var ridIndex = Array.FindIndex(rids, r => r.Equals(entryRid, StringComparison.OrdinalIgnoreCase));
            if (ridIndex < 0)
                continue;

            var fileName = segments[^1];
            if (string.IsNullOrEmpty(fileName) || entry.Length == 0)
                continue;

            if (!best.TryGetValue(fileName, out var current) || ridIndex < current.RidIndex)
                best[fileName] = (ridIndex, entry);
        }

        foreach (var (fileName, candidate) in best)
        {
            try
            {
                Directory.CreateDirectory(nativeDir);
                var destPath = Path.Combine(nativeDir, fileName);
                candidate.Entry.ExtractToFile(destPath, overwrite: true);
                extracted = true;
            }
            catch
            {
                // Best effort — skip files that can't be extracted
            }
        }

        if (extracted)
        {
            NuGetRuntimeResolver.AddNativeSearchDirectory(nativeDir, resolution);
        }
    }

    /// <summary>
    /// Extracts platform-specific managed assemblies from <c>runtimes/{rid}/lib/{tfm}/</c>
    /// folders. Some packages (e.g. <c>Microsoft.Data.SqlClient</c>) ship a reference/stub
    /// assembly under <c>lib/{tfm}/</c> and the actual implementation under
    /// <c>runtimes/{rid}/lib/{tfm}/</c>. Without this pass, the stub is loaded and provider
    /// initialisation fails at runtime.
    ///
    /// For each DLL filename, the most-specific (RID, TFM) winner is selected: RID
    /// specificity (using <see cref="NuGetRuntimeResolver.GetRidFallbacks"/>) takes
    /// precedence over TFM specificity (using <see cref="PreferredFrameworks"/>), matching
    /// how the .NET runtime resolves runtime assets. The selected DLL is written into
    /// <paramref name="packageDir"/>, overwriting any same-named lib/ stub.
    /// </summary>
    private static List<string> ExtractRuntimeManagedLibs(string nupkgPath, string packageDir)
    {
        var rids = NuGetRuntimeResolver.GetRidFallbacks();
        var tfms = PreferredFrameworks;
        var extracted = new List<string>();

        // Per-filename best candidate: lower (ridIndex, tfmIndex) wins.
        var best = new Dictionary<string, (int RidIndex, int TfmIndex, ZipArchiveEntry Entry)>(
            StringComparer.OrdinalIgnoreCase);

        using var archive = ZipFile.OpenRead(nupkgPath);
        foreach (var entry in archive.Entries)
        {
            var fullName = entry.FullName;
            if (!fullName.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase))
                continue;

            // Expect: runtimes/{rid}/lib/{tfm}/{file}.dll
            var segments = fullName.Split('/');
            if (segments.Length < 5 ||
                !segments[2].Equals("lib", StringComparison.OrdinalIgnoreCase))
                continue;

            var entryRid = segments[1];
            var entryTfm = segments[3];
            var fileName = segments[^1];

            if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || entry.Length == 0)
                continue;

            var ridIndex = Array.FindIndex(rids, r => r.Equals(entryRid, StringComparison.OrdinalIgnoreCase));
            if (ridIndex < 0) continue;

            var tfmIndex = Array.FindIndex(tfms, t => t.Equals(entryTfm, StringComparison.OrdinalIgnoreCase));
            if (tfmIndex < 0) continue;

            if (!best.TryGetValue(fileName, out var current) ||
                ridIndex < current.RidIndex ||
                (ridIndex == current.RidIndex && tfmIndex < current.TfmIndex))
            {
                best[fileName] = (ridIndex, tfmIndex, entry);
            }
        }

        foreach (var (fileName, candidate) in best)
        {
            try
            {
                var destPath = Path.Combine(packageDir, fileName);
                candidate.Entry.ExtractToFile(destPath, overwrite: true);
                extracted.Add(destPath);
            }
            catch
            {
                // Best effort — skip files that can't be extracted
            }
        }

        return extracted;
    }

    /// <summary>
    /// Registers previously-extracted managed and native library directories from a cached
    /// package with the <see cref="NuGetRuntimeResolver"/>.
    /// </summary>
    private static void RegisterCachedRuntimeDirs(string packageDir, int resolution)
    {
        // Register managed assembly directory (the package dir itself contains DLLs)
        if (Directory.GetFiles(packageDir, "*.dll").Length > 0)
        {
            NuGetRuntimeResolver.AddManagedSearchDirectory(packageDir, resolution);
        }

        // Register native library directory
        var nativeDir = Path.Combine(packageDir, "native");
        if (Directory.Exists(nativeDir) && Directory.GetFiles(nativeDir).Length > 0)
        {
            NuGetRuntimeResolver.AddNativeSearchDirectory(nativeDir, resolution);
        }
    }

    /// <summary>
    /// Reads the package dependencies for the target framework from a NuGet package.
    /// </summary>
    private static async Task<List<(string Id, string? MinVersion)>> ReadDependenciesAsync(
        PackageArchiveReader reader, CancellationToken ct)
    {
        var depGroups = (await reader.GetPackageDependenciesAsync(ct).ConfigureAwait(false)).ToList();
        if (depGroups.Count == 0)
            return new List<(string, string?)>();

        // Select the dependency group best matching our target framework
        var reducer = new FrameworkReducer();
        var frameworks = depGroups.Select(g => g.TargetFramework).ToList();
        var nearest = reducer.GetNearest(TargetFramework, frameworks);

        var selectedGroup = nearest is not null
            ? depGroups.FirstOrDefault(g => g.TargetFramework.Equals(nearest))
            : depGroups.FirstOrDefault(g => g.TargetFramework.IsAny);

        if (selectedGroup is null)
            return new List<(string, string?)>();

        return selectedGroup.Packages
            .Select(p => (p.Id, p.VersionRange?.MinVersion?.ToString()))
            .ToList();
    }

    /// <summary>
    /// Downloads a package just to read its dependencies (used for legacy cache entries).
    /// </summary>
    private static async Task<List<(string Id, string? MinVersion)>> DownloadAndReadDependenciesAsync(
        string packageId, NuGetVersion version, FindPackageByIdResource resource,
        SourceCacheContext cache, ILogger logger, string packageDir, CancellationToken ct)
    {
        var tempNupkg = Path.Combine(packageDir, $"{packageId}.{version}.tmp.nupkg");
        try
        {
            using (var fileStream = File.Create(tempNupkg))
            {
                await resource.CopyNupkgToStreamAsync(packageId, version, fileStream, cache, logger, ct)
                    .ConfigureAwait(false);
            }

            using var reader = new PackageArchiveReader(tempNupkg);
            return await ReadDependenciesAsync(reader, ct).ConfigureAwait(false);
        }
        catch
        {
            return new List<(string, string?)>();
        }
        finally
        {
            try { File.Delete(tempNupkg); } catch { }
        }
    }

    /// <summary>
    /// Returns <c>true</c> if the package is part of the .NET shared framework and
    /// should not be downloaded (already available at runtime).
    /// <para>
    /// Only core runtime meta-packages (<c>Microsoft.NETCore.*</c>, <c>NETStandard.*</c>)
    /// are skipped; these contain no real assemblies, only target-framework metadata.
    /// </para>
    /// <para>
    /// <c>System.*</c> and <c>Microsoft.Extensions.*</c> packages are NOT skipped, even
    /// if their assembly names happen to appear in the TPA. The TPA reflects whatever
    /// version the host process happens to have loaded, which may be older than what a
    /// dependent package was compiled against (e.g. Microsoft.Data.SqlClient 7.0.1
    /// requires <c>System.Configuration.ConfigurationManager 9.0.0.0</c> while the host
    /// may only carry 8.x). The runtime preferentially resolves from the TPA, so most
    /// downloads remain unused; the <see cref="NuGetRuntimeResolver"/> handler only
    /// loads a downloaded copy when the TPA cannot satisfy a strict-version request.
    /// </para>
    /// </summary>
    private static bool IsFrameworkPackage(string packageId)
    {
        return packageId.StartsWith("Microsoft.NETCore.", StringComparison.OrdinalIgnoreCase) ||
               packageId.StartsWith("NETStandard.", StringComparison.OrdinalIgnoreCase) ||
               packageId.Equals("NETStandard.Library", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Writes the dependency list to a simple cache file so meta-packages (0 DLLs)
    /// are recognized as fully resolved on subsequent runs.
    /// </summary>
    private static void WriteCachedDependencies(string depsFile, List<(string Id, string? MinVersion)> deps)
    {
        try
        {
            var lines = deps.Select(d => d.MinVersion is not null ? $"{d.Id}|{d.MinVersion}" : d.Id);
            File.WriteAllLines(depsFile, lines);
        }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Reads a cached dependency list. Returns <c>null</c> if the cache file doesn't exist.
    /// </summary>
    private static List<(string Id, string? MinVersion)>? ReadCachedDependencies(string depsFile)
    {
        if (!File.Exists(depsFile))
            return null;

        try
        {
            var lines = File.ReadAllLines(depsFile);
            var result = new List<(string, string?)>();
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split('|', 2);
                result.Add((parts[0], parts.Length > 1 ? parts[1] : null));
            }
            return result;
        }
        catch
        {
            return null;
        }
    }
}
