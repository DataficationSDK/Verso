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

    /// <summary>
    /// Creates a new isolated load context for a third-party extension assembly.
    /// </summary>
    /// <param name="pluginPath">Full path to the extension's main assembly (.dll).</param>
    public ExtensionLoadContext(string pluginPath)
        : base(name: $"VersoExt:{Path.GetFileNameWithoutExtension(pluginPath)}", isCollectible: true)
    {
        if (!string.IsNullOrEmpty(pluginPath) && File.Exists(pluginPath))
            _resolver = new AssemblyDependencyResolver(pluginPath);

        _abstractionsName = typeof(Verso.Abstractions.IExtension).Assembly.GetName().Name!;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Share Verso.Abstractions from the default context so interface types match.
        if (string.Equals(assemblyName.Name, _abstractionsName, StringComparison.OrdinalIgnoreCase))
            return null; // fall back to default context

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
