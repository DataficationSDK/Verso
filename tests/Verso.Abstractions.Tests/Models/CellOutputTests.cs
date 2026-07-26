namespace Verso.Abstractions.Tests.Models;

[TestClass]
public class CellOutputTests
{
    [TestMethod]
    public void Constructor_SetsRequiredProperties()
    {
        var output = new CellOutput("text/plain", "hello");
        Assert.AreEqual("text/plain", output.MimeType);
        Assert.AreEqual("hello", output.Content);
    }

    [TestMethod]
    public void Defaults_AreCorrect()
    {
        var output = new CellOutput("text/plain", "hello");
        Assert.IsFalse(output.IsError);
        Assert.IsNull(output.ErrorName);
        Assert.IsNull(output.ErrorStackTrace);
    }

    [TestMethod]
    public void ErrorOutput_SetsAllFields()
    {
        var output = new CellOutput("text/plain", "err", IsError: true, ErrorName: "RuntimeError", ErrorStackTrace: "at line 1");
        Assert.IsTrue(output.IsError);
        Assert.AreEqual("RuntimeError", output.ErrorName);
        Assert.AreEqual("at line 1", output.ErrorStackTrace);
    }

    [TestMethod]
    public void RecordEquality_Works()
    {
        var a = new CellOutput("text/plain", "x");
        var b = new CellOutput("text/plain", "x");
        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void WithExpression_CreatesModifiedCopy()
    {
        var a = new CellOutput("text/plain", "x");
        var b = a with { Content = "y" };
        Assert.AreEqual("y", b.Content);
        Assert.AreEqual("x", a.Content);
    }

    // --- widget documents ---

    [TestMethod]
    public void Widget_CarriesTheDocumentUnderItsOwnType()
    {
        var output = CellOutput.Widget("<!DOCTYPE html><html></html>");

        Assert.AreEqual("text/x-verso-widget", output.MimeType);
        Assert.AreEqual(CellOutput.WidgetMimeType, output.MimeType);
    }

    [TestMethod]
    public void WidgetBody_KeepsOnlyWhatTheBodyHolds()
    {
        var document =
            "<!DOCTYPE html><html><head><title>Widget</title></head><body><b>drawn</b></body></html>";

        Assert.AreEqual("<b>drawn</b>", CellOutput.WidgetBody(document));
    }

    [TestMethod]
    public void WidgetBody_ReadsPastAttributesOnTheBodyElement()
    {
        var document = "<html><body class=\"widget\" style=\"margin:0\"><b>drawn</b></body></html>";

        Assert.AreEqual("<b>drawn</b>", CellOutput.WidgetBody(document));
    }

    [TestMethod]
    public void WidgetBody_StopsAtTheLastClosingTag()
    {
        // A widget's own script can carry the closing tag as text, split so it does not end the
        // element early. The real end of the body is the last one.
        var document = "<html><body><script>var s = '</bo' + 'dy>';</script></body></html>";

        var body = CellOutput.WidgetBody(document);

        Assert.IsTrue(body.StartsWith("<script>", StringComparison.Ordinal));
        Assert.IsTrue(body.EndsWith("</script>", StringComparison.Ordinal));
    }

    [TestMethod]
    public void WidgetBody_ReturnsADocumentItCannotReadWhole()
    {
        // Better to hand back markup a browser will make sense of than to drop the output.
        Assert.AreEqual("<b>loose</b>", CellOutput.WidgetBody("<b>loose</b>"));
    }

    [TestMethod]
    public void WidgetBody_HandlesAnUnclosedBody()
    {
        Assert.AreEqual("<b>drawn</b>", CellOutput.WidgetBody("<html><body><b>drawn</b>"));
    }

    [TestMethod]
    public void WidgetBody_OfNothingIsNothing()
    {
        Assert.AreEqual(string.Empty, CellOutput.WidgetBody(null));
        Assert.AreEqual(string.Empty, CellOutput.WidgetBody(""));
    }
}
