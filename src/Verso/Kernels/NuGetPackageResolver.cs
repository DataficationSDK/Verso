using System.IO.Compression;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Verso.Kernels;

/// <summary>
/// Result of resolving a NuGet package, including the resolved version and assembly paths.
/// </summary>
internal sealed record NuGetResolveResult(string PackageId, string ResolvedVersion, IReadOnlyList<string> AssemblyPaths);

/// <summary>
/// Downloads NuGet packages and their transitive dependencies, extracts DLLs from the
/// appropriate TFM folder, and caches results in a temp directory.
/// </summary>
internal sealed class NuGetPackageResolver
{
    private static readonly string CacheRoot =
        Path.Combine(Path.GetTempPath(), "verso-nuget-packages");

    /// <summary>
    /// Target framework used for selecting lib groups and dependency groups from NuGet packages.
    /// </summary>
    private static readonly NuGetFramework TargetFramework = NuGetFramework.Parse("net8.0");

    private static readonly string[] PreferredFrameworks =
        { "net8.0", "net6.0", "netstandard2.1", "netstandard2.0" };

    /// <summary>
    /// Maximum depth for transitive dependency resolution. Prevents runaway expansion
    /// in deep dependency trees.  Depth 6 covers packages like EF Core whose transitive
    /// chain (e.g. EF.Sqlite → EF.Sqlite.Core → EF.Relational → EF → Extensions.Logging
    /// → Extensions.Logging.Abstractions) requires at least 5 hops.
    /// </summary>
    private const int MaxDependencyDepth = 6;

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
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var resolvedVersion = await ResolveWithDependenciesAsync(
            packageId, version, allAssemblyPaths, visited, depth: 0, ct).ConfigureAwait(false);

