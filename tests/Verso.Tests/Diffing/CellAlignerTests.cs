using Verso.Abstractions;
using Verso.Diffing;

namespace Verso.Tests.Diffing;

[TestClass]
public sealed class CellAlignerTests
{
    private static CellModel Cell(string source, string type = "code", string? language = "csharp", Guid? id = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            Type = type,
            Language = language,
            Source = source,
        };

    private static CellModel Clone(CellModel cell, string? source = null, List<CellOutput>? outputs = null)
        => new()
        {
            Id = cell.Id,
            Type = cell.Type,
            Language = cell.Language,
            Source = source ?? cell.Source,
            Outputs = outputs ?? cell.Outputs.ToList(),
            Metadata = new Dictionary<string, object>(cell.Metadata),
        };

    [TestMethod]
    public void Align_IdenticalNotebooks_AllUnchanged()
    {
        var baseline = new[] { Cell("a"), Cell("b"), Cell("c") };
        var current = baseline.Select(c => Clone(c)).ToArray();

        var entries = CellAligner.Align(baseline, current);

        Assert.AreEqual(3, entries.Count);
        Assert.IsTrue(entries.All(e => e.Kind == CellDiffKind.Unchanged));
        Assert.IsTrue(entries.All(e => !e.MatchedByContent));
    }

    [TestMethod]
    public void Align_CellAdded_ReportsAddedAtCorrectIndex()
    {
        var a = Cell("a");
        var b = Cell("b");
        var entries = CellAligner.Align(
            new[] { a, b },
            new[] { Clone(a), Cell("inserted"), Clone(b) });

        Assert.AreEqual(3, entries.Count);
        Assert.AreEqual(CellDiffKind.Unchanged, entries[0].Kind);
        Assert.AreEqual(CellDiffKind.Added, entries[1].Kind);
        Assert.AreEqual(1, entries[1].CurrentIndex);
        Assert.IsNull(entries[1].BaselineIndex);
        Assert.IsNull(entries[1].BaselineCell);
        Assert.AreEqual(CellDiffKind.Unchanged, entries[2].Kind);
    }

    [TestMethod]
    public void Align_CellRemoved_ReportsRemovedAtCorrectGapPosition()
    {
        var a = Cell("a");
        var b = Cell("b");
        var c = Cell("c");
        var entries = CellAligner.Align(
            new[] { a, b, c },
            new[] { Clone(a), Clone(c) });

        Assert.AreEqual(3, entries.Count);
        Assert.AreEqual(CellDiffKind.Unchanged, entries[0].Kind);
        Assert.AreEqual(CellDiffKind.Removed, entries[1].Kind);
        Assert.AreEqual(1, entries[1].BaselineIndex);
        Assert.IsNull(entries[1].CurrentIndex);
        Assert.AreEqual("b", entries[1].BaselineCell!.Source);
        Assert.AreEqual(CellDiffKind.Unchanged, entries[2].Kind);
    }

    [TestMethod]
    public void Align_RemovedFirstCell_SplicedBeforeEverything()
    {
        var a = Cell("a");
        var b = Cell("b");
        var entries = CellAligner.Align(
            new[] { a, b },
            new[] { Clone(b) });

        Assert.AreEqual(2, entries.Count);
        Assert.AreEqual(CellDiffKind.Removed, entries[0].Kind);
        Assert.AreEqual(CellDiffKind.Unchanged, entries[1].Kind);
    }

    [TestMethod]
    public void Align_SourceEdited_SameId_ReportsModified()
    {
        var a = Cell("original");
        var entries = CellAligner.Align(
            new[] { a },
            new[] { Clone(a, source: "edited") });

        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual(CellDiffKind.Modified, entries[0].Kind);
        Assert.IsTrue(entries[0].SourceChanged);
        Assert.IsFalse(entries[0].OutputsChanged);
        Assert.IsFalse(entries[0].TypeOrLanguageChanged);
        Assert.IsFalse(entries[0].MatchedByContent);
        Assert.AreEqual("original", entries[0].BaselineCell!.Source);
        Assert.AreEqual("edited", entries[0].CurrentCell!.Source);
    }

