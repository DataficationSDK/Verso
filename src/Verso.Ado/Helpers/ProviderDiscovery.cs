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
    /// Attempts to discover a <see cref="DbProviderFactory"/> for the given connection string.
    /// If <paramref name="explicitProvider"/> is specified, it is used directly.
    /// </summary>
    internal static (DbProviderFactory? Factory, string? ProviderName, string? ErrorMessage) Discover(
        string connectionString,
        string? explicitProvider = null)
    {
        // 1. Explicit provider
        if (!string.IsNullOrWhiteSpace(explicitProvider))
        {
            var factory = TryGetFactory(explicitProvider);
            if (factory is not null)
                return (factory, explicitProvider, null);

            return (null, null, $"Provider '{explicitProvider}' is not registered. " +
                "Ensure the provider NuGet package is referenced and DbProviderFactories.RegisterFactory() has been called.");
        }

        // 2. Connection string heuristics
        var guessedProvider = GuessProviderFromConnectionString(connectionString);
        if (guessedProvider is not null)
        {
            var factory = TryGetFactory(guessedProvider);
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

        // 4. Assembly scanning
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

    private static DbProviderFactory? TryGetFactory(string providerName)
    {
        try
        {
            if (DbProviderFactories.TryGetFactory(providerName, out var factory))
                return factory;
        }
        catch
        {
            // Swallow — factory not registered
        }

        // Try assembly scanning as fallback for the specific provider
        return ScanForSpecificFactory(providerName);
    }

    private static DbProviderFactory? ScanForSpecificFactory(string providerName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsSubclassOf(typeof(DbProviderFactory)) &&
                        !type.IsAbstract &&
                        (type.FullName?.Contains(providerName, StringComparison.OrdinalIgnoreCase) ?? false))
                    {
                        var instanceField = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                        if (instanceField?.GetValue(null) is DbProviderFactory factory)
                            return factory;
                    }
                }
            }
            catch
            {
                // Skip assemblies that can't be reflected
            }
        }
        return null;
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
                foreach (var type in assembly.GetTypes())
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
                // Skip assemblies that can't be reflected
            }
        }
        return result;
    }
}
