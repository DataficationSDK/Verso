using System.Data.Common;
using Microsoft.Data.Sqlite;
using Verso.Ado.Kernel;

namespace Verso.Ado.Tests.Kernel;

[TestClass]
public sealed class SchemaCacheTests
{
    private SqliteConnection? _connection;

    [TestInitialize]
    public void Setup()
    {
        DbProviderFactories.RegisterFactory("Microsoft.Data.Sqlite", SqliteFactory.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _connection?.Dispose();
        try { DbProviderFactories.UnregisterFactory("Microsoft.Data.Sqlite"); } catch { }
    }

    private SqliteConnection CreateTestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Price REAL);
            CREATE TABLE Orders (OrderId INTEGER PRIMARY KEY, ProductId INTEGER, Quantity INTEGER);
            CREATE VIEW ProductSummary AS SELECT Name, Price FROM Products;
        ";
        cmd.ExecuteNonQuery();

        return _connection;
    }

    [TestMethod]
    public async Task GetOrRefreshAsync_SQLite_PopulatesTables()
    {
        var conn = CreateTestDatabase();
        var cache = new SchemaCache();

        var entry = await cache.GetOrRefreshAsync("test", conn);

        Assert.IsNotNull(entry);
        Assert.IsTrue(entry.Tables.Count >= 3); // Products, Orders, ProductSummary
        Assert.IsTrue(entry.Tables.Any(t => t.Name == "Products" && t.TableType == "TABLE"));
        Assert.IsTrue(entry.Tables.Any(t => t.Name == "Orders" && t.TableType == "TABLE"));
        Assert.IsTrue(entry.Tables.Any(t => t.Name == "ProductSummary" && t.TableType == "VIEW"));
    }

    [TestMethod]
    public async Task GetOrRefreshAsync_SQLite_PopulatesColumns()
    {
        var conn = CreateTestDatabase();
        var cache = new SchemaCache();

        var entry = await cache.GetOrRefreshAsync("test", conn);

        Assert.IsTrue(entry.Columns.ContainsKey("Products"));
        var productCols = entry.Columns["Products"];
        Assert.IsTrue(productCols.Any(c => c.Name == "Id" && c.IsPrimaryKey));
        Assert.IsTrue(productCols.Any(c => c.Name == "Name" && !c.IsNullable));
        Assert.IsTrue(productCols.Any(c => c.Name == "Price" && c.IsNullable));
    }

    [TestMethod]
    public async Task GetOrRefreshAsync_ReturnsCachedEntry_WithinTTL()
    {
        var conn = CreateTestDatabase();
        var cache = new SchemaCache();

        var entry1 = await cache.GetOrRefreshAsync("test", conn);
        var entry2 = await cache.GetOrRefreshAsync("test", conn);

        Assert.AreSame(entry1, entry2, "Should return the same cached instance within TTL.");
    }

    [TestMethod]
    public async Task GetOrRefreshAsync_RefreshesAfterTTL()
    {
        var conn = CreateTestDatabase();
        var cache = new SchemaCache(ttlSeconds: 0); // Expire immediately

        var entry1 = await cache.GetOrRefreshAsync("test", conn);
        await Task.Delay(50); // Small delay to ensure TTL expires
        var entry2 = await cache.GetOrRefreshAsync("test", conn);

        Assert.AreNotSame(entry1, entry2, "Should return a new entry after TTL expires.");
    }

    [TestMethod]
    public async Task Invalidate_RemovesCachedEntry()
    {
        var conn = CreateTestDatabase();
        var cache = new SchemaCache();

        var entry1 = await cache.GetOrRefreshAsync("test", conn);
        cache.Invalidate("test");
        var entry2 = await cache.GetOrRefreshAsync("test", conn);

        Assert.AreNotSame(entry1, entry2, "Should return a new entry after invalidation.");
    }

    [TestMethod]
    public async Task InvalidateAll_ClearsAllEntries()
    {
        var conn = CreateTestDatabase();
        var cache = new SchemaCache();

        await cache.GetOrRefreshAsync("test1", conn);
        await cache.GetOrRefreshAsync("test2", conn);
        cache.InvalidateAll();

        Assert.IsFalse(cache.TryGetCached("test1", out _));
        Assert.IsFalse(cache.TryGetCached("test2", out _));
    }

    [TestMethod]
    public async Task ConcurrentAccess_DoesNotThrow()
    {
        var conn = CreateTestDatabase();
        var cache = new SchemaCache();

        var tasks = Enumerable.Range(0, 10).Select(i =>
            cache.GetOrRefreshAsync($"conn{i % 3}", conn));

        var results = await Task.WhenAll(tasks);

        Assert.IsTrue(results.All(r => r is not null));
    }
}
