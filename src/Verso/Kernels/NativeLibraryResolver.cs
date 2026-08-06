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
/// Assemblies loaded out of a package directory are additionally given a
/// <see cref="DllImportResolver"/>, which runs ahead of the runtime's own probing, so a
/// package reaches the native library it shipped with rather than a same-named one that
/// happens to sit somewhere the probe looks first.
/// </summary>
internal static class NuGetRuntimeResolver
{
    private static readonly object Lock = new();

    // Published as arrays rather than read from a list under the lock. Every handler in
    // this class runs while the runtime is part way through a load, and taking a lock on
    // that thread invites a deadlock; a snapshot is all any of them needs. Writers hold
    // the lock and swap in a new array, readers take whatever they see.
    private static volatile string[] _nativeSearchDirs = Array.Empty<string>();
    private static volatile string[] _managedSearchDirs = Array.Empty<string>();
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
            if (!_managedSearchDirs.Contains(directory, StringComparer.OrdinalIgnoreCase))
                _managedSearchDirs = Append(_managedSearchDirs, directory);

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
            if (!_nativeSearchDirs.Contains(directory, StringComparer.OrdinalIgnoreCase))
                _nativeSearchDirs = Append(_nativeSearchDirs, directory);

            EnsureRegistered();
        }
    }

    private static string[] Append(string[] existing, string directory)
    {
        var extended = new string[existing.Length + 1];
        Array.Copy(existing, extended, existing.Length);
        extended[^1] = directory;
        return extended;
    }

    private static void EnsureRegistered()
    {
        if (_registered) return;
        _registered = true;

        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        AssemblyLoadContext.Default.Resolving += OnResolvingManagedAssembly;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += OnResolvingUnmanagedDll;
    }

    // --- Managed assembly resolution ---

    private static Assembly? OnResolvingManagedAssembly(AssemblyLoadContext context, AssemblyName name)
    {
        var dirs = _managedSearchDirs;

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

    /// <summary>
    /// Gives an assembly that came out of a package directory first refusal on the native
    /// libraries belonging to its own package version, ahead of the runtime's own probing.
    /// </summary>
    /// <remarks>
    /// The handler below only runs once probing has failed, which is too late when the
    /// probe succeeds with the wrong file. On Windows it hands the name to
    /// <c>LoadLibrary</c>, which searches <c>PATH</c>, so an unrelated program that put a
    /// same-named library there is found first and the package's own copy is never
    /// reached. The managed side then initialises against a stranger's binary, which
    /// SkiaSharp and others refuse to do when the versions disagree. A resolver attached
    /// to the assembly is the only hook that runs ahead of that probe.
    /// </remarks>
    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        try
        {
            var assembly = args.LoadedAssembly;
            if (!IsFromPackageDirectory(assembly))
                return;

            NativeLibrary.SetDllImportResolver(assembly, ResolvePackageNative);
        }
        catch
        {
            // Nothing here is worth failing somebody else's assembly load over, and an
            // assembly that already carries a resolver keeps the one it has.
        }
    }

    /// <summary>
    /// Resolves a native library for an assembly loaded from a package directory, before
    /// the runtime's default probing gets a chance to find a same-named file elsewhere.
    /// </summary>
    /// <remarks>
    /// The claim is deliberately narrow. Once attached, a resolver is consulted for every
    /// P/Invoke the assembly makes, including ones bound for the operating system's own
    /// libraries, so this answers only for directories belonging to the same package
    /// version as the caller and returns zero for everything else. Returning zero falls
    /// through to normal probing, so nothing outside that case changes. Anything wider
    /// would trade a library hijacked from <c>PATH</c> for one hijacked from the package
    /// cache, which is the same bug with a shorter reach.
    /// </remarks>
    private static IntPtr ResolvePackageNative(
        string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        var version = GetPackageVersion(assembly);
        if (version is null)
            return IntPtr.Zero;

        var directory = AssemblyDirectory(assembly);
        var dirs = _nativeSearchDirs;

        // A package that ships its own natives answers for itself first. Otherwise any
        // directory of the same version will do, which is how a managed package reaches
        // the separate native asset package it ships in lockstep with.
        if (directory is not null && TryLoadFrom(Path.Combine(directory, "native"), libraryName, out var own))
            return own;

        foreach (var dir in dirs)
        {
            if (BelongsToPackageVersion(dir, version) && TryLoadFrom(dir, libraryName, out var handle))
                return handle;
        }

        return IntPtr.Zero;
    }

    private static IntPtr OnResolvingUnmanagedDll(Assembly assembly, string libraryName)
    {
        var dirs = _nativeSearchDirs;

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
            if (TryLoadFrom(dir, libraryName, out var handle))
                return handle;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Loads a native library from a directory, trying each name the platform might have
    /// given it.
    /// </summary>
    private static bool TryLoadFrom(string directory, string libraryName, out IntPtr handle)
    {
        foreach (var candidate in GetCandidateNames(libraryName))
        {
            var path = Path.Combine(directory, candidate);
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out handle))
                return true;
        }

        handle = IntPtr.Zero;
        return false;
    }

    /// <summary>
    /// Whether an assembly was loaded from one of the registered package directories.
    /// Only those are given a resolver: this speaks for packages the session extracted,
    /// and for nothing else in the process.
    /// </summary>
    private static bool IsFromPackageDirectory(Assembly assembly)
    {
        var directory = AssemblyDirectory(assembly);
        return directory is not null && IsRegisteredDirectory(_managedSearchDirs, directory);
    }

    /// <summary>
    /// Whether a directory is one of the registered package directories. Compared as whole
    /// directories rather than by prefix, since a prefix would also claim a sibling whose
    /// name merely starts with a registered one, such as a prerelease of the same version.
    /// </summary>
    internal static bool IsRegisteredDirectory(string[] registered, string directory)
    {
        var normalized = Normalize(directory);
        foreach (var dir in registered)
        {
            if (string.Equals(Normalize(dir), normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The directory an assembly was loaded from, or <see langword="null"/> for one with
    /// no backing file.
    /// </summary>
    private static string? AssemblyDirectory(Assembly assembly)
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
        return string.IsNullOrEmpty(directory) ? null : Normalize(directory);
    }

    private static string Normalize(string directory) =>
        directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>
    /// Returns the package version of the NuGet directory an assembly was loaded from, or
    /// <see langword="null"/> when it did not come from one. Package directories follow the
    /// convention <c>.../{packageId}/{version}/{file}.dll</c>, so the immediate parent
    /// directory name is the version.
    /// </summary>
    private static string? GetPackageVersion(Assembly assembly)
    {
        var directory = AssemblyDirectory(assembly);
        return directory is null ? null : Path.GetFileName(directory);
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
