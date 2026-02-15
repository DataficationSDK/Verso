using System.Data.Common;
using Verso.Ado.Helpers;

namespace Verso.Ado.Tests.Helpers;

[TestClass]
public sealed class ProviderDiscoveryTests
{
    [TestMethod]
    public void Discover_SqliteConnectionString_IdentifiesSqlite()
    {
        // Register SQLite provider for the test
        DbProviderFactories.RegisterFactory("Microsoft.Data.Sqlite",
            Microsoft.Data.Sqlite.SqliteFactory.Instance);
        try
        {
            var (factory, providerName, error) = ProviderDiscovery.Discover("Data Source=:memory:");

            Assert.IsNull(error, error);
            Assert.IsNotNull(factory);
            Assert.AreEqual("Microsoft.Data.Sqlite", providerName);
        }
        finally
        {
            DbProviderFactories.UnregisterFactory("Microsoft.Data.Sqlite");
        }
    }

    [TestMethod]
    public void Discover_ExplicitProvider_UsesSpecifiedProvider()
    {
        DbProviderFactories.RegisterFactory("Microsoft.Data.Sqlite",
            Microsoft.Data.Sqlite.SqliteFactory.Instance);
        try
        {
            var (factory, providerName, error) = ProviderDiscovery.Discover(
                "Data Source=test.db",
                explicitProvider: "Microsoft.Data.Sqlite");

            Assert.IsNull(error, error);
            Assert.IsNotNull(factory);
            Assert.AreEqual("Microsoft.Data.Sqlite", providerName);
        }
        finally
        {
            DbProviderFactories.UnregisterFactory("Microsoft.Data.Sqlite");
        }
    }

    [TestMethod]
    public void Discover_UnregisteredExplicitProvider_ReturnsError()
    {
        var (factory, providerName, error) = ProviderDiscovery.Discover(
            "Data Source=test.db",
            explicitProvider: "NonExistent.Provider.That.Does.Not.Exist.Anywhere");

        Assert.IsNotNull(error);
        Assert.IsNull(factory);
    }

    [TestMethod]
    public void Discover_NoProviderAvailable_ReturnsError()
    {
        // With no providers registered and an unrecognizable connection string,
        // we rely on heuristics + registry + scanning all failing
        var (factory, _, error) = ProviderDiscovery.Discover(
            "SomeCustomDriver=value",
            explicitProvider: "Completely.Fake.Provider.ZZZZZ");

        Assert.IsNotNull(error);
        Assert.IsNull(factory);
    }

    [TestMethod]
    public void Discover_WithNuGetAssemblyPaths_LoadsAndFindsProvider()
    {
        // Unregister to ensure DbProviderFactories won't find it
        try { DbProviderFactories.UnregisterFactory("Microsoft.Data.Sqlite"); } catch { }

        // Get the actual assembly path for Microsoft.Data.Sqlite (it's already loaded in the test process,
        // but this validates the nugetAssemblyPaths parameter is accepted and processed)
        var sqliteAssembly = typeof(Microsoft.Data.Sqlite.SqliteFactory).Assembly;
        var assemblyPath = sqliteAssembly.Location;

        var (factory, providerName, error) = ProviderDiscovery.Discover(
            "Data Source=:memory:",
            explicitProvider: "Microsoft.Data.Sqlite",
            nugetAssemblyPaths: new[] { assemblyPath });

        Assert.IsNull(error, error);
        Assert.IsNotNull(factory);
        Assert.AreEqual("Microsoft.Data.Sqlite", providerName);

        // Cleanup auto-registration
        try { DbProviderFactories.UnregisterFactory("Microsoft.Data.Sqlite"); } catch { }
    }

    [TestMethod]
    public void Discover_WithNuGetPaths_NonexistentPaths_DoesNotThrow()
    {
        // Passing nonexistent paths should be handled gracefully
        var (factory, _, error) = ProviderDiscovery.Discover(
            "SomeCustomDriver=value",
            explicitProvider: "Completely.Fake.Provider.ZZZZZ",
            nugetAssemblyPaths: new[] { "/nonexistent/path/Fake.dll", "/another/bad/path.dll" });

        Assert.IsNotNull(error);
        Assert.IsNull(factory);
    }

    [TestMethod]
    public void Discover_WithNuGetPaths_NullPaths_DoesNotThrow()
    {
        // Null paths should be handled gracefully
        DbProviderFactories.RegisterFactory("Microsoft.Data.Sqlite",
            Microsoft.Data.Sqlite.SqliteFactory.Instance);
        try
        {
            var (factory, providerName, error) = ProviderDiscovery.Discover(
                "Data Source=:memory:",
                nugetAssemblyPaths: null);

            Assert.IsNull(error, error);
            Assert.IsNotNull(factory);
        }
        finally
        {
            DbProviderFactories.UnregisterFactory("Microsoft.Data.Sqlite");
        }
    }
}
