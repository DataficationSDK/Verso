using Verso.Ado.Kernel;
using Verso.Ado.Scaffold;

namespace Verso.Ado.Tests.Scaffold;

[TestClass]
public sealed class EfCoreScaffolderTests
{
    private static EfCoreScaffolder CreateScaffolder(
        string connectionName = "testdb",
        string connectionString = "Data Source=test.db",
        string? providerName = "Microsoft.Data.Sqlite",
        List<TableInfo>? tables = null,
        Dictionary<string, List<ColumnInfo>>? columns = null,
        Dictionary<string, List<ForeignKeyInfo>>? foreignKeys = null)
    {
        tables ??= new List<TableInfo>
        {
            new("Products", null, "TABLE"),
        };

        columns ??= new Dictionary<string, List<ColumnInfo>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Products"] = new List<ColumnInfo>
            {
                new("Id", "INTEGER", false, null, true, 0),
                new("Name", "TEXT", false, null, false, 1),
                new("Price", "REAL", true, null, false, 2),
            },
        };

        foreignKeys ??= new Dictionary<string, List<ForeignKeyInfo>>(StringComparer.OrdinalIgnoreCase);

        return new EfCoreScaffolder(connectionName, connectionString, providerName, tables, columns, foreignKeys);
    }

    [TestMethod]
    public void Generate_SingleTable_ProducesEntityClass()
    {
        var scaffolder = CreateScaffolder();
        var result = scaffolder.Generate();

        Assert.AreEqual(1, result.EntityCount);
        Assert.AreEqual("TestdbContext", result.ContextClassName);
        Assert.IsTrue(result.GeneratedCode.Contains("public class Product"));
        Assert.IsTrue(result.GeneratedCode.Contains("public int Id { get; set; }"));
        Assert.IsTrue(result.GeneratedCode.Contains("public string Name { get; set; } = default!;"));
        Assert.IsTrue(result.GeneratedCode.Contains("public float? Price { get; set; }"));
    }

    [TestMethod]
    public void Generate_ConventionalPk_NoKeyAttribute()
    {
        var scaffolder = CreateScaffolder();
        var result = scaffolder.Generate();

        // "Id" is conventional, so no [Key] attribute should appear
        Assert.IsFalse(result.GeneratedCode.Contains("[Key]"));
    }

    [TestMethod]
    public void Generate_NonConventionalPk_HasKeyAttribute()
    {
        var columns = new Dictionary<string, List<ColumnInfo>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Products"] = new List<ColumnInfo>
            {
                new("ProductCode", "varchar", false, null, true, 0),
                new("Name", "TEXT", false, null, false, 1),
            },
        };

        var scaffolder = CreateScaffolder(columns: columns);
        var result = scaffolder.Generate();

        Assert.IsTrue(result.GeneratedCode.Contains("[Key]"));
    }

    [TestMethod]
    public void Generate_NullableColumns_ProducesNullableTypes()
    {
        var scaffolder = CreateScaffolder();
        var result = scaffolder.Generate();

        // Price is nullable REAL -> float?
        Assert.IsTrue(result.GeneratedCode.Contains("float?"));
    }

    [TestMethod]
    public void Generate_ForeignKey_ProducesNavigationProperties()
    {
        var tables = new List<TableInfo>
        {
            new("Orders", null, "TABLE"),
            new("OrderItems", null, "TABLE"),
        };

        var columns = new Dictionary<string, List<ColumnInfo>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Orders"] = new List<ColumnInfo>
            {
                new("Id", "INTEGER", false, null, true, 0),
                new("OrderDate", "datetime", false, null, false, 1),
            },
            ["OrderItems"] = new List<ColumnInfo>
            {
                new("Id", "INTEGER", false, null, true, 0),
                new("OrderId", "INTEGER", false, null, false, 1),
                new("Quantity", "INTEGER", false, null, false, 2),
            },
        };

        var foreignKeys = new Dictionary<string, List<ForeignKeyInfo>>(StringComparer.OrdinalIgnoreCase)
        {
            ["OrderItems"] = new List<ForeignKeyInfo>
            {
                new("FK_OrderItems_Orders", "OrderItems", "OrderId", "Orders", "Id"),
            },
        };

        var scaffolder = CreateScaffolder(tables: tables, columns: columns, foreignKeys: foreignKeys);
        var result = scaffolder.Generate();

        Assert.AreEqual(1, result.RelationshipCount);
        // Dependent side: navigation property with [ForeignKey] attribute
        Assert.IsTrue(result.GeneratedCode.Contains("[ForeignKey(\"OrderId\")]"));
        Assert.IsTrue(result.GeneratedCode.Contains("public virtual Order?"));

        // Principal side: collection navigation
        Assert.IsTrue(result.GeneratedCode.Contains("public virtual ICollection<OrderItem>"));
    }

    [TestMethod]
    public void Generate_DbContext_HasDbSetProperties()
    {
        var scaffolder = CreateScaffolder();
        var result = scaffolder.Generate();

        Assert.IsTrue(result.GeneratedCode.Contains("public DbSet<Product> Products { get; set; }"));
        Assert.IsTrue(result.GeneratedCode.Contains("class TestdbContext : DbContext"));
    }

    [TestMethod]
    public void Generate_ProviderCall_UsesCorrectMethod()
    {
        var scaffolder = CreateScaffolder(providerName: "Microsoft.Data.Sqlite");
        var result = scaffolder.Generate();
        Assert.IsTrue(result.GeneratedCode.Contains("__verso_builder.UseSqlite("));

        var sqlServerScaffolder = CreateScaffolder(providerName: "Microsoft.Data.SqlClient");
        var sqlServerResult = sqlServerScaffolder.Generate();
        Assert.IsTrue(sqlServerResult.GeneratedCode.Contains("__verso_builder.UseSqlServer("));

        var npgsqlScaffolder = CreateScaffolder(providerName: "Npgsql");
        var npgsqlResult = npgsqlScaffolder.Generate();
        Assert.IsTrue(npgsqlResult.GeneratedCode.Contains("__verso_builder.UseNpgsql("));
    }

    [TestMethod]
    public void Generate_InstantiatesContextWithOptions()
    {
        var scaffolder = CreateScaffolder();
        var result = scaffolder.Generate();

        Assert.IsTrue(result.GeneratedCode.Contains("var testdbContext = new TestdbContext(__verso_builder.Options)"));
    }

    [TestMethod]
    public void Generate_RetrievesLiveConnection()
    {
        var scaffolder = CreateScaffolder();
        var result = scaffolder.Generate();

        Assert.IsTrue(result.GeneratedCode.Contains("Variables.Get<System.Data.Common.DbConnection>(\"__verso_scaffold_testdb_connection\")"));
    }

    [TestMethod]
    public void Generate_TablesFilter_ProducesOnlySpecifiedEntities()
    {
        var tables = new List<TableInfo>
        {
            new("Orders", null, "TABLE"),
            new("Products", null, "TABLE"),
        };

        var columns = new Dictionary<string, List<ColumnInfo>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Orders"] = new List<ColumnInfo>
            {
                new("Id", "INTEGER", false, null, true, 0),
            },
            ["Products"] = new List<ColumnInfo>
            {
                new("Id", "INTEGER", false, null, true, 0),
            },
        };

        // Only include Orders in the scaffolder
        var filteredTables = new List<TableInfo> { new("Orders", null, "TABLE") };
        var filteredColumns = new Dictionary<string, List<ColumnInfo>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Orders"] = columns["Orders"],
        };

        var scaffolder = CreateScaffolder(tables: filteredTables, columns: filteredColumns);
        var result = scaffolder.Generate();

        Assert.AreEqual(1, result.EntityCount);
        Assert.IsTrue(result.EntityNames.Contains("Order"));
        Assert.IsFalse(result.GeneratedCode.Contains("class Product"));
    }

    [TestMethod]
    public void Generate_EntityNameId_IsConventionalPk()
    {
        var tables = new List<TableInfo> { new("Orders", null, "TABLE") };
        var columns = new Dictionary<string, List<ColumnInfo>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Orders"] = new List<ColumnInfo>
            {
                new("OrderId", "INTEGER", false, null, true, 0),
                new("Total", "decimal", false, null, false, 1),
            },
        };

        var scaffolder = CreateScaffolder(tables: tables, columns: columns);
        var result = scaffolder.Generate();

        // OrderId on entity Order should be conventional
        Assert.IsFalse(result.GeneratedCode.Contains("[Key]"));
    }

    [TestMethod]
    public void Generate_MultipleTables_EntityNamesCorrect()
    {
        var tables = new List<TableInfo>
        {
            new("Orders", null, "TABLE"),
            new("Products", null, "TABLE"),
            new("Categories", null, "TABLE"),
        };

        var columns = new Dictionary<string, List<ColumnInfo>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Orders"] = new List<ColumnInfo> { new("Id", "INTEGER", false, null, true, 0) },
            ["Products"] = new List<ColumnInfo> { new("Id", "INTEGER", false, null, true, 0) },
            ["Categories"] = new List<ColumnInfo> { new("Id", "INTEGER", false, null, true, 0) },
        };

        var scaffolder = CreateScaffolder(tables: tables, columns: columns);
        var result = scaffolder.Generate();

        Assert.AreEqual(3, result.EntityCount);
        CollectionAssert.Contains(result.EntityNames.ToList(), "Order");
        CollectionAssert.Contains(result.EntityNames.ToList(), "Product");
        CollectionAssert.Contains(result.EntityNames.ToList(), "Category");
    }
}
