using Verso.Ado.Helpers;

namespace Verso.Ado.Tests.Helpers;

[TestClass]
public sealed class SqlStatementSplitterTests
{
    [TestMethod]
    public void Split_SingleStatement_ReturnsOne()
    {
        var result = SqlStatementSplitter.Split("SELECT 1");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("SELECT 1", result[0]);
    }

    [TestMethod]
    public void Split_TwoStatements_ReturnsBoth()
    {
        var result = SqlStatementSplitter.Split("SELECT 1; SELECT 2");

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("SELECT 1", result[0]);
        Assert.AreEqual("SELECT 2", result[1]);
    }

    [TestMethod]
    public void Split_SemicolonInsideSingleQuote_Preserved()
    {
        var result = SqlStatementSplitter.Split("SELECT 'hello;world'");

        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(result[0].Contains("hello;world"));
    }

    [TestMethod]
    public void Split_SemicolonInsideDoubleQuote_Preserved()
    {
        var result = SqlStatementSplitter.Split("SELECT \"col;name\" FROM t");

        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(result[0].Contains("col;name"));
    }

    [TestMethod]
    public void Split_SemicolonInsideSingleLineComment_Preserved()
    {
        var result = SqlStatementSplitter.Split("SELECT 1 -- comment with ;\nSELECT 2");

        // The "-- comment with ;" and "SELECT 2" are in the same "statement" since
        // there's no semicolon separator between them
        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public void Split_SemicolonInsideMultiLineComment_Preserved()
    {
        var result = SqlStatementSplitter.Split("SELECT 1 /* comment; with; semicolons */");

        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public void Split_EmptyStatements_Removed()
    {
        var result = SqlStatementSplitter.Split("SELECT 1; ; ; SELECT 2");

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("SELECT 1", result[0]);
        Assert.AreEqual("SELECT 2", result[1]);
    }

    [TestMethod]
    public void Split_EmptyInput_ReturnsEmpty()
    {
        var result = SqlStatementSplitter.Split("");

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Split_WhitespaceOnly_ReturnsEmpty()
    {
        var result = SqlStatementSplitter.Split("   ");

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Split_GoBatchSeparator_SplitsStatements()
    {
        var result = SqlStatementSplitter.Split(
            "SELECT 1\nGO\nSELECT 2", handleGoBatches: true);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("SELECT 1", result[0]);
        Assert.AreEqual("SELECT 2", result[1]);
    }

    [TestMethod]
    public void Split_GoBatchSeparatorDisabled_NotSplit()
    {
        var result = SqlStatementSplitter.Split(
            "SELECT 1\nGO\nSELECT 2", handleGoBatches: false);

        // GO is treated as part of the statement text
        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public void Split_GoInMiddleOfLine_NotSplit()
    {
        var result = SqlStatementSplitter.Split(
            "SELECT GOPHER FROM Animals", handleGoBatches: true);

        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public void Split_EscapedSingleQuotes_Handled()
    {
        var result = SqlStatementSplitter.Split("SELECT 'it''s'; SELECT 2");

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result[0].Contains("it''s"));
    }

    [TestMethod]
    public void Split_TrailingSemicolon_NoEmptyStatement()
    {
        var result = SqlStatementSplitter.Split("SELECT 1;");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("SELECT 1", result[0]);
    }

    [TestMethod]
    public void Split_IfBeginEndBlock_TreatedAsSingleStatement()
    {
        var sql = "IF EXISTS (SELECT 1 FROM sys.databases WHERE name = 'master')\n" +
                  "BEGIN\n" +
                  "    PRINT 'Works';\n" +
                  "END;";

        var result = SqlStatementSplitter.Split(sql);

        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(result[0].Contains("BEGIN"));
        Assert.IsTrue(result[0].Contains("END"));
        Assert.IsTrue(result[0].Contains("PRINT 'Works'"));
    }

    [TestMethod]
    public void Split_NestedBeginEnd_TreatedAsSingleStatement()
    {
        var sql = "IF (1=1)\n" +
                 "BEGIN\n" +
                 "    IF (2=2)\n" +
                 "    BEGIN\n" +
                 "        PRINT 'nested';\n" +
                 "    END;\n" +
                 "END;";

        var result = SqlStatementSplitter.Split(sql);

        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public void Split_TwoIndependentBlocks_SplitAfterOuterEnd()
    {
        var sql = "IF (1=1) BEGIN PRINT 'a'; END;\n" +
                 "SELECT 2";

        var result = SqlStatementSplitter.Split(sql);

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result[0].Contains("PRINT 'a'"));
        Assert.AreEqual("SELECT 2", result[1]);
    }

    [TestMethod]
    public void Split_BeginTryEndTry_NotSplitInside()
    {
        var sql = "BEGIN TRY\n" +
                 "    SELECT 1;\n" +
                 "    SELECT 2;\n" +
                 "END TRY\n" +
                 "BEGIN CATCH\n" +
                 "    PRINT ERROR_MESSAGE();\n" +
                 "END CATCH;";

        var result = SqlStatementSplitter.Split(sql);

        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(result[0].Contains("BEGIN TRY"));
        Assert.IsTrue(result[0].Contains("END CATCH"));
    }

    [TestMethod]
    public void Split_BeginTransaction_DoesNotAffectBlockDepth()
    {
        var sql = "BEGIN TRANSACTION;\n" +
                 "UPDATE t SET x = 1;\n" +
                 "COMMIT;";

        var result = SqlStatementSplitter.Split(sql);

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("BEGIN TRANSACTION", result[0]);
        Assert.IsTrue(result[1].Contains("UPDATE"));
        Assert.AreEqual("COMMIT", result[2]);
    }

    [TestMethod]
    public void Split_BeginTran_DoesNotAffectBlockDepth()
    {
        var sql = "BEGIN TRAN;\nSELECT 1;\nCOMMIT TRAN;";

        var result = SqlStatementSplitter.Split(sql);

        Assert.AreEqual(3, result.Count);
    }

    [TestMethod]
    public void Split_CaseEndInsideBlock_DoesNotCloseBlock()
    {
        var sql = "IF (1=1)\n" +
                 "BEGIN\n" +
                 "    SELECT CASE WHEN 1=1 THEN 'a' ELSE 'b' END AS Val;\n" +
                 "    PRINT 'after case';\n" +
                 "END;";

        var result = SqlStatementSplitter.Split(sql);

        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(result[0].Contains("CASE WHEN"));
        Assert.IsTrue(result[0].Contains("PRINT 'after case'"));
    }

    [TestMethod]
    public void Split_WhileBeginEnd_TreatedAsSingleStatement()
    {
        var sql = "DECLARE @i INT = 0;\n" +
                 "WHILE @i < 3\n" +
                 "BEGIN\n" +
                 "    SET @i = @i + 1;\n" +
                 "END;";

        var result = SqlStatementSplitter.Split(sql);

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result[0].StartsWith("DECLARE"));
        Assert.IsTrue(result[1].StartsWith("WHILE"));
    }

    [TestMethod]
    public void Split_StrayEnd_DoesNotUnderflow()
    {
        var sql = "SELECT 1; END; SELECT 2;";

        var result = SqlStatementSplitter.Split(sql);

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("SELECT 1", result[0]);
        Assert.AreEqual("END", result[1]);
        Assert.AreEqual("SELECT 2", result[2]);
    }

    [TestMethod]
    public void Split_BeginInsideStringLiteral_NotTreatedAsKeyword()
    {
        var sql = "SELECT 'BEGIN'; SELECT 'END';";

        var result = SqlStatementSplitter.Split(sql);

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result[0].Contains("'BEGIN'"));
        Assert.IsTrue(result[1].Contains("'END'"));
    }

    [TestMethod]
    public void Split_BeginningIdentifier_NotTreatedAsBegin()
    {
        var sql = "SELECT BEGINNING FROM t; SELECT 2;";

        var result = SqlStatementSplitter.Split(sql);

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result[0].Contains("BEGINNING"));
        Assert.AreEqual("SELECT 2", result[1]);
    }

    [TestMethod]
    public void Split_LowercaseBeginEnd_RecognizedCaseInsensitive()
    {
        var sql = "if (1=1) begin print 'x'; end;\nselect 2;";

        var result = SqlStatementSplitter.Split(sql);

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result[0].Contains("begin"));
        Assert.AreEqual("select 2", result[1]);
    }

    [TestMethod]
    public void Split_SemicolonSplittingDisabled_KeepsStatementsTogether()
    {
        var result = SqlStatementSplitter.Split(
            "SELECT 1; SELECT 2", splitOnSemicolon: false);

        // With semicolon splitting off the cell stays a single batch.
        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(result[0].Contains("SELECT 1; SELECT 2"));
    }

    [TestMethod]
    public void Split_SemicolonSplittingDisabled_DeclareThenExecStayInOneBatch()
    {
        // The reported bug: a DECLARE terminated by a semicolon was severed from the
        // EXEC that reads its locals, so each ran as its own batch and the variables
        // were out of scope ("Must declare the scalar variable @SP_Add_RetCode").
        // SQL Server is split only on GO, so the whole script must remain one statement.
        var sql =
            "DECLARE @LS_BackupJobId AS UNIQUEIDENTIFIER,\n" +
            "        @SP_Add_RetCode AS INT;\n" +
            "EXEC @SP_Add_RetCode = master.dbo.sp_add_log_shipping_primary_database\n" +
            "        @database = N'db1',\n" +
            "        @backup_job_id = @LS_BackupJobId OUTPUT";

        var result = SqlStatementSplitter.Split(
            sql, handleGoBatches: true, splitOnSemicolon: false);

        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(result[0].Contains("DECLARE"));
        Assert.IsTrue(result[0].Contains("EXEC"));
    }

    [TestMethod]
    public void Split_SemicolonSplittingDisabled_StillSplitsOnGo()
    {
        // GO remains a batch boundary even when semicolons do not split.
        var sql = "DECLARE @x INT = 1; SELECT @x\nGO\nSELECT 2";

        var result = SqlStatementSplitter.Split(
            sql, handleGoBatches: true, splitOnSemicolon: false);

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result[0].Contains("DECLARE @x"));
        Assert.IsTrue(result[0].Contains("SELECT @x"));
        Assert.AreEqual("SELECT 2", result[1]);
    }

    [TestMethod]
    public void Split_SemicolonSplittingEnabledByDefault_Unchanged()
    {
        // The default keeps the historical per-statement behavior.
        var result = SqlStatementSplitter.Split("SELECT 1; SELECT 2");

        Assert.AreEqual(2, result.Count);
    }
}
