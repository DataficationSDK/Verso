using System.Data.Common;
using System.Reflection;

namespace Verso.Ado.Helpers;

/// <summary>
/// Discovers a <see cref="DbProviderFactory"/> for a given connection string using heuristics,
/// the DbProviderFactories registry, and assembly scanning.
/// </summary>
internal static class ProviderDiscovery
{
    private static readonly (string Keyword, string ProviderName)[] Heuristics =
    {
        ("Data Source=:memory:", "Microsoft.Data.Sqlite"),
        (".db", "Microsoft.Data.Sqlite"),
        ("Server=", "Microsoft.Data.SqlClient"),
        ("Data Source=", "Microsoft.Data.SqlClient"), // fallback — also matches SQLite but checked after
        ("Host=", "Npgsql"),
        ("Port=5432", "Npgsql"),
        ("SslMode=", "Npgsql"),
        ("Server=localhost;Port=3306", "MySql.Data.MySqlClient"),
        ("Uid=", "MySql.Data.MySqlClient"),
    };

    /// <summary>
    /// Maps provider invariant names to their <see cref="DbProviderFactory"/> type names.
    /// Used for targeted type resolution that avoids <c>assembly.GetTypes()</c> (which can
    /// throw <see cref="ReflectionTypeLoadException"/> when transitive dependencies are missing).
    /// </summary>
    private static readonly Dictionary<string, string> WellKnownFactoryTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft.Data.Sqlite"] = "Microsoft.Data.Sqlite.SqliteFactory",
            ["Microsoft.Data.SqlClient"] = "Microsoft.Data.SqlClient.SqlClientFactory",
            ["Npgsql"] = "Npgsql.NpgsqlFactory",
            ["MySql.Data.MySqlClient"] = "MySql.Data.MySqlClient.MySqlClientFactory",
            ["MySqlConnector"] = "MySqlConnector.MySqlConnectorFactory",
            ["Oracle.ManagedDataAccess.Client"] = "Oracle.ManagedDataAccess.Client.OracleClientFactory",
        };

    /// <summary>
    /// Attempts to discover a <see cref="DbProviderFactory"/> for the given connection string.
    /// If <paramref name="explicitProvider"/> is specified, it is used directly.
    /// </summary>
    /// <param name="connectionString">The ADO.NET connection string.</param>
    /// <param name="explicitProvider">Optional explicit provider invariant name.</param>
    /// <param name="nugetAssemblyPaths">Optional list of NuGet-resolved assembly paths to search.</param>
    internal static (DbProviderFactory? Factory, string? ProviderName, string? ErrorMessage) Discover(
        string connectionString,
        string? explicitProvider = null,
        IReadOnlyList<string>? nugetAssemblyPaths = null)
    {
        // 1. Explicit provider
        if (!string.IsNullOrWhiteSpace(explicitProvider))
        {
            var factory = TryGetFactory(explicitProvider, nugetAssemblyPaths);
            if (factory is not null)
                return (factory, explicitProvider, null);

            var nugetDetail = nugetAssemblyPaths is { Count: > 0 }
                ? $" ({nugetAssemblyPaths.Count} NuGet assembly path(s) searched)"
                : " (no NuGet assembly paths available — was #r \"nuget:\" run first?)";
            return (null, null, $"Provider '{explicitProvider}' is not registered.{nugetDetail} " +
                "Ensure the provider NuGet package is referenced and DbProviderFactories.RegisterFactory() has been called.");
        }

        // 2. Connection string heuristics
        var guessedProvider = GuessProviderFromConnectionString(connectionString);
        if (guessedProvider is not null)
        {
            var factory = TryGetFactory(guessedProvider, nugetAssemblyPaths);
            if (factory is not null)
                return (factory, guessedProvider, null);
        }

        // 3. DbProviderFactories registry
        var registered = GetRegisteredFactories();
        if (registered.Count == 1)
            return (registered[0].Factory, registered[0].ProviderName, null);

        if (registered.Count > 1)
        {
            var names = string.Join(", ", registered.Select(r => r.ProviderName));
            return (null, null, $"Multiple database providers are registered ({names}). " +
                "Use --provider to specify which one to use.");
        }

        // 4. Broad assembly scanning (last resort)
        EnsureNuGetAssembliesLoaded(nugetAssemblyPaths);
        var scanned = ScanAssembliesForFactories();
        if (scanned.Count == 1)
            return (scanned[0].Factory, scanned[0].TypeName, null);

        if (scanned.Count > 1)
        {
            var names = string.Join(", ", scanned.Select(s => s.TypeName));
            return (null, null, $"Multiple database providers found in loaded assemblies ({names}). " +
                "Use --provider to specify which one to use.");
        }

        return (null, null, "No database provider found. Install a provider NuGet package " +
            "(e.g., Microsoft.Data.Sqlite, Microsoft.Data.SqlClient) and use --provider to specify it.");
    }

    private static string? GuessProviderFromConnectionString(string connectionString)
    {
        foreach (var (keyword, provider) in Heuristics)
        {
            if (connectionString.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return provider;
        }
        return null;
    }

    private static DbProviderFactory? TryGetFactory(string providerName, IReadOnlyList<string>? nugetPaths)
    {
        // 1. DbProviderFactories registry (fastest)
        try
        {
            if (DbProviderFactories.TryGetFactory(providerName, out var factory))
                return factory;
        }
        catch
        {
            // Swallow — factory not registered
        }

        // 2. Targeted loading from NuGet paths — load the specific assembly and resolve
        //    the factory type by name (avoids GetTypes() and ReflectionTypeLoadException)
        var fromNuGet = TryLoadFactoryFromNuGetPaths(providerName, nugetPaths);
        if (fromNuGet is not null)
            return fromNuGet;

        // 3. Scan already-loaded assemblies as fallback
        return ScanForSpecificFactory(providerName);
    }

    /// <summary>
    /// Finds the provider assembly in the NuGet paths, loads it, and resolves the factory
    /// type directly by name. This avoids <c>assembly.GetTypes()</c> which throws
    /// <see cref="ReflectionTypeLoadException"/> when transitive dependencies (e.g. SQLitePCLRaw)
    /// are not on the probing path.
    /// </summary>
    private static DbProviderFactory? TryLoadFactoryFromNuGetPaths(
        string providerName, IReadOnlyList<string>? paths)
    {
        if (paths is null or { Count: 0 }) return null;

        // Find the assembly file matching the provider name
        var assemblyPath = paths.FirstOrDefault(p =>
            Path.GetFileNameWithoutExtension(p)?
                .Equals(providerName, StringComparison.OrdinalIgnoreCase) == true);

        if (assemblyPath is null || !File.Exists(assemblyPath)) return null;

        try
        {
            var assembly = Assembly.LoadFrom(assemblyPath);

            // If we know the factory type name, resolve it directly (no GetTypes() needed)
            if (WellKnownFactoryTypes.TryGetValue(providerName, out var factoryTypeName))
            {
                var type = assembly.GetType(factoryTypeName);
                if (type is not null)
                {
                    var field = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                    if (field?.GetValue(null) is DbProviderFactory factory)
                    {
                        try { DbProviderFactories.RegisterFactory(providerName, factory); } catch { }
                        return factory;
                    }
                }
            }

            // Unknown provider — fall back to scanning loadable types in this specific assembly
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (type.IsSubclassOf(typeof(DbProviderFactory)) &&
                    !type.IsAbstract &&
                    (type.FullName?.Contains(providerName, StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    var field = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                    if (field?.GetValue(null) is DbProviderFactory factory)
                    {
                        try { DbProviderFactories.RegisterFactory(providerName, factory); } catch { }
                        return factory;
                    }
                }
            }
        }
        catch
        {
            // Assembly couldn't be loaded at all
        }

        return null;
    }

    private static DbProviderFactory? ScanForSpecificFactory(string providerName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var type in GetLoadableTypes(assembly))
                {
                    if (type.IsSubclassOf(typeof(DbProviderFactory)) &&
                        !type.IsAbstract &&
                        (type.FullName?.Contains(providerName, StringComparison.OrdinalIgnoreCase) ?? false))
                    {
                        var instanceField = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                        if (instanceField?.GetValue(null) is DbProviderFactory factory)
                        {
                            try { DbProviderFactories.RegisterFactory(providerName, factory); } catch { }
                            return factory;
                        }
                    }
                }
            }
            catch
            {
                // Skip assemblies that can't be reflected at all
            }
        }
        return null;
    }

    /// <summary>
    /// Loads NuGet-resolved assemblies into the AppDomain so they are visible to
    /// <see cref="ScanAssembliesForFactories"/> (the broad, no-provider-name path).
    /// </summary>
    private static void EnsureNuGetAssembliesLoaded(IReadOnlyList<string>? paths)
    {
        if (paths is null or { Count: 0 }) return;

        var loadedNames = new HashSet<string>(
            AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetName().Name ?? ""),
            StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            if (!File.Exists(path) || !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;

            var name = Path.GetFileNameWithoutExtension(path);
            if (loadedNames.Contains(name))
                continue;

            try
            {
                Assembly.LoadFrom(path);
                loadedNames.Add(name);
            }
            catch
            {
                // Skip assemblies that can't be loaded (native DLLs, etc.)
            }
        }
    }

    private static List<(DbProviderFactory Factory, string ProviderName)> GetRegisteredFactories()
    {
        var result = new List<(DbProviderFactory, string)>();
        try
        {
            var table = DbProviderFactories.GetFactoryClasses();
            foreach (System.Data.DataRow row in table.Rows)
            {
                var invariantName = row["InvariantName"]?.ToString();
                if (invariantName is not null &&
                    DbProviderFactories.TryGetFactory(invariantName, out var factory))
                {
                    result.Add((factory, invariantName));
                }
            }
        }
        catch
        {
            // DbProviderFactories may not be available
        }
        return result;
    }

    private static List<(DbProviderFactory Factory, string TypeName)> ScanAssembliesForFactories()
    {
        var result = new List<(DbProviderFactory, string)>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var type in GetLoadableTypes(assembly))
                {
                    if (type.IsSubclassOf(typeof(DbProviderFactory)) && !type.IsAbstract)
                    {
                        var instanceField = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                        if (instanceField?.GetValue(null) is DbProviderFactory factory)
                        {
                            result.Add((factory, type.FullName ?? type.Name));
                        }
                    }
                }
            }
            catch
            {
                // Skip assemblies that can't be reflected at all
            }
        }
        return result;
    }

    /// <summary>
    /// Returns all types from an assembly that can be loaded, gracefully handling
    /// <see cref="ReflectionTypeLoadException"/> for assemblies with missing dependencies.
    /// </summary>
    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).ToArray()!;
        }
    }
}
