using System.Text.Json;
using Motus.Abstractions;
using Motus.Testing.MSTest;
using Verso.E2E.Tests.Infrastructure;

namespace Verso.E2E.Tests.Serve;

/// <summary>
/// A cell's editor grows to fit its content up to a line limit, past which it scrolls inside
/// itself and says how much is out of view and how to raise the limit. The compare view keeps a
/// taller limit of its own.
/// </summary>
[MotusTestClass]
public sealed class CellEditorHeightTests : MotusTestBase
{
    // The default limit, in lines, before a cell's editor scrolls inside itself.
    private const int MaxLines = 30;

    // The compare view's own limit.
    private const int CompareMaxLines = 40;

    private static VersoServer s_server = null!;
    private ScratchDirectory _scratch = null!;

    [ClassInitialize]
    public static async Task StartServerAsync(TestContext _) => s_server = await VersoServer.StartAsync();

    [ClassCleanup]
    public static async Task StopServerAsync()
    {
        if (s_server is not null)
            await s_server.DisposeAsync();
    }

    [TestInitialize]
    public void CreateScratch() => _scratch = new ScratchDirectory();

    [TestCleanup]
    public void RemoveScratch() => _scratch.Dispose();

    private ILocator LineLimitNotes => Page.Locator(".verso-cell-line-limit");

    [TestMethod]
    public async Task Cells_GrowToFitTheirContent_UpToTheLimit()
    {
        var notebook = _scratch.Write("tall.verso", Notebooks.VersoWithCellsOf(1, 20, 60));
        await OpenAsync(s_server, notebook);

        var editors = await MeasureEditorsAsync(".verso-cell", expected: 4);
        var lineHeight = editors[0].LineHeight;

        var oneLine = editors[0];
        Assert.IsTrue(oneLine.Height >= 3 * lineHeight, $"A one-line cell should still be three lines tall, was {oneLine.Height}px.");
        Assert.IsFalse(oneLine.ScrollsInside, "A one-line cell has nothing to scroll.");

        var twentyLines = editors[1];
        Assert.IsTrue(twentyLines.Height >= 20 * lineHeight, $"A 20-line cell should show all 20 lines, was {twentyLines.Height}px.");
        Assert.IsFalse(twentyLines.ScrollsInside, "A cell under the limit should grow instead of scrolling inside itself.");

        var sixtyLines = editors[2];
        Assert.AreEqual(MaxLines * lineHeight, sixtyLines.Height, lineHeight,
            "A cell past the limit should stop growing at the limit.");
        Assert.IsTrue(sixtyLines.ScrollsInside, "A cell past the limit should scroll inside itself.");

        var wide = editors[3];
        Assert.IsFalse(wide.ScrollsInside,
            "A cell with a horizontal scrollbar should grow by the scrollbar, not hide its last line behind it.");
    }

    [TestMethod]
    public async Task CellPastTheLimit_SaysSoAndHowToRaiseIt()
    {
        var notebook = _scratch.Write("tall.verso", Notebooks.VersoWithCellsOf(20, 60));
        await OpenAsync(s_server, notebook);
        await MeasureEditorsAsync(".verso-cell", expected: 3);
        await LineLimitNotes.First.WaitForAsync(ElementState.Visible, timeout: 10_000);

        Assert.AreEqual(1, await LineLimitNotes.CountAsync(), "Only the cell past the limit should carry the note.");
        var note = await LineLimitNotes.First.InnerTextAsync();
        StringAssert.Contains(note, $"Showing {MaxLines} of 60 lines");
        StringAssert.Contains(note, "--max-cell-lines", "In verso serve the note should name the option that raises the limit.");
    }

    [TestMethod]
    public async Task CellAtTheLimitWithALongLine_ShowsEveryLine()
    {
        // The limit counts lines, so the horizontal scrollbar a long line brings must not push
        // the last of exactly MaxLines lines out of view, or claim the cell is past the limit.
        var source = Notebooks.WideLine + "\n" + Notebooks.Lines(MaxLines - 1, "a");
        var notebook = _scratch.Write("wide.verso", Notebooks.VersoWithSources(source));
        await OpenAsync(s_server, notebook);

        var editors = await MeasureEditorsAsync(".verso-cell", expected: 1);
        Assert.IsFalse(editors[0].ScrollsInside,
            $"All {MaxLines} lines should show; {editors[0].ScrollHeight}px of content in {editors[0].Height}px.");
        Assert.AreEqual(0, await LineLimitNotes.CountAsync(), "A cell of exactly the limit is not past it.");
    }