    [TestMethod]
    public void Align_CellReordered_SameContent_ReportsMovedNotAddedRemoved()
    {
        var a = Cell("a");
        var b = Cell("b");
        var c = Cell("c");
        var entries = CellAligner.Align(
            new[] { a, b, c },
            new[] { Clone(c), Clone(a), Clone(b) });

        Assert.AreEqual(3, entries.Count);
        Assert.AreEqual(CellDiffKind.Moved, entries[0].Kind);
        Assert.AreEqual("c", entries[0].CurrentCell!.Source);
        Assert.AreEqual(2, entries[0].BaselineIndex);
        Assert.AreEqual(0, entries[0].CurrentIndex);
        Assert.AreEqual(CellDiffKind.Unchanged, entries[1].Kind);
        Assert.AreEqual(CellDiffKind.Unchanged, entries[2].Kind);
        Assert.IsFalse(entries.Any(e => e.Kind is CellDiffKind.Added or CellDiffKind.Removed));
    }

    [TestMethod]
    public void Align_MovedAndEdited_ReportsModified()
    {
        var a = Cell("a");
        var b = Cell("b");
        var c = Cell("c");
        var entries = CellAligner.Align(
            new[] { a, b, c },
            new[] { Clone(c, source: "c edited"), Clone(a), Clone(b) });

        var edited = entries.Single(e => e.CurrentCell?.Source == "c edited");
        Assert.AreEqual(CellDiffKind.Modified, edited.Kind);
        Assert.IsTrue(edited.SourceChanged);
    }

    [TestMethod]
    public void Align_NoStableIds_IdenticalContent_AllUnchangedByContentFallback()
    {
        var baseline = new[] { Cell("a"), Cell("b"), Cell("c") };
        var current = new[] { Cell("a"), Cell("b"), Cell("c") };

        var entries = CellAligner.Align(baseline, current);

        Assert.AreEqual(3, entries.Count);
        Assert.IsTrue(entries.All(e => e.Kind == CellDiffKind.Unchanged));
        Assert.IsTrue(entries.All(e => e.MatchedByContent));
    }

    [TestMethod]
    public void Align_NoStableIds_OneCellEdited_MiddleOfSequence_OnlyThatOneIsModified()
    {
        var baseline = new[] { Cell("a"), Cell("b"), Cell("c"), Cell("d"), Cell("e") };
        var current = new[] { Cell("a"), Cell("b"), Cell("c EDITED"), Cell("d"), Cell("e") };

        var entries = CellAligner.Align(baseline, current);

        Assert.AreEqual(5, entries.Count);
        Assert.AreEqual(1, entries.Count(e => e.Kind == CellDiffKind.Modified));
        Assert.AreEqual(4, entries.Count(e => e.Kind == CellDiffKind.Unchanged));
        var modified = entries.Single(e => e.Kind == CellDiffKind.Modified);
        Assert.AreEqual("c", modified.BaselineCell!.Source);
        Assert.AreEqual("c EDITED", modified.CurrentCell!.Source);
        Assert.IsTrue(modified.MatchedByContent);
    }

    [TestMethod]
    public void Align_BothSidesFreshGuids_AddAndRemoveAndEdit_StillAlignsByContent()
    {
        var baseline = new[] { Cell("a"), Cell("b"), Cell("c"), Cell("d") };
        var current = new[] { Cell("a"), Cell("b EDITED"), Cell("d"), Cell("new tail") };

        var entries = CellAligner.Align(baseline, current);

        Assert.AreEqual(1, entries.Count(e => e.Kind == CellDiffKind.Modified));
        Assert.AreEqual(1, entries.Count(e => e.Kind == CellDiffKind.Removed));
        Assert.AreEqual(1, entries.Count(e => e.Kind == CellDiffKind.Added));
        Assert.AreEqual(2, entries.Count(e => e.Kind == CellDiffKind.Unchanged));
        Assert.AreEqual("c", entries.Single(e => e.Kind == CellDiffKind.Removed).BaselineCell!.Source);
        Assert.AreEqual("new tail", entries.Single(e => e.Kind == CellDiffKind.Added).CurrentCell!.Source);
    }

