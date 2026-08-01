using Verso.Abstractions;
using Verso.Blazor.Shared.Models;
using Verso.Blazor.Shared.Tests.Fakes;

namespace Verso.Blazor.Shared.Tests;

/// <summary>
/// The state behind the Compare panel. Held apart from the component so the rules about
/// what survives a failed or cancelled comparison can be stated without rendering anything.
/// </summary>
[TestClass]
public sealed class NotebookComparisonTests
{
    private FakeNotebookService _service = default!;
    private NotebookComparison _comparison = default!;

    [TestInitialize]
    public void SetUp()
    {
        _service = new FakeNotebookService();
        _comparison = new NotebookComparison(_service);
    }

    [TestMethod]
    public async Task LoadSourcesAsync_PopulatesSources()
    {
        Assert.IsNull(_comparison.Sources, "Sources are unknown until asked for.");

        await _comparison.LoadSourcesAsync();

        Assert.IsNotNull(_comparison.Sources);
        Assert.AreEqual(4, _comparison.Sources.Count);
    }

    [TestMethod]
    public async Task CompareAsync_Success_KeepsResultAndBaseline()
    {
        _service.DiffResultToReturn = new NotebookDiffResult { BaselineLabel = "Git: HEAD" };

        var compared = await _comparison.CompareAsync("gitHead");

        Assert.IsTrue(compared);
        Assert.IsTrue(_comparison.IsActive);
        Assert.AreEqual("Git: HEAD", _comparison.Result!.BaselineLabel);
        Assert.AreEqual("gitHead", _comparison.BaselineSourceId);
        Assert.AreEqual("gitHead", _service.LastDiffSourceId);
    }

    [TestMethod]
    public async Task CompareAsync_Cancelled_LeavesTheCurrentComparisonAlone()
    {
        // A null result means the user backed out of a native picker. That is not a reason
        // to throw away a comparison they already had.
        _service.DiffResultToReturn = new NotebookDiffResult { BaselineLabel = "Last Saved" };
        await _comparison.CompareAsync("lastSaved");

        _service.DiffResultToReturn = null;
        var compared = await _comparison.CompareAsync("file");

        Assert.IsFalse(compared);
        Assert.IsTrue(_comparison.IsActive);
        Assert.AreEqual("Last Saved", _comparison.Result!.BaselineLabel);
        Assert.AreEqual("lastSaved", _comparison.BaselineSourceId);
    }

    [TestMethod]
    public async Task CompareAsync_Failure_PropagatesSoTheCallerCanReportIt()
    {
        _service.ComputeDiffExceptionToThrow =
            new InvalidOperationException("'notebook.verso' is not tracked at 'HEAD'.");

        var thrown = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => _comparison.CompareAsync("gitHead"));

        StringAssert.Contains(thrown.Message, "not tracked at 'HEAD'");
        Assert.IsFalse(_comparison.IsActive);
        Assert.IsFalse(_comparison.IsBusy, "A failure must not leave the panel showing a spinner.");
    }

    [TestMethod]
    public async Task CellMarks_CoverChangedCellsOnly()
    {
        var modified = Guid.NewGuid();
        var added = Guid.NewGuid();
        var unchanged = Guid.NewGuid();
        var removed = Guid.NewGuid();

        _service.DiffResultToReturn = new NotebookDiffResult
        {
            BaselineLabel = "Last Saved",
            Cells = new List<CellDiffEntry>
            {
                Entry(CellDiffKind.Modified, current: modified),
                Entry(CellDiffKind.Added, current: added),
                Entry(CellDiffKind.Unchanged, current: unchanged),
                // Removed cells carry only a baseline side; there is no cell left to mark.
                Entry(CellDiffKind.Removed, baseline: removed),
            }
        };

        await _comparison.CompareAsync("lastSaved");

        Assert.AreEqual(2, _comparison.CellMarks.Count);
        Assert.AreEqual(CellDiffKind.Modified, _comparison.CellMarks[modified]);
        Assert.AreEqual(CellDiffKind.Added, _comparison.CellMarks[added]);
        Assert.IsFalse(_comparison.CellMarks.ContainsKey(unchanged));
        Assert.IsFalse(_comparison.CellMarks.ContainsKey(removed));
    }

    [TestMethod]
    public async Task Clear_DropsTheResultAndTheMarks_ButKeepsTheSources()
    {
        _service.DiffResultToReturn = new NotebookDiffResult
        {
            BaselineLabel = "Git: HEAD",
            Cells = new List<CellDiffEntry> { Entry(CellDiffKind.Modified, current: Guid.NewGuid()) }
        };
        await _comparison.LoadSourcesAsync();
        await _comparison.CompareAsync("gitHead");

        _comparison.Clear();

        Assert.IsFalse(_comparison.IsActive);
        Assert.IsNull(_comparison.Result);
        Assert.IsNull(_comparison.BaselineSourceId);
        Assert.AreEqual(0, _comparison.CellMarks.Count);
        Assert.IsNotNull(_comparison.Sources,
            "The baselines on offer do not stop existing because the reader stopped comparing.");
    }

    private static CellDiffEntry Entry(CellDiffKind kind, Guid? current = null, Guid? baseline = null)
        => new()
        {
            Kind = kind,
            CurrentCell = current is { } c ? new CellModel { Id = c } : null,
            BaselineCell = baseline is { } b ? new CellModel { Id = b } : null,
        };
}