        return new NuGetResolveResult(packageId, resolvedVersion, allAssemblyPaths);
    }

    /// <summary>
    /// Recursively resolves a package and its dependencies, collecting all assembly paths.
    /// </summary>
    private async Task<string> ResolveWithDependenciesAsync(
        string packageId, string? version, List<string> allPaths,
        HashSet<string> visited, int depth, CancellationToken ct)
    {
        if (depth > MaxDependencyDepth) return version ?? "";
        if (!visited.Add(packageId)) return version ?? "";
        if (IsFrameworkPackage(packageId)) return version ?? "";

        var (resolvedVersion, assemblyPaths, dependencies) =
            await DownloadSinglePackageAsync(packageId, version, ct).ConfigureAwait(false);

        allPaths.AddRange(assemblyPaths);

        foreach (var (depId, depMinVersion) in dependencies)
        {
            await ResolveWithDependenciesAsync(
                depId, depMinVersion, allPaths, visited, depth + 1, ct).ConfigureAwait(false);
        }

        return resolvedVersion;
    }

    /// <summary>
    /// Downloads a single NuGet package (with caching), extracts its DLLs, and reads its
    /// dependency list for the target framework.
    /// </summary>
    private async Task<(string ResolvedVersion, List<string> AssemblyPaths, List<(string Id, string? MinVersion)> Dependencies)>
        DownloadSinglePackageAsync(string packageId, string? version, CancellationToken ct)
    {
        var repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
        var resource = await repository.GetResourceAsync<FindPackageByIdResource>(ct).ConfigureAwait(false);

        var logger = NullLogger.Instance;
        var cache = new SourceCacheContext();

        // Resolve version
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
        var depsFile = Path.Combine(packageDir, ".deps");

        // Check cache — both DLLs and dependency list
        if (Directory.Exists(packageDir))
        {
            var cachedDlls = Directory.GetFiles(packageDir, "*.dll");
            var cachedDeps = ReadCachedDependencies(depsFile);

            // Cache hit: package was previously resolved (may legitimately have 0 DLLs for meta-packages)
            if (cachedDeps is not null)
            {
                RegisterCachedRuntimeDirs(packageDir);
                return (resolvedVersion.ToString(), new List<string>(cachedDlls), cachedDeps);
            }

            // Legacy cache entry (no .deps file) — re-extract if it has DLLs but no deps info
            if (cachedDlls.Length > 0)
            {
                RegisterCachedRuntimeDirs(packageDir);
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
                throw new InvalidOperationException($"Failed to download package '{packageId}' v{resolvedVersion}.");
        }

        var assemblyPaths = new List<string>();
        List<(string Id, string? MinVersion)> dependencies;

        using (var reader = new PackageArchiveReader(tempNupkg))
        {
            // Extract managed DLLs from lib/
            assemblyPaths = await ExtractDllsAsync(reader, packageDir, ct).ConfigureAwait(false);

            // Register the directory so cross-package assembly references resolve
            if (assemblyPaths.Count > 0)
                NuGetRuntimeResolver.AddManagedSearchDirectory(packageDir);

            // Read dependencies for our target framework
            dependencies = await ReadDependenciesAsync(reader, ct).ConfigureAwait(false);
        }

        // Extract native libraries after closing the PackageArchiveReader to avoid
        // file contention — uses ZipFile.OpenRead() directly for reliable extraction.
        ExtractNativeLibs(tempNupkg, packageDir);

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
    /// Extracts native libraries from the <c>runtimes/{rid}/native/</c> folder of a NuGet package
    /// into a <c>native/</c> subdirectory and registers the directory with the
    /// <see cref="NuGetRuntimeResolver"/> so the runtime can find them.
    /// Uses <see cref="ZipFile"/> directly for reliable extraction (bypasses NuGet reader path matching).
    /// </summary>
    private static void ExtractNativeLibs(string nupkgPath, string packageDir)
    {
        var rids = NuGetRuntimeResolver.GetRidFallbacks();
        var nativeDir = Path.Combine(packageDir, "native");
        var extracted = false;

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
            if (!rids.Any(r => r.Equals(entryRid, StringComparison.OrdinalIgnoreCase)))
                continue;

            var fileName = segments[^1];
            if (string.IsNullOrEmpty(fileName) || entry.Length == 0)
                continue;

            try
            {
                Directory.CreateDirectory(nativeDir);
                var destPath = Path.Combine(nativeDir, fileName);
                entry.ExtractToFile(destPath, overwrite: true);
                extracted = true;
            }
            catch
            {
                // Best effort — skip files that can't be extracted
            }
        }

        if (extracted)
        {
            NuGetRuntimeResolver.AddNativeSearchDirectory(nativeDir);
        }
    }

    /// <summary>
    /// Registers previously-extracted managed and native library directories from a cached
    /// package with the <see cref="NuGetRuntimeResolver"/>.
    /// </summary>
    private static void RegisterCachedRuntimeDirs(string packageDir)
    {
        // Register managed assembly directory (the package dir itself contains DLLs)
        if (Directory.GetFiles(packageDir, "*.dll").Length > 0)
        {
            NuGetRuntimeResolver.AddManagedSearchDirectory(packageDir);
        }

        // Register native library directory
        var nativeDir = Path.Combine(packageDir, "native");
        if (Directory.Exists(nativeDir) && Directory.GetFiles(nativeDir).Length > 0)
        {
            NuGetRuntimeResolver.AddNativeSearchDirectory(nativeDir);
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
    /// Assembly names present in the Trusted Platform Assemblies list, built lazily on
    /// first access.  Packages whose primary assembly is already in the TPA can be
    /// skipped because the runtime will resolve them from the shared framework.
    /// </summary>
    private static readonly Lazy<HashSet<string>> TpaAssemblyNames = new(BuildTpaAssemblyNames);

    private static HashSet<string> BuildTpaAssemblyNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (tpa is not null)
        {
            var separator = OperatingSystem.IsWindows() ? ';' : ':';
            foreach (var path in tpa.Split(separator))
                set.Add(Path.GetFileNameWithoutExtension(path));
        }
        return set;
    }

    /// <summary>
    /// Returns <c>true</c> if the package is part of the .NET shared framework and
    /// should not be downloaded (already available at runtime).
    /// <para>
    /// Core runtime packages (<c>Microsoft.NETCore.*</c>, <c>NETStandard.*</c>) are
    /// always skipped.  For <c>System.*</c> and <c>Microsoft.Extensions.*</c> packages,
    /// we check the TPA list so they are correctly downloaded when running in a plain
    /// console host (where they are NOT in the shared framework) but skipped in ASP.NET
    /// Core hosts (where they are).
    /// </para>
    /// </summary>
    private static bool IsFrameworkPackage(string packageId)
    {
        // Always skip core runtime meta-packages
        if (packageId.StartsWith("Microsoft.NETCore.", StringComparison.OrdinalIgnoreCase) ||
            packageId.StartsWith("NETStandard.", StringComparison.OrdinalIgnoreCase) ||
            packageId.Equals("NETStandard.Library", StringComparison.OrdinalIgnoreCase))
            return true;

        // For System.* and Microsoft.Extensions.* packages, only skip if the assembly
        // is already present in the TPA (i.e. the shared framework provides it).
        if (packageId.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
            packageId.StartsWith("Microsoft.Extensions.", StringComparison.OrdinalIgnoreCase))
            return TpaAssemblyNames.Value.Contains(packageId);

        return false;
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