    [TestMethod]
    public void Align_DuplicateIdenticalCells_PairsGreedilyWithoutCrossing()
    {
        var baseline = new[] { Cell("dup"), Cell("dup") };
        var current = new[] { Cell("dup"), Cell("dup") };

        var entries = CellAligner.Align(baseline, current);

        Assert.AreEqual(2, entries.Count);
        Assert.IsTrue(entries.All(e => e.Kind == CellDiffKind.Unchanged));
        Assert.AreEqual(0, entries[0].BaselineIndex);
        Assert.AreEqual(0, entries[0].CurrentIndex);
        Assert.AreEqual(1, entries[1].BaselineIndex);
        Assert.AreEqual(1, entries[1].CurrentIndex);
    }

    [TestMethod]
    public void Align_UnrelatedRemoveAndAddInSameGap_NotPairedAsModified()
    {
        var anchor = Cell("anchor");
        var entries = CellAligner.Align(
            new[] { anchor, Cell("var results = LoadResults();") },
            new[] { Clone(anchor), Cell("import matplotlib.pyplot as plt") });

        Assert.AreEqual(3, entries.Count);
        Assert.AreEqual(1, entries.Count(e => e.Kind == CellDiffKind.Removed));
        Assert.AreEqual(1, entries.Count(e => e.Kind == CellDiffKind.Added));
        Assert.AreEqual(0, entries.Count(e => e.Kind == CellDiffKind.Modified));
    }

    [TestMethod]
    public void Align_HeavyEditInSameGap_StillPairedAsModified()
    {
        var anchor = Cell("anchor");
        var entries = CellAligner.Align(
            new[] { anchor, Cell("var data = LoadData();\nConsole.WriteLine(data.Count);") },
            new[] { Clone(anchor), Cell("var data = LoadData().Where(d => d.IsValid).ToList();\nConsole.WriteLine(data.Count);") });

        Assert.AreEqual(2, entries.Count);
        var modified = entries.Single(e => e.Kind == CellDiffKind.Modified);
        Assert.IsTrue(modified.SourceChanged);
        Assert.IsTrue(modified.MatchedByContent);
    }

    [TestMethod]
    public void Align_EmptyBaseline_AllAdded()
    {
        var entries = CellAligner.Align(Array.Empty<CellModel>(), new[] { Cell("a"), Cell("b") });

        Assert.AreEqual(2, entries.Count);
        Assert.IsTrue(entries.All(e => e.Kind == CellDiffKind.Added));
    }

    [TestMethod]
    public void Align_EmptyCurrent_AllRemoved()
    {
        var entries = CellAligner.Align(new[] { Cell("a"), Cell("b") }, Array.Empty<CellModel>());

        Assert.AreEqual(2, entries.Count);
        Assert.IsTrue(entries.All(e => e.Kind == CellDiffKind.Removed));
    }

    [TestMethod]
    public void Align_OutputsOnlyChange_FlagsOutputsChangedOnly()
    {
        var a = Cell("a");
        a.Outputs.Add(CellOutput.Plain("old result"));
        var entries = CellAligner.Align(
            new[] { a },
            new[] { Clone(a, outputs: new List<CellOutput> { CellOutput.Plain("new result") }) });

        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual(CellDiffKind.Modified, entries[0].Kind);
        Assert.IsTrue(entries[0].OutputsChanged);
        Assert.IsFalse(entries[0].SourceChanged);
        Assert.IsFalse(entries[0].TypeOrLanguageChanged);
        Assert.IsFalse(entries[0].CellMetadataChanged);
    }

