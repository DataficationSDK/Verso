using System.IO.Compression;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Verso.FSharp.NuGet;

/// <summary>
/// Result of resolving a NuGet package, including the resolved version and assembly paths.
/// </summary>
internal sealed record FSharpNuGetResolveResult(
    string PackageId,
    string ResolvedVersion,
    IReadOnlyList<string> AssemblyPaths);

/// <summary>
/// Standalone NuGet package resolver for the F# kernel. Downloads packages from nuget.org,
/// extracts DLLs from the preferred TFM folder, resolves transitive dependencies, and
/// caches results in a shared temp directory.
/// <para>
/// This is a simplified port of the core <c>NuGetPackageResolver</c> without native library
/// extraction or <c>NuGetRuntimeResolver</c> integration (FSI handles assembly loading via
/// <c>#r</c> directives).
/// </para>
/// </summary>
internal sealed class NuGetFallbackResolver
{
    internal static readonly string CacheRoot =
        Path.Combine(Path.GetTempPath(), "verso-nuget-packages", $"net{Environment.Version.Major}.0");

    private readonly List<SourceRepository> _sources;

    private static readonly NuGetFramework TargetFramework =
        NuGetFramework.Parse($"net{Environment.Version.Major}.0");

    /// <summary>
    /// Framework preferences for lib/ and typeproviders/ extraction, built from the
    /// running runtime version. Only includes TFMs at or below the current runtime
    /// so we never attempt to load assemblies compiled for a newer runtime.
    /// </summary>
    private static readonly string[] PreferredFrameworks = BuildPreferredFrameworks();

    private static readonly string[] TypeProviderPrefixes = BuildTypeProviderPrefixes();

    private static string[] BuildPreferredFrameworks()
    {
        var runtimeMajor = Environment.Version.Major;
        var frameworks = new List<string>();
        // Descend from current runtime version down to net6.0
        for (var v = runtimeMajor; v >= 6; v--)
            frameworks.Add($"net{v}.0");
        // netstandard fallbacks are always compatible
        frameworks.Add("netstandard2.1");
        frameworks.Add("netstandard2.0");
        return frameworks.ToArray();
    }

    private static string[] BuildTypeProviderPrefixes()
    {
        return PreferredFrameworks
            .Select(tfm => $"typeproviders/fsharp41/{tfm}/")
            .ToArray();
    }

    private const int MaxDependencyDepth = 6;

    public NuGetFallbackResolver()
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

        if (_sources.Count == 0)
            _sources.Add(Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json"));
    }

    /// <summary>
    /// Adds a package source at the highest priority (before NuGet.Config sources).
    /// </summary>
    public void AddSource(string source)
    {
        var trimmed = source.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return;

        if (_sources.Any(s => string.Equals(s.PackageSource.Source, trimmed, StringComparison.OrdinalIgnoreCase)))
            return;

        _sources.Insert(0, Repository.Factory.GetCoreV3(trimmed));
    }