    [TestMethod]
    public async Task MaxCellLinesOption_RaisesTheLimit()
    {
        await using var server = await VersoServer.StartAsync("--max-cell-lines", "100");
        var notebook = _scratch.Write("tall.verso", Notebooks.VersoWithCellsOf(60));
        await OpenAsync(server, notebook);

        var editors = await MeasureEditorsAsync(".verso-cell", expected: 2);
        var sixtyLines = editors[0];
        Assert.IsTrue(sixtyLines.Height >= 60 * sixtyLines.LineHeight,
            $"Under a limit of 100 a 60-line cell should show all 60 lines, was {sixtyLines.Height}px.");
        Assert.IsFalse(sixtyLines.ScrollsInside, "Under a limit of 100 a 60-line cell should not scroll inside itself.");
        Assert.AreEqual(0, await LineLimitNotes.CountAsync(), "No cell is past the limit, so none should say so.");
    }

    [TestMethod]
    public async Task CompareView_KeepsItsOwnTallerLimit()
    {
        // Open one version, then put another on disk: comparing with Last Saved shows the
        // difference between them as one changed cell 60 lines long. That is past both limits,
        // whether the view lays the two versions side by side or inline, so the view should
        // stop at its own limit rather than the shorter cell one.
        var cellId = Guid.NewGuid();
        var notebook = _scratch.Write("compare.verso", Notebooks.VersoWithCell(cellId, Notebooks.Lines(60, "a")));
        await OpenAsync(s_server, notebook);
        await MeasureEditorsAsync(".verso-cell", expected: 1);
        _scratch.Write("compare.verso", Notebooks.VersoWithCell(cellId, Notebooks.Lines(60, "b")));

        await Page.Locator("button.verso-toolbar-toggle[aria-label='Compare']").ClickAsync();
        await Page.Locator(".verso-panel-row", new LocatorOptions { HasText = "Last Saved" }).ClickAsync();
        await Page.Locator("button", new LocatorOptions { HasText = "Open full diff" }).ClickAsync();

        var sides = await MeasureEditorsAsync(".verso-monaco-diff-editor", expected: 2);
        var tallest = sides.MaxBy(side => side.Height)!;
        Assert.AreEqual(CompareMaxLines * tallest.LineHeight, tallest.Height, tallest.LineHeight,
            $"The compare view should stop at its own {CompareMaxLines}-line limit, not the {MaxLines}-line cell one.");
    }

    private async Task OpenAsync(VersoServer server, string notebookPath)
        => await Page.GotoAsync(server.UrlFor(notebookPath));

    private sealed record EditorSize(double Height, double LineHeight, bool ScrollsInside, double ScrollHeight);

    // Reads every editor inside the given container, in page order, once each has been laid
    // out. ScrollsInside compares what Monaco would scroll through with the room it was given.
    private async Task<EditorSize[]> MeasureEditorsAsync(string within, int expected)
    {
        const string read = """
            (() => {
              if (!window.monaco || !monaco.editor.getEditors) return null;
              const eds = monaco.editor.getEditors()
                .filter(e => e.getContainerDomNode().closest('%WITHIN%'))
                .sort((a, b) => a.getContainerDomNode().compareDocumentPosition(b.getContainerDomNode()) & 4 ? -1 : 1);
              if (eds.length < %EXPECTED%) return null;
              const lh = eds[0].getOption(monaco.editor.EditorOption.lineHeight);
              if (!lh) return null;
              const sizes = eds.map(e => ({
                height: e.getLayoutInfo().height,
                lineHeight: lh,
                scrollsInside: e.getScrollHeight() > e.getLayoutInfo().height + 1,
                scrollHeight: e.getScrollHeight()
              }));
              return sizes.some(s => s.height < 2 * lh) ? null : { sizes };
            })()
            """;

        var script = read.Replace("%EXPECTED%", expected.ToString()).Replace("%WITHIN%", within);
        await Page.WaitForFunctionAsync<bool>($"!!{script}", timeout: 60_000);

        // Sizing settles over a frame or two as content heights are reported.
        await Page.WaitForTimeoutAsync(300);
        var result = await Page.EvaluateAsync<JsonElement>(script);
        return result.GetProperty("sizes").EnumerateArray()
            .Select(s => new EditorSize(
                s.GetProperty("height").GetDouble(),
                s.GetProperty("lineHeight").GetDouble(),
                s.GetProperty("scrollsInside").GetBoolean(),
                s.GetProperty("scrollHeight").GetDouble()))
            .ToArray();
    }
}