    private static IReadOnlySet<string> Transient(params string[] cellTypeIds)
        => new HashSet<string>(cellTypeIds, StringComparer.OrdinalIgnoreCase);

    [TestMethod]
    public void Align_TransientOutputType_GainsOutput_StaysUnchanged()
    {
        // A saved markup cell carries no outputs; the live notebook renders one when it opens.
        var saved = Cell("# heading", type: "markdown", language: null);
        var live = Clone(saved, outputs: new List<CellOutput> { CellOutput.Html("<h1>heading</h1>") });

        var entries = CellAligner.Align(new[] { saved }, new[] { live }, Transient("markdown"));

        Assert.AreEqual(CellDiffKind.Unchanged, entries[0].Kind);
        Assert.IsFalse(entries[0].OutputsChanged);
    }

    [TestMethod]
    public void Align_TransientOutputType_SourceChanged_StillModified()
    {
        var saved = Cell("# heading", type: "markdown", language: null);
        var live = Clone(saved, source: "# other heading",
            outputs: new List<CellOutput> { CellOutput.Html("<h1>other heading</h1>") });

        var entries = CellAligner.Align(new[] { saved }, new[] { live }, Transient("markdown"));

        Assert.AreEqual(CellDiffKind.Modified, entries[0].Kind);
        Assert.IsTrue(entries[0].SourceChanged);
        Assert.IsFalse(entries[0].OutputsChanged);
    }

    [TestMethod]
    public void Align_PersistingType_OutputsDiffer_StillModified()
    {
        var a = Cell("a");
        a.Outputs.Add(CellOutput.Plain("old result"));
        var edited = Clone(a, outputs: new List<CellOutput> { CellOutput.Plain("new result") });

        var entries = CellAligner.Align(new[] { a }, new[] { edited }, Transient("markdown"));

        Assert.AreEqual(CellDiffKind.Modified, entries[0].Kind);
        Assert.IsTrue(entries[0].OutputsChanged);
    }

    [TestMethod]
    public void Align_TransientTypeConvertedToKernelType_ReportsGainedOutputs()
    {
        var saved = Cell("# heading", type: "markdown", language: null);
        var converted = Clone(saved);
        converted.Type = "code";
        converted.Language = "csharp";
        converted.Outputs.Add(CellOutput.Plain("42"));

        var entries = CellAligner.Align(new[] { saved }, new[] { converted }, Transient("markdown"));

        Assert.AreEqual(CellDiffKind.Modified, entries[0].Kind);
        Assert.IsTrue(entries[0].TypeOrLanguageChanged);
        Assert.IsTrue(entries[0].OutputsChanged);
    }

    [TestMethod]
    public void Align_CellMetadataChange_FlagsCellMetadataChanged()
    {
        var a = Cell("a");
        a.Metadata["collapsed"] = true;
        var edited = Clone(a);
        edited.Metadata["collapsed"] = false;

        var entries = CellAligner.Align(new[] { a }, new[] { edited });

        Assert.AreEqual(CellDiffKind.Modified, entries[0].Kind);
        Assert.IsTrue(entries[0].CellMetadataChanged);
        Assert.IsFalse(entries[0].SourceChanged);
    }

    [TestMethod]
    public void Align_TypeChanged_SameId_FlagsTypeOrLanguageChanged()
    {
        var a = Cell("# heading", type: "code");
        var changed = Clone(a);
        changed.Type = "markdown";
        changed.Language = null;

        var entries = CellAligner.Align(new[] { a }, new[] { changed });

        Assert.AreEqual(CellDiffKind.Modified, entries[0].Kind);
        Assert.IsTrue(entries[0].TypeOrLanguageChanged);
    }
}
