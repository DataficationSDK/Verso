using Verso.Ado.Helpers;

namespace Verso.Ado.Tests.Helpers;

[TestClass]
public sealed class SqlLocalScopeAnalyzerTests
{
    [TestMethod]
    public void FindLocalNames_DeclareSimple_Found()
    {
        var names = SqlLocalScopeAnalyzer.FindLocalNames(
            "DECLARE @x INT", SqlDialect.SqlServer);

        Assert.IsTrue(names.Contains("x"));
        Assert.AreEqual(1, names.Count);
    }

    [TestMethod]
    public void FindLocalNames_DeclareMultiVar_AllFound()
    {
        var names = SqlLocalScopeAnalyzer.FindLocalNames(
            "DECLARE @a INT = 1, @b VARCHAR(10) = 'x', @c SYSNAME = @@SERVERNAME;",
            SqlDialect.SqlServer);

        Assert.IsTrue(names.Contains("a"));
        Assert.IsTrue(names.Contains("b"));
        Assert.IsTrue(names.Contains("c"));
        Assert.AreEqual(3, names.Count);
    }

    [TestMethod]
    public void FindLocalNames_DeclareTable_Found()
    {
        var names = SqlLocalScopeAnalyzer.FindLocalNames(
            "DECLARE @t TABLE (Id INT, Name VARCHAR(50))", SqlDialect.SqlServer);

        Assert.IsTrue(names.Contains("t"));
    }

    [TestMethod]
    public void FindLocalNames_DeclareCursor_Found()
    {
        var names = SqlLocalScopeAnalyzer.FindLocalNames(
            "DECLARE @c CURSOR FOR SELECT 1", SqlDialect.SqlServer);

        Assert.IsTrue(names.Contains("c"));
    }

    [TestMethod]
    public void FindLocalNames_CreateProcedureParams_Found()
    {
        var names = SqlLocalScopeAnalyzer.FindLocalNames(
            "CREATE PROCEDURE dbo.MyProc @p1 INT, @p2 VARCHAR(100) AS BEGIN SELECT 1 END",
            SqlDialect.SqlServer);

        Assert.IsTrue(names.Contains("p1"));
        Assert.IsTrue(names.Contains("p2"));
    }

    [TestMethod]
    public void FindLocalNames_AlterProcedureParams_Found()
    {
        var names = SqlLocalScopeAnalyzer.FindLocalNames(
            "ALTER PROCEDURE dbo.MyProc @p1 INT AS BEGIN SELECT 1 END",
            SqlDialect.SqlServer);

        Assert.IsTrue(names.Contains("p1"));
    }

    [TestMethod]
    public void FindLocalNames_CreateProcShortKeyword_Found()
    {
        var names = SqlLocalScopeAnalyzer.FindLocalNames(
            "CREATE PROC dbo.MyProc @p INT AS BEGIN SELECT 1 END",
            SqlDialect.SqlServer);

        Assert.IsTrue(names.Contains("p"));
    }

    [TestMethod]
    public void FindLocalNames_CreateFunctionParams_Found()
    {
        var names = SqlLocalScopeAnalyzer.FindLocalNames(
            "CREATE FUNCTION dbo.MyFn (@p INT) RETURNS INT AS BEGIN RETURN @p END",
            SqlDialect.SqlServer);

        Assert.IsTrue(names.Contains("p"));
    }

    [TestMethod]
    public void FindLocalNames_CreateFunctionParamWithNestedParens_Found()
    {
        var names = SqlLocalScopeAnalyzer.FindLocalNames(
            "CREATE FUNCTION dbo.MyFn (@p VARCHAR(50), @q INT) RETURNS INT AS BEGIN RETURN @q END",
            SqlDialect.SqlServer);

        Assert.IsTrue(names.Contains("p"));
        Assert.IsTrue(names.Contains("q"));
    }

    [TestMethod]
    public void FindLocalNames_DeclareInLineComment_NotFound()
    {
        var names = SqlLocalScopeAnalyzer.FindLocalNames(
            "-- DECLARE @hidden INT\nSELECT 1", SqlDialect.SqlServer);

        Assert.AreEqual(0, names.Count);
    }

