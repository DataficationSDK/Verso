using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Verso.Kernels;

/// <summary>
/// Resolves managed and native libraries from NuGet package extraction directories.
/// Registers handlers on <see cref="AssemblyLoadContext.Default"/> so that:
/// <list type="bullet">
///   <item>Managed assemblies (e.g. <c>SQLitePCLRaw.core.dll</c>) loaded from one NuGet
///     package directory can find dependencies in other NuGet package directories.</item>
///   <item>Native libraries (e.g. <c>e_sqlite3</c>) extracted from <c>runtimes/{rid}/native/</c>
///     can be found at runtime.</item>
/// </list>
/// </summary>
internal static class NuGetRuntimeResolver
{
    private static readonly object Lock = new();
    private static readonly List<string> NativeSearchDirs = new();
    private static readonly List<string> ManagedSearchDirs = new();
    private static bool _registered;

    /// <summary>
    /// Private ALC used as a fallback when loading a managed NuGet assembly into the
    /// default ALC fails because the host's TPA already contains a different version
    /// of the same simple-named assembly. The runtime accepts cross-ALC Assembly
    /// objects returned from the <see cref="AssemblyLoadContext.Resolving"/> event,
    /// so the caller still gets a binding for the strict-version request.
    /// </summary>
    private static AssemblyLoadContext? _isolationContext;
    private static AssemblyLoadContext IsolationContext =>
        _isolationContext ??= new AssemblyLoadContext("VersoNuGetIsolation", isCollectible: false);

    /// <summary>
    /// Adds a directory containing managed assemblies (DLLs from <c>lib/</c>) to the search path.
    /// </summary>
    internal static void AddManagedSearchDirectory(string directory)
    {
        lock (Lock)
        {
            if (!ManagedSearchDirs.Contains(directory, StringComparer.OrdinalIgnoreCase))
                ManagedSearchDirs.Add(directory);

            EnsureRegistered();
        }
    }

    /// <summary>
    /// Adds a directory containing native libraries (from <c>runtimes/{rid}/native/</c>) to the search path.
    /// </summary>
    internal static void AddNativeSearchDirectory(string directory)
    {
        lock (Lock)
        {
            if (!NativeSearchDirs.Contains(directory, StringComparer.OrdinalIgnoreCase))
                NativeSearchDirs.Add(directory);

            EnsureRegistered();
        }
    }

    private static void EnsureRegistered()
    {
        if (_registered) return;
        _registered = true;

        AssemblyLoadContext.Default.Resolving += OnResolvingManagedAssembly;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += OnResolvingUnmanagedDll;
    }

    // --- Managed assembly resolution ---

    private static Assembly? OnResolvingManagedAssembly(AssemblyLoadContext context, AssemblyName name)
    {
        string[] dirs;
        lock (Lock)
        {
            dirs = ManagedSearchDirs.ToArray();
        }

        var fileName = $"{name.Name}.dll";
        var candidates = new List<string>();
        foreach (var dir in dirs)
        {
            var path = Path.Combine(dir, fileName);
            if (File.Exists(path))
                candidates.Add(path);
        }

        if (candidates.Count == 0)
            return null;

        // Prefer a candidate whose immediate parent directory matches the requested
        // major.minor.patch — NuGet package directories follow the convention
        // .../{packageId}/{version}/{file}.dll, so the parent dir name IS the package
        // version. Without this preference, returning the first match (potentially an
        // older version) makes the runtime binder reject the load.
        if (name.Version is not null)
        {
            var versionStr = name.Version.ToString(3);
            candidates = candidates
                .OrderByDescending(p =>
                    string.Equals(
                        Path.GetFileName(Path.GetDirectoryName(p)),
                        versionStr,
                        StringComparison.Ordinal) ? 1 : 0)
                .ToList();
        }

        // First try to load into the requesting context (typically the default ALC).
        // If every candidate hits a FileLoadException with HRESULT 0x80131040 — "the
        // located assembly's manifest definition does not match the assembly reference"
        // — that means the host's TPA carries a different version of the same simple
        // name. The default ALC refuses to hold both, but the runtime accepts cross-ALC
        // Assembly objects returned from a Resolving event, so we fall back to a private
        // isolation ALC. This unblocks packages whose strict-version requirement exceeds
        // what the host's TPA happens to carry (e.g. SqlClient binding to
        // System.Configuration.ConfigurationManager 9.0.0.0 when TPA has 8.0.0.0).
        var sawTpaConflict = false;
        foreach (var candidate in candidates)
        {
            try { return context.LoadFromAssemblyPath(candidate); }
            catch (FileLoadException) { sawTpaConflict = true; }
            catch { /* try next */ }
        }

        if (sawTpaConflict)
        {
            foreach (var candidate in candidates)
            {
                try { return IsolationContext.LoadFromAssemblyPath(candidate); }
                catch { /* try next */ }
            }
        }

        return null;
    }

