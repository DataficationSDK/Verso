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
}