    [TestMethod]
    public void FindLocalNames_DeclareInBlockComment_NotFound()
    {
        var names = SqlLocalScopeAnalyzer.FindLocalNames(
            "/* DECLARE @hidden INT */ SELECT 1", SqlDialect.SqlServer);

        Assert.AreEqual(0, names.Count);
    }

    [TestMethod]
    public void FindLocalNames_DeclareInsideStringLiteral_NotFound()
    {
        var names = SqlLocalScopeAnalyzer.FindLocalNames(
            "INSERT INTO Logs VALUES ('DECLARE @hidden INT')", SqlDialect.SqlServer);

        Assert.AreEqual(0, names.Count);
    }

    [TestMethod]
    public void FindLocalNames_MySqlSet_Found()
    {
        var names = SqlLocalScopeAnalyzer.FindLocalNames(
            "SET @userVar = 42", SqlDialect.MySql);

        Assert.IsTrue(names.Contains("userVar"));
    }

    [TestMethod]
    public void FindLocalNames_MySqlAssign_Found()
    {
        var names = SqlLocalScopeAnalyzer.FindLocalNames(
            "SELECT @rowNum := @rowNum + 1 FROM t, (SELECT @rowNum := 0) r",
            SqlDialect.MySql);

        Assert.IsTrue(names.Contains("rowNum"));
    }

    [TestMethod]
    public void FindLocalNames_MySqlDialect_DoesNotPickUpTSqlDeclare()
    {
        var names = SqlLocalScopeAnalyzer.FindLocalNames(
            "DECLARE @x INT; SELECT @x", SqlDialect.MySql);

        Assert.AreEqual(0, names.Count);
    }

    [TestMethod]
    public void FindLocalNames_OracleDialect_AlwaysEmpty()
    {
        var names = SqlLocalScopeAnalyzer.FindLocalNames(
            "DECLARE x INT := 1; BEGIN NULL; END;", SqlDialect.Oracle);

        Assert.AreEqual(0, names.Count);
    }

    [TestMethod]
    public void FindLocalNames_PostgresDialect_AlwaysEmpty()
    {
        var names = SqlLocalScopeAnalyzer.FindLocalNames(
            "DECLARE @x INT", SqlDialect.Postgres);

        Assert.AreEqual(0, names.Count);
    }

    [TestMethod]
    public void FindLocalNames_EmptyOrNullInput_Empty()
    {
        Assert.AreEqual(0, SqlLocalScopeAnalyzer.FindLocalNames("", SqlDialect.SqlServer).Count);
        Assert.AreEqual(0, SqlLocalScopeAnalyzer.FindLocalNames(null!, SqlDialect.SqlServer).Count);
    }

    [TestMethod]
    public void FindLocalNames_ReportedUserScenario_AllLocalsFound()
    {
        // The exact shape from the user issue: multi-var DECLARE with one
        // initializer referencing a kernel variable (@waitTime) and a system
        // global (@@SERVERNAME). The kernel variable should not be in the
        // local set (it's used, not introduced); the locals should be.
        var sql = @"
            DECLARE @rows INT = 1,
                    @waitDelay CHAR(8) = '00:' + RIGHT('0' + CAST(@waitTime AS VARCHAR(2)) + '', 2),
                    @serverName SYSNAME = @@SERVERNAME;
            WHILE (@rows > 0)
            BEGIN
                SELECT @rows = @@ROWCOUNT;
            END;";

        var names = SqlLocalScopeAnalyzer.FindLocalNames(sql, SqlDialect.SqlServer);

        Assert.IsTrue(names.Contains("rows"), "Expected 'rows' in local names");
        Assert.IsTrue(names.Contains("waitDelay"), "Expected 'waitDelay' in local names");
        Assert.IsTrue(names.Contains("serverName"), "Expected 'serverName' in local names");
        // @waitTime is REFERENCED from a kernel variable, not declared — must stay out.
        Assert.IsFalse(names.Contains("waitTime"),
            "'waitTime' is a kernel variable reference, not a local declaration");
    }
}
