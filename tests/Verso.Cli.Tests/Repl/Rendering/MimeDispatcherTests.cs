using Microsoft.VisualStudio.TestTools.UnitTesting;
using Spectre.Console;
using Spectre.Console.Testing;
using Verso.Abstractions;
using Verso.Cli.Repl.Rendering;
using Verso.Cli.Repl.Settings;

namespace Verso.Cli.Tests.Repl.Rendering;

[TestClass]
public class MimeDispatcherTests
{
    private TestConsole _console = null!;
    private MimeDispatcher _dispatcher = null!;
    private TruncationPolicy _policy;

    [TestInitialize]
    public void Setup()
    {
        _console = new TestConsole();
        _console.Profile.Width = 120;
        _dispatcher = new MimeDispatcher(_console, useColor: true);
        _policy = TruncationPolicy.FromSettings(new ReplSettings());
    }

    [TestMethod]
    public void Render_TextPlain_PrintsContent()
    {
        _dispatcher.Render(new CellOutput("text/plain", "hello"), cellCounter: 1, outputIndex: 0, _policy);
        StringAssert.Contains(_console.Output, "hello");
    }

    [TestMethod]
    public void Render_Markdown_HeadingBecomesRule()
    {
        _dispatcher.Render(new CellOutput("text/markdown", "# Title\nbody"), cellCounter: 1, outputIndex: 0, _policy);
        // The heading text should survive; Spectre's Rule includes the text in its output.
        StringAssert.Contains(_console.Output, "Title");
        StringAssert.Contains(_console.Output, "body");
    }

    [TestMethod]
    public void Render_Html_StripsTagsAndDecodesEntities()
    {
        _dispatcher.Render(new CellOutput("text/html", "<p>Hello &amp; <b>world</b></p>"), 1, 0, _policy);
        StringAssert.Contains(_console.Output, "Hello & world");
    }

    [TestMethod]
    public void Render_Html_DropsScriptContents()
    {
        _dispatcher.Render(new CellOutput("text/html", "<p>safe</p><script>alert(1)</script>"), 1, 0, _policy);
        StringAssert.Contains(_console.Output, "safe");
        Assert.IsFalse(_console.Output.Contains("alert(1)"),
            "Script contents must not leak into terminal output — they're neither useful nor safe to render.");
    }

    [TestMethod]
    public void Render_Csv_RendersAsTable()
    {
        _dispatcher.Render(new CellOutput("text/csv", "name,age\nalice,30\nbob,25"), 1, 0, _policy);
        // Spectre tables draw border characters around the column headers.
        StringAssert.Contains(_console.Output, "name");
        StringAssert.Contains(_console.Output, "alice");
        StringAssert.Contains(_console.Output, "bob");
    }

    [TestMethod]
    public void Render_Csv_RowCapAppendsTruncationFooter()
    {
        var rows = string.Join('\n', Enumerable.Range(0, 50).Select(i => $"r{i},v{i}"));
        var csv = "a,b\n" + rows;
        var policy = new TruncationPolicy(MaxRows: 5, MaxLines: 200, MaxWidth: 120);
        _dispatcher.Render(new CellOutput("text/csv", csv), 1, 0, policy);
        StringAssert.Contains(_console.Output, "45 more rows",
            "CSV rendering should cap to MaxRows and append a '… N more rows' footer.");
    }

    [TestMethod]
    public void Render_Png_SavesPlaceholderFile()
    {
        // 1x1 transparent PNG: smallest valid base64 payload.
        const string base64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";
        _dispatcher.Render(new CellOutput("image/png", base64), cellCounter: 7, outputIndex: 2, _policy);

        StringAssert.Contains(_console.Output, "image/png");
        StringAssert.Contains(_console.Output, "verso-repl-7-2.png");

        // The placeholder path should actually exist on disk so the user can open it.
        var path = Path.Combine(Path.GetTempPath(), "verso-repl-7-2.png");
        Assert.IsTrue(File.Exists(path), "Image placeholder should materialize the decoded bytes to $TMPDIR.");
        try { File.Delete(path); } catch { }
    }

    [TestMethod]
    public void Render_UnknownMime_FallsBackToPreview()
    {
        _dispatcher.Render(new CellOutput("application/x-made-up", "payload"), 1, 0, _policy);
        StringAssert.Contains(_console.Output, "application/x-made-up");
        StringAssert.Contains(_console.Output, "payload");
    }
}
