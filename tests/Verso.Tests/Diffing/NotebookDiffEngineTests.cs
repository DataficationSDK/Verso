using Verso.Abstractions;
using Verso.Diffing;

namespace Verso.Tests.Diffing;

[TestClass]
public sealed class NotebookDiffEngineTests
{
    private static NotebookModel Notebook(params CellModel[] cells)
        => new() { Cells = cells.ToList() };

    private static CellModel Cell(string source, Guid? id = null)
        => new() { Id = id ?? Guid.NewGuid(), Source = source, Language = "csharp" };

    [TestMethod]
    public void Compute_SetsBaselineLabel()
    {
        var result = NotebookDiffEngine.Compute(Notebook(), Notebook(), "Git: HEAD");

        Assert.AreEqual("Git: HEAD", result.BaselineLabel);
    }

    [TestMethod]
    public void Compute_MetadataModifiedTimestampDiffers_NotReportedAsChange()
    {
        var baseline = Notebook();
        baseline.Modified = DateTimeOffset.UtcNow.AddDays(-1);
        var current = Notebook();
        current.Modified = DateTimeOffset.UtcNow;

        var result = NotebookDiffEngine.Compute(baseline, current, "Last Saved");

        Assert.AreEqual(0, result.MetadataChanges.Count);
    }

    [TestMethod]
    public void Compute_FormatVersionDiffers_NotReportedAsChange()
    {
        var baseline = Notebook();
        baseline.FormatVersion = "1.0";
        var current = Notebook();
        current.FormatVersion = "1.1";

        var result = NotebookDiffEngine.Compute(baseline, current, "Last Saved");

        Assert.AreEqual(0, result.MetadataChanges.Count);
    }

    [TestMethod]
    public void Compute_TitleChanged_ReportedInMetadataChanges()
    {
        var baseline = Notebook();
        baseline.Title = "Old Title";
        var current = Notebook();
        current.Title = "New Title";

        var result = NotebookDiffEngine.Compute(baseline, current, "Last Saved");

        var change = result.MetadataChanges.Single();
        Assert.AreEqual("Title", change.Field);
        Assert.AreEqual("Old Title", change.BaselineValue);
        Assert.AreEqual("New Title", change.CurrentValue);
    }

    [TestMethod]
    public void Compute_ActiveLayoutChanged_ReportedQualified()
    {
        var baseline = Notebook();
        baseline.ActiveLayout = new LayoutReference("verso.layout.notebook", "notebook");
        var current = Notebook();
        current.ActiveLayout = new LayoutReference("verso.layout.presentation", "presentation");

        var result = NotebookDiffEngine.Compute(baseline, current, "Last Saved");

        var change = result.MetadataChanges.Single();
        Assert.AreEqual("Active layout", change.Field);
        Assert.AreEqual("verso.layout.notebook:notebook", change.BaselineValue);
        Assert.AreEqual("verso.layout.presentation:presentation", change.CurrentValue);
    }

    [TestMethod]
    public void Compute_UnqualifiedBaselineLayout_SameLayoutId_NotReportedAsChange()
    {
        // A baseline parsed from an older file format keeps its bare-string layout reference;
        // the live notebook's has been qualified. Same layout id means no real change.
        var baseline = Notebook();
        baseline.ActiveLayout = new LayoutReference("", "notebook");
        var current = Notebook();
        current.ActiveLayout = new LayoutReference("verso.layout.notebook", "notebook");

        var result = NotebookDiffEngine.Compute(baseline, current, "Last Saved");

        Assert.AreEqual(0, result.MetadataChanges.Count);
    }

    [TestMethod]
    public void Compute_UnqualifiedBaselineLayout_DifferentLayoutId_Reported()
    {
        var baseline = Notebook();
        baseline.ActiveLayout = new LayoutReference("", "notebook");
        var current = Notebook();
        current.ActiveLayout = new LayoutReference("verso.layout.presentation", "presentation");

        var result = NotebookDiffEngine.Compute(baseline, current, "Last Saved");

        var change = result.MetadataChanges.Single();
        Assert.AreEqual("Active layout", change.Field);
        Assert.AreEqual("notebook", change.BaselineValue);
        Assert.AreEqual("verso.layout.presentation:presentation", change.CurrentValue);
    }

    [TestMethod]
    public void Compute_RequiredExtensionsReordered_NotReportedAsChange()
    {
        var baseline = Notebook();
        baseline.RequiredExtensions.AddRange(new[] { "ext.alpha", "ext.beta" });
        var current = Notebook();
        current.RequiredExtensions.AddRange(new[] { "ext.beta", "ext.alpha" });

        var result = NotebookDiffEngine.Compute(baseline, current, "Last Saved");

        Assert.AreEqual(0, result.MetadataChanges.Count);
    }

    [TestMethod]
    public void Compute_RequiredExtensionAdded_Reported()
    {
        var baseline = Notebook();
        var current = Notebook();
        current.RequiredExtensions.Add("ext.alpha");

        var result = NotebookDiffEngine.Compute(baseline, current, "Last Saved");

        var change = result.MetadataChanges.Single();
        Assert.AreEqual("Required extensions", change.Field);
        Assert.IsNull(change.BaselineValue);
        Assert.AreEqual("ext.alpha", change.CurrentValue);
    }

    [TestMethod]
    public void Compute_SummaryCounts_MatchCellKindTally()
    {
        var kept = Cell("kept");
        var edited = Cell("edited before");
        var removed = Cell("removed");
        var baseline = Notebook(kept, edited, removed);
        var current = Notebook(
            new CellModel { Id = kept.Id, Source = kept.Source, Language = kept.Language },
            new CellModel { Id = edited.Id, Source = "edited after", Language = edited.Language },
            Cell("added"));

        var result = NotebookDiffEngine.Compute(baseline, current, "Last Saved");

        Assert.AreEqual(1, result.Summary.Added);
        Assert.AreEqual(1, result.Summary.Removed);
        Assert.AreEqual(1, result.Summary.Modified);
        Assert.AreEqual(0, result.Summary.Moved);
        Assert.AreEqual(1, result.Summary.Unchanged);
        Assert.AreEqual(result.Cells.Count, result.Summary.Added + result.Summary.Removed + result.Summary.Modified + result.Summary.Moved + result.Summary.Unchanged);
    }

    [TestMethod]
    public void Compute_OutputsChanged_SourceUnchanged_FlaggedOutputsChangedOnly()
    {
        var cell = Cell("print(1)");
        cell.Outputs.Add(CellOutput.Plain("1"));
        var baseline = Notebook(cell);
        var current = Notebook(new CellModel
        {
            Id = cell.Id,
            Source = cell.Source,
            Language = cell.Language,
            Outputs = new List<CellOutput> { CellOutput.Plain("2") },
        });

        var result = NotebookDiffEngine.Compute(baseline, current, "Last Saved");

        var entry = result.Cells.Single();
        Assert.AreEqual(CellDiffKind.Modified, entry.Kind);
        Assert.IsTrue(entry.OutputsChanged);
        Assert.IsFalse(entry.SourceChanged);
        Assert.AreEqual(1, result.Summary.Modified);
    }
}
