namespace Verso.Ado.Scaffold;

/// <summary>
/// Checks whether the required EF Core assemblies are loaded in the current AppDomain.
/// </summary>
internal static class EfCorePrerequisiteChecker
{
    private static readonly Dictionary<string, (string Package, string Assembly)> ProviderMap
        = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.Data.Sqlite"] = ("Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite"),
        ["Microsoft.Data.SqlClient"] = ("Microsoft.EntityFrameworkCore.SqlServer", "Microsoft.EntityFrameworkCore.SqlServer"),
        ["Npgsql"] = ("Npgsql.EntityFrameworkCore.PostgreSQL", "Npgsql.EntityFrameworkCore.PostgreSQL"),
        ["MySql.Data.MySqlClient"] = ("Pomelo.EntityFrameworkCore.MySql", "Pomelo.EntityFrameworkCore.MySql"),
        ["MySqlConnector"] = ("Pomelo.EntityFrameworkCore.MySql", "Pomelo.EntityFrameworkCore.MySql"),
    };

    /// <summary>
    /// Returns #r directives for installing the required EF Core packages for a given provider.
    /// Used as a hint in error messages when compilation fails.
    /// </summary>
    internal static string GetInstallHint(string? providerName)
    {
        var hint = "  #r \"nuget: Microsoft.EntityFrameworkCore\"\n";

        if (providerName is not null && ProviderMap.TryGetValue(providerName, out var info))
        {
            hint += $"  #r \"nuget: {info.Package}\"\n";
        }

        return hint;
    }
}
