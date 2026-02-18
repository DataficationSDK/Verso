namespace MyExtension.Tests;

[TestClass]
public sealed class SampleFormatterTests
{
    private readonly SampleFormatter _formatter = new();

    [TestMethod]
    public void ExtensionId_IsSet()
    {
        Assert.IsFalse(string.IsNullOrEmpty(_formatter.ExtensionId));
    }

    [TestMethod]
    public void CanFormat_DateTime_ReturnsTrue()
    {
        var context = new StubFormatterContext();
        Assert.IsTrue(_formatter.CanFormat(DateTime.Now, context));
    }

    [TestMethod]
    public void CanFormat_String_ReturnsFalse()
    {
        var context = new StubFormatterContext();
        Assert.IsFalse(_formatter.CanFormat("hello", context));
    }

    [TestMethod]
    public async Task FormatAsync_ReturnsHtml()
    {
        var context = new StubFormatterContext();
        var output = await _formatter.FormatAsync(new DateTime(2025, 1, 15, 10, 30, 0), context);

        Assert.AreEqual("text/html", output.MimeType);
        Assert.IsTrue(output.Content.Contains("<time"));
    }
}
