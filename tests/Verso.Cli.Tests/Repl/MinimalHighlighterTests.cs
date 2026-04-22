using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Repl.Prompt;

namespace Verso.Cli.Tests.Repl;

[TestClass]
public class MinimalHighlighterTests
{
    [TestMethod]
    public void Highlight_CSharpLineComment_ProducesOneSpan()
    {
        var spans = MinimalHighlighter.Highlight("var x = 1; // trailing\nvar y = 2;", "csharp");
        Assert.AreEqual(1, spans.Count);
        using var e = spans.GetEnumerator(); e.MoveNext();
        Assert.AreEqual(11, e.Current.Start);
        // Length covers "// trailing" (11 chars) without the newline.
        Assert.AreEqual(11, e.Current.Length);
    }

    [TestMethod]
    public void Highlight_CSharpStringLiteral_IsIncludedAsOneSpan()
    {
        var spans = MinimalHighlighter.Highlight("var s = \"hello\";", "csharp");
        Assert.AreEqual(1, spans.Count);
        using var e = spans.GetEnumerator(); e.MoveNext();
        Assert.AreEqual(8, e.Current.Start);
        Assert.AreEqual(7, e.Current.Length);
    }

    [TestMethod]
    public void Highlight_CSharpBlockComment_Spans()
    {
        var spans = MinimalHighlighter.Highlight("/* multi\nline */ rest", "csharp");
        Assert.AreEqual(1, spans.Count);
        using var e = spans.GetEnumerator(); e.MoveNext();
        Assert.AreEqual(0, e.Current.Start);
        // "/* multi\nline */" = 16 characters.
        Assert.AreEqual(16, e.Current.Length);
    }

    [TestMethod]
    public void Highlight_PythonHashComment()
    {
        var spans = MinimalHighlighter.Highlight("x = 1  # hi\ny", "python");
        Assert.AreEqual(1, spans.Count);
    }

    [TestMethod]
    public void Highlight_UnknownLanguage_ReturnsEmpty()
    {
        var spans = MinimalHighlighter.Highlight("anything // nothing is styled", "ruby");
        Assert.AreEqual(0, spans.Count);
    }

    [TestMethod]
    public void Highlight_EmptyText_ReturnsEmpty()
    {
        var spans = MinimalHighlighter.Highlight("", "csharp");
        Assert.AreEqual(0, spans.Count);
    }

    [TestMethod]
    public void Highlight_SqlLineComment()
    {
        var spans = MinimalHighlighter.Highlight("SELECT 1 -- inline\nFROM dual", "sql");
        Assert.AreEqual(1, spans.Count);
    }

    [TestMethod]
    public void Highlight_FSharpBlockCommentIsNestable()
    {
        var spans = MinimalHighlighter.Highlight("(* outer (* inner *) still-outer *) code", "fsharp");
        Assert.AreEqual(1, spans.Count);
        using var e = spans.GetEnumerator(); e.MoveNext();
        Assert.AreEqual(0, e.Current.Start);
        // The balanced block extends through both closing *) but not the trailing code.
        Assert.AreEqual("(* outer (* inner *) still-outer *)".Length, e.Current.Length);
    }
}
