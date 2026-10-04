using System.Text.Json;
using Motus.Abstractions;
using Motus.Testing.MSTest;
using Verso.E2E.Tests.Infrastructure;

namespace Verso.E2E.Tests.Serve;

/// <summary>
/// A cell's editor grows to fit its content, so the notebook scrolls as one page instead of
/// inside each cell, up to a limit that keeps a cell with thousands of lines quick to draw.
/// </summary>
[MotusTestClass]
public sealed class CellEditorHeightTests : MotusTestBase
{
    // The default limit, in lines, before a cell's editor scrolls inside itself.
    private const int MaxLines = 500;

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

    [TestMethod]
    public async Task Cells_GrowToFitTheirContent_UpToTheLimit()
    {
        var notebook = _scratch.Write("tall.verso", Notebooks.VersoWithCellsOf(1, 60, 700));
        await OpenAsync(notebook);

        var editors = await MeasureEditorsAsync(".verso-cell", expected: 4);
        var lineHeight = editors[0].LineHeight;

        var oneLine = editors[0];
        Assert.IsTrue(oneLine.Height >= 3 * lineHeight, $"A one-line cell should still be three lines tall, was {oneLine.Height}px.");
        Assert.IsFalse(oneLine.ScrollsInside, "A one-line cell has nothing to scroll.");

        var sixtyLines = editors[1];
        Assert.IsTrue(sixtyLines.Height >= 60 * lineHeight, $"A 60-line cell should show all 60 lines, was {sixtyLines.Height}px.");
        Assert.IsFalse(sixtyLines.ScrollsInside, "A 60-line cell should grow instead of scrolling inside itself.");

        var sevenHundredLines = editors[2];
        Assert.AreEqual(MaxLines * lineHeight, sevenHundredLines.Height, lineHeight,
            "A cell past the limit should stop growing at the limit.");
        Assert.IsTrue(sevenHundredLines.ScrollsInside, "A cell past the limit should scroll inside itself.");

        var wide = editors[3];
        Assert.IsFalse(wide.ScrollsInside,
            "A cell with a horizontal scrollbar should grow by the scrollbar, not hide its last line behind it.");
    }

    [TestMethod]
    public async Task CompareView_GrowsToFitTheChangedCell()
    {
        // Open one version, then put another on disk: comparing with Last Saved shows the
        // difference between them as a single changed cell 60 lines long on each side.
        var cellId = Guid.NewGuid();
        var notebook = _scratch.Write("compare.verso", Notebooks.VersoWithCell(cellId, Notebooks.Lines(60, "a")));
        await OpenAsync(notebook);
        await MeasureEditorsAsync(".verso-cell", expected: 1);
        _scratch.Write("compare.verso", Notebooks.VersoWithCell(cellId, Notebooks.Lines(60, "b")));

        await Page.Locator("button.verso-toolbar-toggle[aria-label='Compare']").ClickAsync();
        await Page.Locator(".verso-panel-row", new LocatorOptions { HasText = "Last Saved" }).ClickAsync();
        await Page.Locator("button", new LocatorOptions { HasText = "Open full diff" }).ClickAsync();

        var sides = await MeasureEditorsAsync(".verso-monaco-diff-editor", expected: 2);
        foreach (var side in sides)
        {
            Assert.IsTrue(side.Height >= 60 * side.LineHeight, $"Each side should show all 60 lines, was {side.Height}px.");
            Assert.IsFalse(side.ScrollsInside, "A 60-line change should grow instead of scrolling inside itself.");
        }
    }

    private async Task OpenAsync(string notebookPath)
        => await Page.GotoAsync(s_server.UrlFor(notebookPath));

    private sealed record EditorSize(double Height, double LineHeight, bool ScrollsInside);

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
                scrollsInside: e.getScrollHeight() > e.getLayoutInfo().height + 1
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
                s.GetProperty("scrollsInside").GetBoolean()))
            .ToArray();
    }
}
