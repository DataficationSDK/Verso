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
        Assert.IsNull(directives.CommandTimeout);
        Assert.AreEqual(code, remaining);
    }

    [TestMethod]
    public void Parse_WithTimeout_ParsesSeconds()
    {
        var code = "--connection Primary --timeout 120\nBACKUP DATABASE master TO DISK = 'x.bak'";

        var (directives, remaining) = SqlDirectives.Parse(code);

        Assert.AreEqual("Primary", directives.ConnectionName);
        Assert.AreEqual(120, directives.CommandTimeout);
        Assert.AreEqual("BACKUP DATABASE master TO DISK = 'x.bak'", remaining);
    }

    [TestMethod]
    public void Parse_TimeoutZero_MeansNoLimit()
    {
        var code = "--timeout 0\nWAITFOR DELAY '00:01:00'";

        var (directives, _) = SqlDirectives.Parse(code);

        Assert.AreEqual(0, directives.CommandTimeout);
    }

    [TestMethod]
    public void Parse_NegativeTimeout_IsIgnored()
    {
        var code = "--timeout -5\nSELECT 1";

        var (directives, _) = SqlDirectives.Parse(code);

        Assert.IsNull(directives.CommandTimeout);
    }

    [TestMethod]
    public void Parse_NonIntegerTimeout_IsIgnored()
    {
        var code = "--timeout abc\nSELECT 1";

        var (directives, _) = SqlDirectives.Parse(code);

        Assert.IsNull(directives.CommandTimeout);
    }

    [TestMethod]
    public void DetectMisusedDirective_TimeoutWithSpace_ReturnsHint()
    {
        Assert.IsNotNull(SqlDirectives.DetectMisusedDirective("-- timeout 120\nSELECT 1"));
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

    [TestMethod]
    public void Parse_DirectiveWithSpaceAfterDashes_TreatedAsComment()
    {
        // A space after "--" makes it a comment, not a directive — the connection
        // is silently ignored. (DetectMisusedDirective surfaces a hint for this.)
        var code = "-- connection Tertiary\nSELECT @@SERVERNAME";

        var (directives, remaining) = SqlDirectives.Parse(code);

        Assert.IsNull(directives.ConnectionName);
        Assert.AreEqual(code, remaining);
    }

    [TestMethod]
    public void DetectMisusedDirective_SpaceAfterDashes_ReturnsHint()
    {
        var hint = SqlDirectives.DetectMisusedDirective("-- connection Tertiary\nSELECT 1");

        Assert.IsNotNull(hint);
        Assert.IsTrue(hint!.Contains("--connection"));
    }

    [TestMethod]
    public void DetectMisusedDirective_OtherKeys_ReturnHint()
    {
        Assert.IsNotNull(SqlDirectives.DetectMisusedDirective("-- name result\nSELECT 1"));
        Assert.IsNotNull(SqlDirectives.DetectMisusedDirective("-- no-display\nSELECT 1"));
        Assert.IsNotNull(SqlDirectives.DetectMisusedDirective("-- page-size 50\nSELECT 1"));
    }

    [TestMethod]
    public void DetectMisusedDirective_CorrectDirective_ReturnsNull()
    {
        Assert.IsNull(SqlDirectives.DetectMisusedDirective("--connection Tertiary\nSELECT 1"));
    }

    [TestMethod]
    public void DetectMisusedDirective_OrdinaryComment_ReturnsNull()
    {
        // Prose comments and keyword-prefixed comments that are not directive-shaped
        // must not trigger a false hint.
        Assert.IsNull(SqlDirectives.DetectMisusedDirective("-- This is a regular comment\nSELECT 1"));
        Assert.IsNull(SqlDirectives.DetectMisusedDirective("-- name: report query\nSELECT 1"));
        Assert.IsNull(SqlDirectives.DetectMisusedDirective("SELECT 1"));
    }
}