    /// <summary>
    /// Parses a <c>#i</c> source directive value, validating it is a URL, UNC path, or local directory.
    /// </summary>
    internal static string? ParseSourceDirective(string? directive)
    {
        if (string.IsNullOrWhiteSpace(directive))
            return null;

        var source = directive.Trim();

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" or "file")
            return source;

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
    public async Task<FSharpNuGetResolveResult> ResolvePackageAsync(
        string packageId, string? version, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(packageId);

        var allAssemblyPaths = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var resolvedVersion = await ResolveWithDependenciesAsync(
            packageId, version, allAssemblyPaths, visited, depth: 0, ct).ConfigureAwait(false);

        return new FSharpNuGetResolveResult(packageId, resolvedVersion, allAssemblyPaths);
    }

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

    private async Task<(string ResolvedVersion, List<string> AssemblyPaths, List<(string Id, string? MinVersion)> Dependencies)>
        DownloadSinglePackageAsync(string packageId, string? version, CancellationToken ct)
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
                var cachedDlls = GetAllCachedDlls(cachedDir);
                var cachedDeps = ReadCachedDependencies(cachedDepsFile);
                if (cachedDeps is not null)
                    return (parsedVersion.ToString(), new List<string>(cachedDlls), cachedDeps);
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

        // Check cache
        if (Directory.Exists(packageDir))
        {
            var cachedDlls = GetAllCachedDlls(packageDir);
            var cachedDeps = ReadCachedDependencies(depsFile);

            if (cachedDeps is not null)
                return (resolvedVersion.ToString(), new List<string>(cachedDlls), cachedDeps);

            if (cachedDlls.Length > 0)
            {
                var deps = await DownloadAndReadDependenciesAsync(
                    packageId, resolvedVersion, resource, cache, logger, packageDir, ct).ConfigureAwait(false);
                WriteCachedDependencies(depsFile, deps);
                return (resolvedVersion.ToString(), new List<string>(cachedDlls), deps);
            }
        }

        Directory.CreateDirectory(packageDir);

        // Download and extract
        var tempNupkg = Path.Combine(packageDir, $"{packageId}.{resolvedVersion}.nupkg");
        try
        {
            using (var fileStream = File.Create(tempNupkg))
            {
                var downloaded = await resource.CopyNupkgToStreamAsync(
                    packageId, resolvedVersion, fileStream, cache, logger, ct).ConfigureAwait(false);

                if (!downloaded)
                    throw new InvalidOperationException(
                        $"Failed to download package '{packageId}' v{resolvedVersion} from nuget.org.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Unable to download package '{packageId}' v{resolvedVersion}. Check your network connection and try again. ({ex.GetType().Name}: {ex.Message})", ex);
        }

        var assemblyPaths = new List<string>();
        List<(string Id, string? MinVersion)> dependencies;

        using (var reader = new PackageArchiveReader(tempNupkg))
        {
            assemblyPaths = await ExtractDllsAsync(reader, packageDir, ct).ConfigureAwait(false);
            var tpPaths = await ExtractTypeProviderDllsAsync(reader, packageDir, ct).ConfigureAwait(false);
            assemblyPaths.AddRange(tpPaths);
            dependencies = await ReadDependenciesAsync(reader, ct).ConfigureAwait(false);
        }

        WriteCachedDependencies(depsFile, dependencies);

        try { File.Delete(tempNupkg); } catch { /* best effort */ }

        return (resolvedVersion.ToString(), assemblyPaths, dependencies);
    }

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
    /// Extracts F# type provider design-time assemblies from the <c>typeproviders/</c> folder
    /// in a NuGet package. These assemblies are placed alongside the main <c>lib/</c> DLLs so
    /// that FCS can discover them when probing the referencing assembly's directory.
    /// </summary>
    private static async Task<List<string>> ExtractTypeProviderDllsAsync(
        PackageArchiveReader reader, string packageDir, CancellationToken ct)
    {
        var paths = new List<string>();
        var allEntries = reader.GetFiles().ToList();

        // Find the best matching typeproviders folder
        string? selectedPrefix = null;
        foreach (var prefix in TypeProviderPrefixes)
        {
            if (allEntries.Any(e => e.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && e.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                selectedPrefix = prefix;
                break;
            }
        }

        if (selectedPrefix is null)
            return paths;

        // Extract into the same directory as the lib/ DLLs so FCS type provider
        // resolution finds them by probing the referencing assembly's directory
        foreach (var entry in allEntries)
        {
            if (!entry.StartsWith(selectedPrefix, StringComparison.OrdinalIgnoreCase)
                || !entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;

            var zipEntry = reader.GetEntry(entry);
            if (zipEntry is null) continue;

            var fileName = Path.GetFileName(entry);
            var destPath = Path.Combine(packageDir, fileName);

            // Do not overwrite a lib/ DLL with a type provider DLL of the same name
            if (File.Exists(destPath)) continue;

            using var entryStream = zipEntry.Open();
            using var destStream = File.Create(destPath);
            await entryStream.CopyToAsync(destStream, ct).ConfigureAwait(false);

            paths.Add(destPath);
        }

        return paths;
    }

    private static async Task<List<(string Id, string? MinVersion)>> ReadDependenciesAsync(
        PackageArchiveReader reader, CancellationToken ct)
    {
        var depGroups = (await reader.GetPackageDependenciesAsync(ct).ConfigureAwait(false)).ToList();
        if (depGroups.Count == 0)
            return new List<(string, string?)>();

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

    private static bool IsFrameworkPackage(string packageId)
    {
        if (packageId.StartsWith("Microsoft.NETCore.", StringComparison.OrdinalIgnoreCase) ||
            packageId.StartsWith("NETStandard.", StringComparison.OrdinalIgnoreCase) ||
            packageId.Equals("NETStandard.Library", StringComparison.OrdinalIgnoreCase))
            return true;

        if (packageId.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
            packageId.StartsWith("Microsoft.Extensions.", StringComparison.OrdinalIgnoreCase))
            return TpaAssemblyNames.Value.Contains(packageId);

        return false;
    }

    /// <summary>
    /// Returns all cached DLLs from a package directory, including type provider assemblies
    /// extracted alongside the main lib/ DLLs.
    /// </summary>
    private static string[] GetAllCachedDlls(string packageDir)
    {
        return Directory.GetFiles(packageDir, "*.dll");
    }

    private static void WriteCachedDependencies(string depsFile, List<(string Id, string? MinVersion)> deps)
    {
        try
        {
            var lines = deps.Select(d => d.MinVersion is not null ? $"{d.Id}|{d.MinVersion}" : d.Id);
            File.WriteAllLines(depsFile, lines);
        }
        catch { /* best effort */ }
    }

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
