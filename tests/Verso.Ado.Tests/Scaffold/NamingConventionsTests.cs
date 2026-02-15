using Verso.Ado.Scaffold;

namespace Verso.Ado.Tests.Scaffold;

[TestClass]
public sealed class NamingConventionsTests
{
    [TestMethod]
    [DataRow("order_items", "OrderItems")]
    [DataRow("order-items", "OrderItems")]
    [DataRow("orderItems", "OrderItems")]
    [DataRow("OrderItems", "OrderItems")]
    [DataRow("ORDERS", "ORDERS")]
    [DataRow("my_table_name", "MyTableName")]
    public void ToPascalCase_ConvertsCorrectly(string input, string expected)
    {
        Assert.AreEqual(expected, NamingConventions.ToPascalCase(input));
    }

    [TestMethod]
    [DataRow("Orders", "Order")]
    [DataRow("Products", "Product")]
    [DataRow("Categories", "Category")]
    [DataRow("Addresses", "Address")]
    [DataRow("Boxes", "Box")]
    [DataRow("Watches", "Watch")]
    [DataRow("Buses", "Bus")]
    [DataRow("Status", "Status")] // ends in "us", no change
    [DataRow("Analysis", "Analysis")] // ends in "is", no change
    [DataRow("Order", "Order")] // already singular
    public void Singularize_BasicRules(string input, string expected)
    {
        Assert.AreEqual(expected, NamingConventions.Singularize(input));
    }

    [TestMethod]
    [DataRow("Orders", "Order")]
    [DataRow("tbl_orders", "Order")]
    [DataRow("tblOrders", "Order")]
    [DataRow("order_items", "OrderItem")]
    [DataRow("Products", "Product")]
    public void ToEntityClassName_ConvertsCorrectly(string tableName, string expected)
    {
        Assert.AreEqual(expected, NamingConventions.ToEntityClassName(tableName));
    }

    [TestMethod]
    [DataRow("order_id", "OrderId")]
    [DataRow("Name", "Name")]
    [DataRow("class", "@Class")]
    [DataRow("namespace", "@Namespace")]
    [DataRow("string", "@String")]
    public void ToPropertyName_ConvertsCorrectly(string columnName, string expected)
    {
        Assert.AreEqual(expected, NamingConventions.ToPropertyName(columnName));
    }

    [TestMethod]
    [DataRow("mydb", "MydbContext")]
    [DataRow("my_database", "MyDatabaseContext")]
    [DataRow("TestDb", "TestDbContext")]
    public void ToContextClassName_ConvertsCorrectly(string connectionName, string expected)
    {
        Assert.AreEqual(expected, NamingConventions.ToContextClassName(connectionName));
    }

    [TestMethod]
    public void IsCSharpKeyword_ReturnsTrue_ForKeywords()
    {
        Assert.IsTrue(NamingConventions.IsCSharpKeyword("class"));
        Assert.IsTrue(NamingConventions.IsCSharpKeyword("int"));
        Assert.IsTrue(NamingConventions.IsCSharpKeyword("namespace"));
        Assert.IsTrue(NamingConventions.IsCSharpKeyword("string"));
    }

    [TestMethod]
    public void IsCSharpKeyword_ReturnsFalse_ForNonKeywords()
    {
        Assert.IsFalse(NamingConventions.IsCSharpKeyword("Order"));
        Assert.IsFalse(NamingConventions.IsCSharpKeyword("Name"));
        Assert.IsFalse(NamingConventions.IsCSharpKeyword("MyClass"));
    }
}
