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
}
