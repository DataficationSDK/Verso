using Bunit;
using Microsoft.AspNetCore.Components;
using Verso.Abstractions;
using Verso.Blazor.Shared.Components.Notebook;

namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class NotebookDiffViewTests : BunitTestContext
{
    private static CellModel Cell(string source, string type = "code", string? language = "csharp")
        => new() { Type = type, Language = language, Source = source };

    private static CellDiffEntry Entry(
        CellDiffKind kind,
        CellModel? baselineCell = null,
        CellModel? currentCell = null,
        int? baselineIndex = null,
        int? currentIndex = null,
        bool sourceChanged = false,
        bool outputsChanged = false)
        => new()
        {
            Kind = kind,
            BaselineCell = baselineCell,
            CurrentCell = currentCell,
            BaselineIndex = baselineIndex,
            CurrentIndex = currentIndex,
            SourceChanged = sourceChanged,
            OutputsChanged = outputsChanged,
        };

    private static NotebookDiffResult Result(params CellDiffEntry[] entries)
    {
        var result = new NotebookDiffResult
        {
            BaselineLabel = "Git: HEAD",
            Cells = entries.ToList(),
        };
        foreach (var entry in entries)
        {
            switch (entry.Kind)
            {
                case CellDiffKind.Added: result.Summary.Added++; break;
                case CellDiffKind.Removed: result.Summary.Removed++; break;
                case CellDiffKind.Modified: result.Summary.Modified++; break;
                case CellDiffKind.Moved: result.Summary.Moved++; break;
                default: result.Summary.Unchanged++; break;
            }
        }

        return result;
    }

    private IRenderedComponent<NotebookDiffView> Render(NotebookDiffResult diff, EventCallback onClose = default)
    {
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;
        return TestContext.RenderComponent<NotebookDiffView>(parameters => parameters
            .Add(p => p.Diff, diff)
            .Add(p => p.OnClose, onClose));
    }

    [TestMethod]
    public void RendersSummaryBadges_MatchingCounts()
    {
        var diff = Result(
            Entry(CellDiffKind.Added, currentCell: Cell("new"), currentIndex: 0),
            Entry(CellDiffKind.Removed, baselineCell: Cell("old"), baselineIndex: 1),
            Entry(CellDiffKind.Modified, baselineCell: Cell("a"), currentCell: Cell("b"), baselineIndex: 2, currentIndex: 1, sourceChanged: true));

        var cut = Render(diff);

        StringAssert.Contains(cut.Find(".verso-diff-summary-badge--added").TextContent, "1 added");
        StringAssert.Contains(cut.Find(".verso-diff-summary-badge--removed").TextContent, "1 removed");
        StringAssert.Contains(cut.Find(".verso-diff-summary-badge--modified").TextContent, "1 modified");
        StringAssert.Contains(cut.Find(".verso-diff-title-text").TextContent, "Git: HEAD");
    }

    [TestMethod]
    public void AddedCell_RendersTintedBlock_NoMonacoContainer()
    {
        var cut = Render(Result(Entry(CellDiffKind.Added, currentCell: Cell("var x = 1;"), currentIndex: 0)));

        var block = cut.Find(".verso-diff-cell--added .verso-diff-source-block");
        StringAssert.Contains(block.TextContent, "var x = 1;");
        Assert.AreEqual(0, cut.FindAll(".verso-monaco-diff-editor").Count);
    }

    [TestMethod]
    public void RemovedCell_RendersTintedBlock()
    {
        var cut = Render(Result(Entry(CellDiffKind.Removed, baselineCell: Cell("var gone = true;"), baselineIndex: 0)));

        var block = cut.Find(".verso-diff-cell--removed .verso-diff-source-block");
        StringAssert.Contains(block.TextContent, "var gone = true;");
        Assert.AreEqual(0, cut.FindAll(".verso-monaco-diff-editor").Count);
    }

    [TestMethod]
    public void ModifiedCell_BelowThreshold_MountsMonacoDiffEditorEagerly()
    {
        var cut = Render(Result(Entry(
            CellDiffKind.Modified,
            baselineCell: Cell("before"), currentCell: Cell("after"),
            baselineIndex: 0, currentIndex: 0, sourceChanged: true)));

        Assert.AreEqual(1, cut.FindAll(".verso-monaco-diff-editor").Count);
        Assert.AreEqual(0, cut.FindAll(".verso-diff-expand-btn").Count);
    }

    [TestMethod]
    public void ModifiedCells_AboveThreshold_CollapsedUntilExpanded()
    {
        var entries = Enumerable.Range(0, NotebookDiffView.MaxEagerDiffEditors + 1)
            .Select(i => Entry(
                CellDiffKind.Modified,
                baselineCell: Cell($"before {i}"), currentCell: Cell($"after {i}"),
                baselineIndex: i, currentIndex: i, sourceChanged: true))
            .ToArray();

        var cut = Render(Result(entries));

        Assert.AreEqual(0, cut.FindAll(".verso-monaco-diff-editor").Count);
        var expandButtons = cut.FindAll(".verso-diff-expand-btn");
        Assert.AreEqual(entries.Length, expandButtons.Count);

        cut.FindAll(".verso-diff-expand-btn")[0].Click();

        Assert.AreEqual(1, cut.FindAll(".verso-monaco-diff-editor").Count);
    }

    [TestMethod]
    public void UnchangedRun_CollapsesIntoGroupDivider_ExpandsOnClick()
    {
        var cut = Render(Result(
            Entry(CellDiffKind.Unchanged, baselineCell: Cell("a"), currentCell: Cell("a"), baselineIndex: 0, currentIndex: 0),
            Entry(CellDiffKind.Unchanged, baselineCell: Cell("b"), currentCell: Cell("b"), baselineIndex: 1, currentIndex: 1),
            Entry(CellDiffKind.Added, currentCell: Cell("new"), currentIndex: 2)));

        var toggle = cut.Find(".verso-diff-unchanged-toggle");
        StringAssert.Contains(toggle.TextContent, "2 unchanged cells");
        Assert.AreEqual(0, cut.FindAll(".verso-diff-cell--unchanged").Count);

        toggle.Click();

        Assert.AreEqual(2, cut.FindAll(".verso-diff-cell--unchanged").Count);
    }

    [TestMethod]
    public void MovedCell_RendersPositionDetail_NoSourceBlock()
    {
        var cut = Render(Result(Entry(
            CellDiffKind.Moved,
            baselineCell: Cell("same"), currentCell: Cell("same"),
            baselineIndex: 2, currentIndex: 0)));

        StringAssert.Contains(cut.Find(".verso-diff-move-detail").TextContent, "position 3");
        Assert.AreEqual(0, cut.FindAll(".verso-diff-cell--moved .verso-diff-source-block").Count);
    }

    [TestMethod]
    public void OutputsChanged_TogglesSideBySideOutputColumns()
    {
        var baselineCell = Cell("print(1)");
        baselineCell.Outputs.Add(CellOutput.Plain("old output"));
        var currentCell = Cell("print(1)");
        currentCell.Outputs.Add(CellOutput.Plain("new output"));

        var cut = Render(Result(Entry(
            CellDiffKind.Modified,
            baselineCell: baselineCell, currentCell: currentCell,
            baselineIndex: 0, currentIndex: 0, outputsChanged: true)));

        Assert.AreEqual(0, cut.FindAll(".verso-diff-outputs-columns").Count);

        cut.Find(".verso-diff-outputs-toggle").Click();

        var columns = cut.FindAll(".verso-diff-outputs-column");
        Assert.AreEqual(2, columns.Count);
        StringAssert.Contains(columns[0].TextContent, "old output");
        StringAssert.Contains(columns[1].TextContent, "new output");
    }

    [TestMethod]
    public void EscapeKey_InvokesOnClose()
    {
        var closed = false;
        var onClose = EventCallback.Factory.Create(this, () => closed = true);

        var cut = Render(Result(Entry(CellDiffKind.Added, currentCell: Cell("x"), currentIndex: 0)), onClose);
        cut.Find(".verso-diff-overlay").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        Assert.IsTrue(closed);
    }

    [TestMethod]
    public void CloseButton_InvokesOnClose()
    {
        var closed = false;
        var onClose = EventCallback.Factory.Create(this, () => closed = true);

        var cut = Render(Result(Entry(CellDiffKind.Added, currentCell: Cell("x"), currentIndex: 0)), onClose);
        cut.Find(".verso-diff-close").Click();

        Assert.IsTrue(closed);
    }

    [TestMethod]
    public void MetadataChanges_Rendered()
    {
        var diff = Result();
        diff.MetadataChanges.Add(new MetadataChange { Field = "Title", BaselineValue = "Old", CurrentValue = "New" });

        var cut = Render(diff);

        var row = cut.Find(".verso-diff-metadata-row");
        StringAssert.Contains(row.TextContent, "Title");
        StringAssert.Contains(row.TextContent, "Old");
        StringAssert.Contains(row.TextContent, "New");
    }

    [TestMethod]
    public void NoChanges_ShowsNoChangesBadge()
    {
        var cut = Render(Result(
            Entry(CellDiffKind.Unchanged, baselineCell: Cell("a"), currentCell: Cell("a"), baselineIndex: 0, currentIndex: 0)));

        StringAssert.Contains(cut.Find(".verso-diff-summary").TextContent, "No changes");
    }

    [TestMethod]
    public void NoChanges_AnswersInTheBody_NotOnlyInABadge()
    {
        // An otherwise empty scroll area reads as a view that failed to load rather than as
        // a notebook that matches its baseline.
        var cut = Render(Result(
            Entry(CellDiffKind.Unchanged, baselineCell: Cell("a"), currentCell: Cell("a"), baselineIndex: 0, currentIndex: 0)));

        var empty = cut.Find(".verso-diff-empty");
        StringAssert.Contains(empty.TextContent, "Identical to");
        StringAssert.Contains(empty.TextContent, "Git: HEAD");
    }

    [TestMethod]
    public void OneChange_OffersNoStepper()
    {
        // Stepping between changes is only worth the header space when there is somewhere
        // to step to.
        var cut = Render(Result(
            Entry(CellDiffKind.Added, currentCell: Cell("new"), currentIndex: 0)));

        Assert.AreEqual(0, cut.FindAll(".verso-diff-nav").Count);
    }

    [TestMethod]
    public void SeveralChanges_OfferTheStepperWithACount()
    {
        var cut = Render(Result(
            Entry(CellDiffKind.Added, currentCell: Cell("new"), currentIndex: 0),
            Entry(CellDiffKind.Unchanged, baselineCell: Cell("a"), currentCell: Cell("a"), baselineIndex: 0, currentIndex: 1),
            Entry(CellDiffKind.Removed, baselineCell: Cell("gone"), baselineIndex: 1)));

        StringAssert.Contains(cut.Find(".verso-diff-nav-position").TextContent, "2 changes");
        Assert.AreEqual(2, cut.FindAll(".verso-diff-nav-btn").Count);
    }

    [TestMethod]
    public void SteppingForward_ReportsPositionAndAnchorsToAChange()
    {
        var cut = Render(Result(
            Entry(CellDiffKind.Added, currentCell: Cell("new"), currentIndex: 0),
            Entry(CellDiffKind.Removed, baselineCell: Cell("gone"), baselineIndex: 1)));

        cut.FindAll(".verso-diff-nav-btn")[1].Click();

        StringAssert.Contains(cut.Find(".verso-diff-nav-position").TextContent, "1 of 2");
        Assert.AreEqual(2, cut.FindAll(".verso-diff-cell[id]").Count,
            "Every change carries an anchor, which is what the stepper scrolls to.");
    }
}
