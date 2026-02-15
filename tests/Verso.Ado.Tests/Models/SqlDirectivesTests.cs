using Verso.Ado.Models;

namespace Verso.Ado.Tests.Models;

[TestClass]
public sealed class SqlDirectivesTests
{
    [TestMethod]
    public void Parse_WithAllDirectives_ParsesCorrectly()
    {
        var code = "--connection northwind --name salesData --no-display --page-size 100\nSELECT * FROM Orders";

        var (directives, remaining) = SqlDirectives.Parse(code);

        Assert.AreEqual("northwind", directives.ConnectionName);
        Assert.AreEqual("salesData", directives.VariableName);
        Assert.IsTrue(directives.NoDisplay);
        Assert.AreEqual(100, directives.PageSize);
        Assert.AreEqual("SELECT * FROM Orders", remaining);
    }

    [TestMethod]
    public void Parse_WithNoDirectives_ReturnsDefaults()
    {
        var code = "SELECT * FROM Orders";

        var (directives, remaining) = SqlDirectives.Parse(code);

        Assert.IsNull(directives.ConnectionName);
        Assert.IsNull(directives.VariableName);
        Assert.IsFalse(directives.NoDisplay);
        Assert.IsNull(directives.PageSize);
        Assert.AreEqual(code, remaining);
    }

    [TestMethod]
    public void Parse_ConnectionOnly_ParsesCorrectly()
    {
        var code = "--connection mydb\nSELECT 1";

        var (directives, remaining) = SqlDirectives.Parse(code);

        Assert.AreEqual("mydb", directives.ConnectionName);
        Assert.IsNull(directives.VariableName);
        Assert.AreEqual("SELECT 1", remaining);
    }

    [TestMethod]
    public void Parse_EmptyCode_ReturnsDefaults()
    {
        var (directives, remaining) = SqlDirectives.Parse("");

        Assert.IsNull(directives.ConnectionName);
        Assert.AreEqual("", remaining);
    }

    [TestMethod]
    public void Parse_RegularSqlComment_NotTreatedAsDirective()
    {
        var code = "-- This is a regular comment\nSELECT 1";

        var (directives, remaining) = SqlDirectives.Parse(code);

        Assert.IsNull(directives.ConnectionName);
        Assert.AreEqual(code, remaining);
    }

    [TestMethod]
    public void Parse_DirectiveOnly_EmptyRemainingCode()
    {
        var code = "--connection mydb --name result";

        var (directives, remaining) = SqlDirectives.Parse(code);

        Assert.AreEqual("mydb", directives.ConnectionName);
        Assert.AreEqual("result", directives.VariableName);
        Assert.AreEqual(string.Empty, remaining);
    }

    [TestMethod]
    public void Parse_MultiLineCode_PreservesRemainingLines()
    {
        var code = "--connection mydb\nSELECT *\nFROM Orders\nWHERE Id > 1";

        var (directives, remaining) = SqlDirectives.Parse(code);

        Assert.AreEqual("mydb", directives.ConnectionName);
        Assert.AreEqual("SELECT *\nFROM Orders\nWHERE Id > 1", remaining);
    }
}
