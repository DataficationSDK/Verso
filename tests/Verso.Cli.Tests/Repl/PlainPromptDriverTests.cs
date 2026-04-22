using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Repl.Prompt;

namespace Verso.Cli.Tests.Repl;

[TestClass]
public class PlainPromptDriverTests
{
    [TestMethod]
    public async Task Read_MetaCommandOnFirstLine_SubmitsImmediately()
    {
        // Meta-commands should take effect without the user needing a trailing blank line.
        var input = new StringReader(".exit\n");
        var output = new StringWriter();
        var driver = new PlainPromptDriver(input, output);

        var result = await driver.ReadAsync(1, "csharp", null, CancellationToken.None);

        Assert.AreEqual(ReplInputKind.Submission, result.Kind);
        Assert.AreEqual(".exit", result.Text);
    }

    [TestMethod]
    public async Task Read_BlankLineAfterCode_Submits()
    {
        var input = new StringReader("var x = 42;\n\n");
        var output = new StringWriter();
        var driver = new PlainPromptDriver(input, output);

        var result = await driver.ReadAsync(1, "csharp", null, CancellationToken.None);

        Assert.AreEqual(ReplInputKind.Submission, result.Kind);
        Assert.AreEqual("var x = 42;", result.Text);
    }

    [TestMethod]
    public async Task Read_DoubleSemicolonSentinel_Submits()
    {
        // ;; on its own line is the F#-style explicit submit sentinel.
        var input = new StringReader("let x =\n  1 + 2\n;;\n");
        var output = new StringWriter();
        var driver = new PlainPromptDriver(input, output);

        var result = await driver.ReadAsync(1, "fsharp", null, CancellationToken.None);

        Assert.AreEqual(ReplInputKind.Submission, result.Kind);
        StringAssert.Contains(result.Text, "let x =");
        StringAssert.Contains(result.Text, "1 + 2");
        Assert.IsFalse(result.Text.Contains(";;"), "The ;; sentinel should not be part of the submitted text.");
    }

    [TestMethod]
    public async Task Read_EofAtEmptyPrompt_ReturnsEof()
    {
        var input = new StringReader("");
        var output = new StringWriter();
        var driver = new PlainPromptDriver(input, output);

        var result = await driver.ReadAsync(1, "csharp", null, CancellationToken.None);

        Assert.AreEqual(ReplInputKind.Eof, result.Kind);
    }

    [TestMethod]
    public async Task Read_EofMidBuffer_SubmitsPartial()
    {
        // A pipe closing while we have buffered lines should submit them — this is how
        // `echo '1+1' | verso repl --plain` delivers its single-line payload for execution.
        var input = new StringReader("1+1\n");
        var output = new StringWriter();
        var driver = new PlainPromptDriver(input, output);

        var result = await driver.ReadAsync(1, "csharp", null, CancellationToken.None);

        Assert.AreEqual(ReplInputKind.Submission, result.Kind);
        Assert.AreEqual("1+1", result.Text);
    }

    [TestMethod]
    public async Task Read_InitialText_IsSeededIntoBuffer()
    {
        // .recall pre-loads source into the prompt; the subsequent blank line submits it.
        var input = new StringReader("\n");
        var output = new StringWriter();
        var driver = new PlainPromptDriver(input, output);

        var result = await driver.ReadAsync(1, "csharp", "var x = 42;", CancellationToken.None);

        Assert.AreEqual(ReplInputKind.Submission, result.Kind);
        StringAssert.Contains(result.Text, "var x = 42;");
    }
}