    // --- Native library resolution ---

    private static IntPtr OnResolvingUnmanagedDll(Assembly assembly, string libraryName)
    {
        string[] dirs;
        lock (Lock)
        {
            dirs = NativeSearchDirs.ToArray();
        }

        // Prefer a directory belonging to the same package version as the assembly making
        // the call. Native asset packages ship in lockstep with the managed package that
        // P/Invokes into them (SkiaSharp 3.119.0 alongside SkiaSharp.NativeAssets.* 3.119.0),
        // and a managed library that loads a native of the wrong version typically refuses to
        // initialize. Search directories accumulate for the life of the process, so a session
        // that referenced two versions of the same package would otherwise hand the first
        // registered native to whichever version asked for it. Ordering is stable, so
        // directories that do not match keep their existing relative order and remain
        // reachable as a fallback.
        var requestedVersion = GetPackageVersion(assembly);
        if (requestedVersion is not null)
        {
            dirs = dirs
                .OrderByDescending(dir => BelongsToPackageVersion(dir, requestedVersion) ? 1 : 0)
                .ToArray();
        }

        foreach (var dir in dirs)
        {
            foreach (var candidate in GetCandidateNames(libraryName))
            {
                var path = Path.Combine(dir, candidate);
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
                    return handle;
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Returns the package version of the NuGet directory an assembly was loaded from, or
    /// <see langword="null"/> when it did not come from one. Package directories follow the
    /// convention <c>.../{packageId}/{version}/{file}.dll</c>, so the immediate parent
    /// directory name is the version.
    /// </summary>
    private static string? GetPackageVersion(Assembly assembly)
    {
        string location;
        try
        {
            // Throws for assemblies with no backing file, and returns empty for some others.
            location = assembly.Location;
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrEmpty(location))
            return null;

        var directory = Path.GetDirectoryName(location);
        return string.IsNullOrEmpty(directory) ? null : Path.GetFileName(directory);
    }

    /// <summary>
    /// Returns whether a native search directory sits under the given package version.
    /// Native libraries are extracted to <c>.../{packageId}/{version}/native/</c>.
    /// </summary>
    internal static bool BelongsToPackageVersion(string nativeDir, string version)
    {
        var trimmed = nativeDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(trimmed);
        return !string.IsNullOrEmpty(parent)
            && string.Equals(Path.GetFileName(parent), version, StringComparison.Ordinal);
    }

    /// <summary>
    /// Generates platform-appropriate file name candidates for a native library.
    /// For example, given "e_sqlite3" on macOS, yields: e_sqlite3.dylib, libe_sqlite3.dylib, e_sqlite3.
    /// </summary>
    private static IEnumerable<string> GetCandidateNames(string libraryName)
    {
        // If the caller already provided an extension, try it as-is first
        if (HasNativeExtension(libraryName))
        {
            yield return libraryName;
            yield return Path.GetFileNameWithoutExtension(libraryName);
        }

        if (OperatingSystem.IsMacOS())
        {
            yield return $"{libraryName}.dylib";
            yield return $"lib{libraryName}.dylib";
            yield return libraryName;
        }
        else if (OperatingSystem.IsLinux())
        {
            yield return $"{libraryName}.so";
            yield return $"lib{libraryName}.so";
            yield return libraryName;
        }
        else if (OperatingSystem.IsWindows())
        {
            yield return $"{libraryName}.dll";
            yield return libraryName;
        }
        else
        {
            yield return libraryName;
        }
    }

    private static bool HasNativeExtension(string name) =>
        name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".so", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns an ordered list of runtime identifiers to search in NuGet packages,
    /// from most specific to least specific for the current platform.
    /// </summary>
    internal static string[] GetRidFallbacks()
    {
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64"
        };

        if (OperatingSystem.IsMacOS())
            return new[] { $"osx-{arch}", "osx", "unix" };

        if (OperatingSystem.IsLinux())
            return new[] { $"linux-{arch}", "linux", "unix" };

        if (OperatingSystem.IsWindows())
            return new[] { $"win-{arch}", "win" };

        return new[] { $"unix-{arch}", "unix" };
    }
}
