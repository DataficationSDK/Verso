using System.Reflection;
using System.Runtime.Loader;

namespace Verso.Extensions;

/// <summary>
/// Isolated <see cref="AssemblyLoadContext"/> for third-party extensions.
/// Collectible, shares Verso.Abstractions types from the default context to maintain
/// interface type identity, and uses <see cref="AssemblyDependencyResolver"/> for
/// extension-local dependencies.
/// </summary>
internal sealed class ExtensionLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver? _resolver;
    private readonly string _abstractionsName;
    private readonly Assembly _abstractionsAssembly;

    /// <summary>
    /// Creates a new isolated load context for a third-party extension assembly.
    /// </summary>
    /// <param name="pluginPath">Full path to the extension's main assembly (.dll).</param>
    public ExtensionLoadContext(string pluginPath)
        : base(name: $"VersoExt:{Path.GetFileNameWithoutExtension(pluginPath)}", isCollectible: true)
    {
        if (!string.IsNullOrEmpty(pluginPath) && File.Exists(pluginPath))
            _resolver = new AssemblyDependencyResolver(pluginPath);

        _abstractionsAssembly = typeof(Verso.Abstractions.IExtension).Assembly;
        _abstractionsName = _abstractionsAssembly.GetName().Name!;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Return the host's Verso.Abstractions directly so interface types match,
        // regardless of the version the extension was compiled against. Returning
        // null here would delegate to the default context, which may fail if the
        // requested version differs from the host's version.
        if (string.Equals(assemblyName.Name, _abstractionsName, StringComparison.OrdinalIgnoreCase))
            return _abstractionsAssembly;

        if (_resolver is not null)
        {
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            if (path is not null)
                return LoadFromAssemblyPath(path);
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        if (_resolver is not null)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (path is not null)
                return LoadUnmanagedDllFromPath(path);
        }

        return IntPtr.Zero;
    }
}
