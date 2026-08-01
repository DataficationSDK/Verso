using Microsoft.Data.Analysis;
using Verso.DataFrame.Formatters;
using Verso.Testing.Stubs;
using AnalysisDataFrame = Microsoft.Data.Analysis.DataFrame;

namespace Verso.DataFrame.Tests.Formatters;

[TestClass]
public sealed class DataFrameFormatterTests
{
    private readonly DataFrameFormatter _formatter = new();
    private readonly StubFormatterContext _context = new();

    [TestMethod]
    public void Metadata_IsCorrect()
    {
        Assert.AreEqual("verso.dataframe.formatter", _formatter.ExtensionId);
        Assert.AreEqual(50, _formatter.Priority);
        Assert.IsFalse(((Verso.Abstractions.IDataFormatter)_formatter).IsFallback);
        Assert.IsTrue(_formatter.SupportedTypes.Contains(typeof(object)));
    }

    [TestMethod]
    public void HasVersoExtensionAttribute()
    {
        Assert.IsNotNull(
            typeof(DataFrameFormatter).GetCustomAttributes(
                typeof(Verso.Abstractions.VersoExtensionAttribute),
                inherit: false).SingleOrDefault());
    }

    [TestMethod]
    public void CanFormat_DataFrame_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(CreateDataFrame(), _context));

    [TestMethod]
    public void CanFormat_OtherObject_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat(new object(), _context));

    [TestMethod]
    public async Task FormatAsync_RendersColumnsTypesRowsAndFooter()
    {
        var output = await _formatter.FormatAsync(CreateDataFrame(), _context);

        Assert.AreEqual("text/html", output.MimeType);
        StringAssert.Contains(output.Content, "species");
        StringAssert.Contains(output.Content, "String");
        StringAssert.Contains(output.Content, "bill_length_mm");
        StringAssert.Contains(output.Content, "Single");
        StringAssert.Contains(output.Content, "Adelie");
        StringAssert.Contains(output.Content, "39.1");
        StringAssert.Contains(output.Content, "Showing 2 of 2 rows");
    }

    [TestMethod]
    public async Task FormatAsync_PreservesNullColumnPosition()
    {
        var frame = new AnalysisDataFrame(
            new[]
            {
                new DataFrameColumn("first", typeof(string)),
                new DataFrameColumn("second", typeof(string)),
                new DataFrameColumn("third", typeof(string))
            },
            new[] { new DataFrameRow("left", null, "right") });

        var output = await _formatter.FormatAsync(frame, _context);

        StringAssert.Contains(
            output.Content,
            "<td>left</td><td><span class=\"verso-dataframe-null\">null</span></td><td>right</td>");
    }

    [TestMethod]
    public async Task FormatAsync_HtmlEncodesColumnNamesAndValues()
    {
        var frame = new AnalysisDataFrame(
            new[] { new DataFrameColumn("<column>", typeof(string)) },
            new[] { new DataFrameRow("<script>alert('x')</script>") });

        var output = await _formatter.FormatAsync(frame, _context);

        Assert.IsFalse(output.Content.Contains("<script>"));
        StringAssert.Contains(output.Content, "&lt;column&gt;");
        StringAssert.Contains(output.Content, "&lt;script&gt;");
    }

    [TestMethod]
    public async Task FormatAsync_TruncatesLargeFrames()
    {
        var rows = Enumerable.Range(0, 105)
            .Select(index => new DataFrameRow($"row-{index}"));
        var frame = new AnalysisDataFrame(
            new[] { new DataFrameColumn("value", typeof(string)) },
            rows);

        var output = await _formatter.FormatAsync(frame, _context);

        StringAssert.Contains(output.Content, "row-99");
        Assert.IsFalse(output.Content.Contains("row-100"));
        StringAssert.Contains(output.Content, "Showing 100 of 105 rows");
    }

    [TestMethod]
    public async Task FormatAsync_EmptyFrameRendersHelpfulMessage()
    {
        var frame = new AnalysisDataFrame(
            new[] { new DataFrameColumn("value", typeof(int)) },
            Array.Empty<DataFrameRow>());

        var output = await _formatter.FormatAsync(frame, _context);

        StringAssert.Contains(output.Content, "DataFrame has no rows.");
        StringAssert.Contains(output.Content, "Showing 0 of 0 rows");
    }

    [TestMethod]
    public async Task FormatAsync_ObservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _context.CancellationToken = cancellation.Token;

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(
            () => _formatter.FormatAsync(CreateDataFrame(), _context));
    }

    private static AnalysisDataFrame CreateDataFrame()
    {
        return new AnalysisDataFrame(
            new[]
            {
                new DataFrameColumn("species", typeof(string)),
                new DataFrameColumn("bill_length_mm", typeof(float))
            },
            new[]
            {
                new DataFrameRow("Adelie", 39.1f),
                new DataFrameRow("Gentoo", 46.1f)
            });
    }
}
