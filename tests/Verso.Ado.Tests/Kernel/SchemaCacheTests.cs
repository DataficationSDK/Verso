using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Verso.Ado.Kernel;

namespace Verso.Ado.Tests.Kernel;

/// <summary>
/// Wraps a <see cref="DbConnection"/> with a custom type name so that
/// <see cref="SchemaCache"/> routes to an alternate schema query path.
/// </summary>
internal sealed class FakeFirebirdConnection : DbConnection
{
    private readonly DbConnection _inner;

    public FakeFirebirdConnection(DbConnection inner) => _inner = inner;

    // The type name contains "Firebird", triggering the Firebird code path
    public override string ToString() => "FirebirdSql.Data.FirebirdClient.FbConnection";

    public override string ConnectionString
    {
        get => _inner.ConnectionString;
        set => _inner.ConnectionString = value;
    }
    public override string Database => _inner.Database;
    public override string DataSource => _inner.DataSource;
    public override string ServerVersion => _inner.ServerVersion;
    public override ConnectionState State => _inner.State;

    public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);
    public override void Close() => _inner.Close();
    public override void Open() => _inner.Open();
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        => _inner.BeginTransaction(isolationLevel);
    protected override DbCommand CreateDbCommand() => _inner.CreateCommand();
}

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

    // --- Firebird path tests ---
    // These use a SQLite database populated with Firebird-style RDB$ system
    // tables, accessed through a FakeFirebirdConnection so SchemaCache routes
    // to the Firebird query path.

    private FakeFirebirdConnection CreateFakeFirebirdDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            -- Firebird system catalog tables
            CREATE TABLE ""RDB$RELATIONS"" (
                ""RDB$RELATION_NAME"" TEXT,
                ""RDB$RELATION_TYPE"" INTEGER,
                ""RDB$SYSTEM_FLAG"" INTEGER
            );
            CREATE TABLE ""RDB$RELATION_FIELDS"" (
                ""RDB$FIELD_NAME"" TEXT,
                ""RDB$RELATION_NAME"" TEXT,
                ""RDB$FIELD_SOURCE"" TEXT,
                ""RDB$NULL_FLAG"" INTEGER,
                ""RDB$DEFAULT_SOURCE"" TEXT,
                ""RDB$FIELD_POSITION"" INTEGER
            );
            CREATE TABLE ""RDB$FIELDS"" (
                ""RDB$FIELD_NAME"" TEXT,
                ""RDB$FIELD_TYPE"" INTEGER
            );
            CREATE TABLE ""RDB$TYPES"" (
                ""RDB$TYPE_NAME"" TEXT,
                ""RDB$TYPE"" INTEGER,
                ""RDB$FIELD_NAME"" TEXT
            );
            CREATE TABLE ""RDB$RELATION_CONSTRAINTS"" (
                ""RDB$CONSTRAINT_NAME"" TEXT,
                ""RDB$CONSTRAINT_TYPE"" TEXT,
                ""RDB$RELATION_NAME"" TEXT,
                ""RDB$INDEX_NAME"" TEXT
            );
            CREATE TABLE ""RDB$INDEX_SEGMENTS"" (
                ""RDB$INDEX_NAME"" TEXT,
                ""RDB$FIELD_NAME"" TEXT,
                ""RDB$FIELD_POSITION"" INTEGER
            );
            CREATE TABLE ""RDB$REF_CONSTRAINTS"" (
                ""RDB$CONSTRAINT_NAME"" TEXT,
                ""RDB$CONST_NAME_UQ"" TEXT
            );
            CREATE TABLE ""RDB$INDICES"" (
                ""RDB$INDEX_NAME"" TEXT,
                ""RDB$RELATION_NAME"" TEXT
            );

            -- Populate: two user tables and one view
            INSERT INTO ""RDB$RELATIONS"" VALUES ('CUSTOMERS', 0, 0);
            INSERT INTO ""RDB$RELATIONS"" VALUES ('ORDERS', 0, 0);
            INSERT INTO ""RDB$RELATIONS"" VALUES ('CUSTOMER_SUMMARY', 1, 0);
            INSERT INTO ""RDB$RELATIONS"" VALUES ('RDB$DATABASE', 0, 1); -- system table, should be skipped

            -- Field type mappings
            INSERT INTO ""RDB$FIELDS"" VALUES ('FIELD_INT', 8);
            INSERT INTO ""RDB$FIELDS"" VALUES ('FIELD_VARCHAR', 37);
            INSERT INTO ""RDB$TYPES"" VALUES ('BIGINT', 8, 'RDB$FIELD_TYPE');
            INSERT INTO ""RDB$TYPES"" VALUES ('VARCHAR', 37, 'RDB$FIELD_TYPE');

            -- CUSTOMERS columns
            INSERT INTO ""RDB$RELATION_FIELDS"" VALUES ('ID', 'CUSTOMERS', 'FIELD_INT', 1, NULL, 0);
            INSERT INTO ""RDB$RELATION_FIELDS"" VALUES ('NAME', 'CUSTOMERS', 'FIELD_VARCHAR', 1, NULL, 1);
            INSERT INTO ""RDB$RELATION_FIELDS"" VALUES ('EMAIL', 'CUSTOMERS', 'FIELD_VARCHAR', NULL, NULL, 2);

            -- ORDERS columns
            INSERT INTO ""RDB$RELATION_FIELDS"" VALUES ('ORDER_ID', 'ORDERS', 'FIELD_INT', 1, NULL, 0);
            INSERT INTO ""RDB$RELATION_FIELDS"" VALUES ('CUSTOMER_ID', 'ORDERS', 'FIELD_INT', 1, NULL, 1);
            INSERT INTO ""RDB$RELATION_FIELDS"" VALUES ('TOTAL', 'ORDERS', 'FIELD_INT', NULL, NULL, 2);

            -- Primary keys
            INSERT INTO ""RDB$RELATION_CONSTRAINTS"" VALUES ('PK_CUSTOMERS', 'PRIMARY KEY', 'CUSTOMERS', 'IDX_PK_CUSTOMERS');
            INSERT INTO ""RDB$INDEX_SEGMENTS"" VALUES ('IDX_PK_CUSTOMERS', 'ID', 0);

            INSERT INTO ""RDB$RELATION_CONSTRAINTS"" VALUES ('PK_ORDERS', 'PRIMARY KEY', 'ORDERS', 'IDX_PK_ORDERS');
            INSERT INTO ""RDB$INDEX_SEGMENTS"" VALUES ('IDX_PK_ORDERS', 'ORDER_ID', 0);

            -- Foreign key: ORDERS.CUSTOMER_ID -> CUSTOMERS.ID
            INSERT INTO ""RDB$RELATION_CONSTRAINTS"" VALUES ('FK_ORDERS_CUSTOMER', 'FOREIGN KEY', 'ORDERS', 'IDX_FK_ORDERS_CUST');
            INSERT INTO ""RDB$INDEX_SEGMENTS"" VALUES ('IDX_FK_ORDERS_CUST', 'CUSTOMER_ID', 0);
            INSERT INTO ""RDB$REF_CONSTRAINTS"" VALUES ('FK_ORDERS_CUSTOMER', 'PK_CUSTOMERS');
            INSERT INTO ""RDB$INDICES"" VALUES ('IDX_PK_CUSTOMERS', 'CUSTOMERS');
        ";
        cmd.ExecuteNonQuery();

        return new FakeFirebirdConnection(_connection);
    }

    [TestMethod]
    public async Task GetOrRefreshAsync_Firebird_PopulatesTables()
    {
        var conn = CreateFakeFirebirdDatabase();
        var cache = new SchemaCache();

        var entry = await cache.GetOrRefreshAsync("fb-test", conn);

        Assert.IsNotNull(entry);
        Assert.AreEqual(3, entry.Tables.Count, "Should include 2 tables + 1 view, excluding system tables");
        Assert.IsTrue(entry.Tables.Any(t => t.Name == "CUSTOMERS" && t.TableType == "TABLE"));
        Assert.IsTrue(entry.Tables.Any(t => t.Name == "ORDERS" && t.TableType == "TABLE"));
        Assert.IsTrue(entry.Tables.Any(t => t.Name == "CUSTOMER_SUMMARY" && t.TableType == "VIEW"));
    }

    [TestMethod]
    public async Task GetOrRefreshAsync_Firebird_PopulatesColumns()
    {
        var conn = CreateFakeFirebirdDatabase();
        var cache = new SchemaCache();

        var entry = await cache.GetOrRefreshAsync("fb-test", conn);

        Assert.IsTrue(entry.Columns.ContainsKey("CUSTOMERS"));
        var cols = entry.Columns["CUSTOMERS"];
        Assert.AreEqual(3, cols.Count);
        Assert.IsTrue(cols.Any(c => c.Name == "ID" && c.IsPrimaryKey && !c.IsNullable));
        Assert.IsTrue(cols.Any(c => c.Name == "NAME" && !c.IsNullable));
        Assert.IsTrue(cols.Any(c => c.Name == "EMAIL" && c.IsNullable));
    }

    [TestMethod]
    public async Task GetOrRefreshAsync_Firebird_PopulatesColumnTypes()
    {
        var conn = CreateFakeFirebirdDatabase();
        var cache = new SchemaCache();

        var entry = await cache.GetOrRefreshAsync("fb-test", conn);

        var cols = entry.Columns["CUSTOMERS"];
        Assert.IsTrue(cols.Any(c => c.Name == "ID" && c.DataType == "BIGINT"));
        Assert.IsTrue(cols.Any(c => c.Name == "NAME" && c.DataType == "VARCHAR"));
    }

    [TestMethod]
    public async Task GetOrRefreshAsync_Firebird_PopulatesForeignKeys()
    {
        var conn = CreateFakeFirebirdDatabase();
        var cache = new SchemaCache();

        var entry = await cache.GetOrRefreshAsync("fb-test", conn);

        Assert.IsTrue(entry.ForeignKeys.ContainsKey("ORDERS"));
        var fks = entry.ForeignKeys["ORDERS"];
        Assert.AreEqual(1, fks.Count);
        Assert.AreEqual("FK_ORDERS_CUSTOMER", fks[0].ConstraintName);
        Assert.AreEqual("CUSTOMER_ID", fks[0].FromColumn);
        Assert.AreEqual("CUSTOMERS", fks[0].ToTable);
        Assert.AreEqual("ID", fks[0].ToColumn);
    }
}
