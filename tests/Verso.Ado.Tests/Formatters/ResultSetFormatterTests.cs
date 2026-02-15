using System.Data;
using Verso.Ado.Formatters;
using Verso.Ado.Models;
using Verso.Ado.Tests.Helpers;

namespace Verso.Ado.Tests.Formatters;

[TestClass]
public sealed class ResultSetFormatterTests
{
    private readonly ResultSetFormatter _formatter = new();
    private readonly StubFormatterContext _context = new();

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.ado.formatter.resultset", _formatter.ExtensionId);

    [TestMethod]
    public void Priority_IsThirty()
        => Assert.AreEqual(30, _formatter.Priority);

    [TestMethod]
    public void SupportedTypes_ContainsDataTableAndSqlResultSet()
    {
        Assert.IsTrue(_formatter.SupportedTypes.Contains(typeof(DataTable)));
        Assert.IsTrue(_formatter.SupportedTypes.Contains(typeof(SqlResultSet)));
    }

    [TestMethod]
    public void CanFormat_DataTable_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(new DataTable(), _context));

    [TestMethod]
    public void CanFormat_SqlResultSet_ReturnsTrue()
    {
        var rs = new SqlResultSet(
            new List<SqlColumnMetadata> { new("Id", "INTEGER", typeof(int), false) },
            new List<object?[]> { new object?[] { 1 } },
            1, false);
        Assert.IsTrue(_formatter.CanFormat(rs, _context));
    }

    [TestMethod]
    public void CanFormat_String_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat("hello", _context));

    [TestMethod]
    public async Task FormatAsync_DataTable_ReturnsHtmlTable()
    {
        var dt = new DataTable();
        dt.Columns.Add("Name", typeof(string));
        dt.Columns.Add("Age", typeof(int));
        dt.Rows.Add("Alice", 30);
        dt.Rows.Add("Bob", 25);

        var result = await _formatter.FormatAsync(dt, _context);
        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("<table>"));
        Assert.IsTrue(result.Content.Contains("Name"));
        Assert.IsTrue(result.Content.Contains("Age"));
        Assert.IsTrue(result.Content.Contains("Alice"));
        Assert.IsTrue(result.Content.Contains("Bob"));
    }

    [TestMethod]
    public async Task FormatAsync_SqlResultSet_RendersTypeTooltips()
    {
        var rs = new SqlResultSet(
            new List<SqlColumnMetadata>
            {
                new("Id", "INTEGER", typeof(int), false),
                new("Name", "TEXT", typeof(string), true)
            },
            new List<object?[]>
            {
                new object?[] { 1, "Test" }
            },
            1, false);

        var result = await _formatter.FormatAsync(rs, _context);
        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("title=\"INTEGER\""));
        Assert.IsTrue(result.Content.Contains("title=\"TEXT\""));
    }

    [TestMethod]
    public async Task FormatAsync_NullValues_RendersNullSpan()
    {
        var rs = new SqlResultSet(
            new List<SqlColumnMetadata> { new("Value", "TEXT", typeof(string), true) },
            new List<object?[]> { new object?[] { null } },
            1, false);

        var result = await _formatter.FormatAsync(rs, _context);
        Assert.IsTrue(result.Content.Contains("verso-sql-null"));
        Assert.IsTrue(result.Content.Contains("NULL"));
    }

    [TestMethod]
    public async Task FormatAsync_HtmlEntities_AreEncoded()
    {
        var rs = new SqlResultSet(
            new List<SqlColumnMetadata> { new("Data", "TEXT", typeof(string), true) },
            new List<object?[]> { new object?[] { "<script>alert(\"xss\")</script>" } },
            1, false);

        var result = await _formatter.FormatAsync(rs, _context);
        Assert.IsFalse(result.Content.Contains("<script>alert"));
        Assert.IsTrue(result.Content.Contains("&lt;script&gt;"));
    }

    [TestMethod]
    public void FormatResultSetHtml_PagingControls_Present()
    {
        var rows = new List<object?[]>();
        for (int i = 0; i < 60; i++)
            rows.Add(new object?[] { i });

        var rs = new SqlResultSet(
            new List<SqlColumnMetadata> { new("Id", "INTEGER", typeof(int), false) },
            rows, 60, false);

        var html = ResultSetFormatter.FormatResultSetHtml(rs, null, pageSize: 50);
        Assert.IsTrue(html.Contains("verso-sql-pager"));
        Assert.IsTrue(html.Contains("Previous"));
        Assert.IsTrue(html.Contains("Next"));
    }

    [TestMethod]
    public void FormatResultSetHtml_Truncated_ShowsWarning()
    {
        var rs = new SqlResultSet(
            new List<SqlColumnMetadata> { new("Id", "INTEGER", typeof(int), false) },
            new List<object?[]> { new object?[] { 1 } },
            TotalRowCount: 1000,
            WasTruncated: true);

        var html = ResultSetFormatter.FormatResultSetHtml(rs, null);
        Assert.IsTrue(html.Contains("verso-sql-truncation"));
        Assert.IsTrue(html.Contains("Results truncated"));
        Assert.IsTrue(html.Contains("WHERE or LIMIT"));
    }

    [TestMethod]
    public void FormatResultSetHtml_Empty_ShowsNoRowsMessage()
    {
        var rs = new SqlResultSet(
            new List<SqlColumnMetadata> { new("Id", "INTEGER", typeof(int), false) },
            new List<object?[]>(),
            0, false);

        var html = ResultSetFormatter.FormatResultSetHtml(rs, null);
        Assert.IsTrue(html.Contains("Query returned no rows"));
    }

    [TestMethod]
    public void FormatNonQueryHtml_ShowsRowsAffected()
    {
        var html = ResultSetFormatter.FormatNonQueryHtml(5, 42, null);
        Assert.IsTrue(html.Contains("5 row(s) affected"));
        Assert.IsTrue(html.Contains("42 ms"));
    }
}
