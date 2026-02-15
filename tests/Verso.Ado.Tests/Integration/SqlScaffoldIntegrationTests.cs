using System.Data.Common;
using Microsoft.Data.Sqlite;
using Verso.Ado.Kernel;
using Verso.Ado.Scaffold;

namespace Verso.Ado.Tests.Integration;

[TestClass]
public sealed class SqlScaffoldIntegrationTests
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
            CREATE TABLE Categories (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL
            );
            CREATE TABLE Products (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                Price REAL,
                CategoryId INTEGER,
                FOREIGN KEY (CategoryId) REFERENCES Categories(Id)
            );
            CREATE TABLE Orders (
                Id INTEGER PRIMARY KEY,
                OrderDate TEXT NOT NULL,
                Total REAL
            );
            CREATE TABLE OrderItems (
                Id INTEGER PRIMARY KEY,
                OrderId INTEGER NOT NULL,
                ProductId INTEGER NOT NULL,
                Quantity INTEGER NOT NULL,
                FOREIGN KEY (OrderId) REFERENCES Orders(Id),
                FOREIGN KEY (ProductId) REFERENCES Products(Id)
            );
        ";
        cmd.ExecuteNonQuery();

        return _connection;
    }

    [TestMethod]
    public async Task SchemaCache_LoadsForeignKeys()
    {
        var conn = CreateTestDatabase();
        var cache = new SchemaCache();

        var entry = await cache.GetOrRefreshAsync("test", conn);

        // Products has FK to Categories
        Assert.IsTrue(entry.ForeignKeys.ContainsKey("Products"));
        var productFks = entry.ForeignKeys["Products"];
        Assert.AreEqual(1, productFks.Count);
        Assert.AreEqual("Categories", productFks[0].ToTable);
        Assert.AreEqual("CategoryId", productFks[0].FromColumn);

        // OrderItems has FKs to Orders and Products
        Assert.IsTrue(entry.ForeignKeys.ContainsKey("OrderItems"));
        var orderItemFks = entry.ForeignKeys["OrderItems"];
        Assert.AreEqual(2, orderItemFks.Count);
    }

    [TestMethod]
    public async Task SchemaCache_TableWithNoFks_EmptyList()
    {
        var conn = CreateTestDatabase();
        var cache = new SchemaCache();

        var entry = await cache.GetOrRefreshAsync("test", conn);

        // Categories and Orders have no outgoing FKs
        Assert.IsFalse(entry.ForeignKeys.ContainsKey("Categories"));
        Assert.IsFalse(entry.ForeignKeys.ContainsKey("Orders"));
    }

    [TestMethod]
    public async Task EfCoreScaffolder_WithForeignKeys_GeneratesNavigationProperties()
    {
        var conn = CreateTestDatabase();
        var cache = new SchemaCache();

        var entry = await cache.GetOrRefreshAsync("test", conn);

        var tables = entry.Tables.Where(t => t.TableType == "TABLE").ToList();

        var scaffolder = new EfCoreScaffolder(
            "testdb",
            "Data Source=:memory:",
            "Microsoft.Data.Sqlite",
            tables,
            entry.Columns,
            entry.ForeignKeys);

        var result = scaffolder.Generate();

        // Should have all 4 entities
        Assert.AreEqual(4, result.EntityCount);
        Assert.AreEqual("TestdbContext", result.ContextClassName);

        // Should have entity classes
        Assert.IsTrue(result.GeneratedCode.Contains("public class Category"));
        Assert.IsTrue(result.GeneratedCode.Contains("public class Product"));
        Assert.IsTrue(result.GeneratedCode.Contains("public class Order"));
        Assert.IsTrue(result.GeneratedCode.Contains("public class OrderItem"));

        // Should have DbContext
        Assert.IsTrue(result.GeneratedCode.Contains("class TestdbContext : DbContext"));
        Assert.IsTrue(result.GeneratedCode.Contains("DbSet<Product>"));
        Assert.IsTrue(result.GeneratedCode.Contains("DbSet<Order>"));

        // Should have FK relationships
        Assert.IsTrue(result.RelationshipCount > 0);
        Assert.IsTrue(result.GeneratedCode.Contains("[ForeignKey("));
        Assert.IsTrue(result.GeneratedCode.Contains("UseSqlite("));

        // Should instantiate context with options from live connection
        Assert.IsTrue(result.GeneratedCode.Contains("var testdbContext = new TestdbContext(__verso_builder.Options)"));
    }

    [TestMethod]
    public async Task EfCoreScaffolder_GeneratedCode_ContainsUsings()
    {
        var conn = CreateTestDatabase();
        var cache = new SchemaCache();
        var entry = await cache.GetOrRefreshAsync("test", conn);

        var tables = entry.Tables.Where(t => t.TableType == "TABLE").ToList();
        var scaffolder = new EfCoreScaffolder(
            "testdb", "Data Source=:memory:", "Microsoft.Data.Sqlite",
            tables, entry.Columns, entry.ForeignKeys);

        var result = scaffolder.Generate();

        Assert.IsTrue(result.GeneratedCode.Contains("using System;"));
        Assert.IsTrue(result.GeneratedCode.Contains("using Microsoft.EntityFrameworkCore;"));
        Assert.IsTrue(result.GeneratedCode.Contains("using System.ComponentModel.DataAnnotations;"));
        Assert.IsTrue(result.GeneratedCode.Contains("using System.ComponentModel.DataAnnotations.Schema;"));
    }

    [TestMethod]
    public async Task EfCoreScaffolder_MultipleFksOnSameTable_AllCaptured()
    {
        var conn = CreateTestDatabase();
        var cache = new SchemaCache();
        var entry = await cache.GetOrRefreshAsync("test", conn);

        // OrderItems should have 2 FKs (OrderId -> Orders, ProductId -> Products)
        Assert.IsTrue(entry.ForeignKeys.ContainsKey("OrderItems"));
        Assert.AreEqual(2, entry.ForeignKeys["OrderItems"].Count);
    }
}
