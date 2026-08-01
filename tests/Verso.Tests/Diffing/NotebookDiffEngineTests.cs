using Verso.Abstractions;
using Verso.Diffing;
using Verso.Extensions.CellTypes;

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
    public void Compute_ParameterAdded_Reported()
    {
        var baseline = Notebook();
        var current = Notebook();
        current.Parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["region"] = new() { Type = "string", Default = "us-east" },
        };

        var result = NotebookDiffEngine.Compute(baseline, current, "Last Saved");

        var change = result.MetadataChanges.Single();
        Assert.AreEqual("Parameter 'region'", change.Field);
        Assert.IsNull(change.BaselineValue);
        StringAssert.Contains(change.CurrentValue, "us-east");
    }

    [TestMethod]
    public void Compute_ParameterDefaultChanged_Reported()
    {
        var baseline = Notebook();
        baseline.Parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["limit"] = new() { Type = "int", Default = 10 },
        };
        var current = Notebook();
        current.Parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["limit"] = new() { Type = "int", Default = 25 },
        };

        var result = NotebookDiffEngine.Compute(baseline, current, "Last Saved");

        var change = result.MetadataChanges.Single();
        Assert.AreEqual("Parameter 'limit'", change.Field);
        StringAssert.Contains(change.BaselineValue, "10");
        StringAssert.Contains(change.CurrentValue, "25");
    }

    [TestMethod]
    public void Compute_ExtensionSettingChanged_ReportedPerSetting()
    {
        var baseline = Notebook();
        baseline.ExtensionSettings["verso.charts"] = new Dictionary<string, object?>
        {
            ["palette"] = "classic",
            ["gridlines"] = true,
        };
        var current = Notebook();
        current.ExtensionSettings["verso.charts"] = new Dictionary<string, object?>
        {
            ["palette"] = "vivid",
            ["gridlines"] = true,
        };

        var result = NotebookDiffEngine.Compute(baseline, current, "Last Saved");

        var change = result.MetadataChanges.Single();
        Assert.AreEqual("Extension setting 'verso.charts.palette'", change.Field);
        StringAssert.Contains(change.BaselineValue, "classic");
        StringAssert.Contains(change.CurrentValue, "vivid");
    }

    [TestMethod]
    public void Compute_LayoutStateChanged_ReportedPerLayout()
    {
        var baseline = Notebook();
        baseline.Layouts["acme.studio:studio"] = new Dictionary<string, object> { ["document"] = "{\"layers\":1}" };
        var current = Notebook();
        current.Layouts["acme.studio:studio"] = new Dictionary<string, object> { ["document"] = "{\"layers\":2}" };

        var result = NotebookDiffEngine.Compute(baseline, current, "Last Saved");

        var change = result.MetadataChanges.Single();
        Assert.AreEqual("Layout state 'acme.studio:studio'", change.Field);
        StringAssert.Contains(change.BaselineValue, "layers");
    }

    [TestMethod]
    public void Compute_LayoutStateSameContentDifferentKeyOrder_NotReported()
    {
        var baseline = Notebook();
        baseline.Layouts["dashboard"] = new Dictionary<string, object> { ["rows"] = 2, ["cols"] = 3 };
        var current = Notebook();
        current.Layouts["dashboard"] = new Dictionary<string, object> { ["cols"] = 3, ["rows"] = 2 };

        var result = NotebookDiffEngine.Compute(baseline, current, "Last Saved");

        Assert.AreEqual(0, result.MetadataChanges.Count);
    }

    [TestMethod]
    public void Compute_LayoutStateBareBaselineKey_QualifiedCurrentKey_SameContent_NotReported()
    {
        var baseline = Notebook();
        baseline.Layouts["dashboard"] = new Dictionary<string, object> { ["rows"] = 2 };
        var current = Notebook();
        current.Layouts["verso.layout.dashboard:dashboard"] = new Dictionary<string, object> { ["rows"] = 2 };

        var result = NotebookDiffEngine.Compute(baseline, current, "Last Saved");

        Assert.AreEqual(0, result.MetadataChanges.Count,
            "A legacy bare layout key promoted to its qualified form must not read as a change.");
    }

    [TestMethod]
    public void Compute_LayoutStateLargeValue_TruncatedInReport()
    {
        var baseline = Notebook();
        var current = Notebook();
        current.Layouts["acme.studio:studio"] = new Dictionary<string, object>
        {
            ["document"] = new string('x', 5000),
        };

        var result = NotebookDiffEngine.Compute(baseline, current, "Last Saved");

        var change = result.MetadataChanges.Single();
        Assert.IsNotNull(change.CurrentValue);
        Assert.IsTrue(change.CurrentValue.Length < 200,
            $"Large values must be truncated for display, got {change.CurrentValue.Length} chars.");
        StringAssert.EndsWith(change.CurrentValue, "...");
    }

    [TestMethod]
    public void Compute_JsonElementBaseline_ClrCurrent_SameSettings_NotReported()
    {
        var baseline = Notebook();
        baseline.ExtensionSettings["verso.charts"] = new Dictionary<string, object?>
        {
            ["limit"] = System.Text.Json.JsonSerializer.Deserialize<object>("25"),
            ["palette"] = System.Text.Json.JsonSerializer.Deserialize<object>("\"vivid\""),
        };
        var current = Notebook();
        current.ExtensionSettings["verso.charts"] = new Dictionary<string, object?>
        {
            ["limit"] = 25,
            ["palette"] = "vivid",
        };

        var result = NotebookDiffEngine.Compute(baseline, current, "Last Saved");

        Assert.AreEqual(0, result.MetadataChanges.Count,
            "A deserialized baseline (JsonElement values) must compare equal to the live model (CLR values).");
    }

    [TestMethod]
    public async Task Compute_SerializeRoundTrip_NoMetadataNoise()
    {
        var original = Notebook(Cell("print(1)"));
        original.Title = "Round Trip";
        original.Parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["limit"] = new() { Type = "int", Default = 10, Required = true },
        };
        original.ExtensionSettings["verso.charts"] = new Dictionary<string, object?> { ["palette"] = "vivid" };
        original.Layouts["acme.studio:studio"] = new Dictionary<string, object> { ["document"] = "{\"layers\":[1,2]}" };

        var serializer = new Verso.Serializers.VersoSerializer();
        var roundTripped = await serializer.DeserializeAsync(await serializer.SerializeAsync(original));

        var result = NotebookDiffEngine.Compute(roundTripped, original, "Last Saved");

        Assert.AreEqual(0, result.MetadataChanges.Count,
            "Comparing a notebook against its own serialized form must report no metadata changes: " +
            string.Join("; ", result.MetadataChanges.Select(c => c.Field)));
    }

    [TestMethod]
    public async Task Compute_SaveRoundTrip_RenderedMarkdownCell_ReportsNoChange()
    {
        // What a host actually holds after opening a notebook: markdown rendered into an output
        // that the save path deliberately never writes to disk.
        var cellTypes = new ICellType[] { new MarkdownCellType() };
        var markdown = new CellModel { Id = Guid.NewGuid(), Type = "markdown", Source = "# Heading" };
        markdown.Outputs.Add(CellOutput.Html("<h1>Heading</h1>"));
        var code = Cell("print(1)");
        code.Outputs.Add(CellOutput.Plain("1"));
        var live = Notebook(markdown, code);

        var serializer = new Verso.Serializers.VersoSerializer(cellTypes);
        var saved = await serializer.DeserializeAsync(await serializer.SerializeAsync(live));

        var result = NotebookDiffEngine.Compute(saved, live, "Last Saved", cellTypes);

        Assert.AreEqual(0, result.Summary.Modified,
            "Comparing a notebook against its own saved file must report no cell changes; " +
            "markdown outputs are rendered on open and never persisted.");
        Assert.AreEqual(2, result.Summary.Unchanged);
    }

    [TestMethod]
    public async Task Compute_SaveRoundTrip_NoCellTypeRegistry_StillComparesAllOutputs()
    {
        var cellTypes = new ICellType[] { new MarkdownCellType() };
        var markdown = new CellModel { Id = Guid.NewGuid(), Type = "markdown", Source = "# Heading" };
        markdown.Outputs.Add(CellOutput.Html("<h1>Heading</h1>"));
        var live = Notebook(markdown);

        var serializer = new Verso.Serializers.VersoSerializer(cellTypes);
        var saved = await serializer.DeserializeAsync(await serializer.SerializeAsync(live));

        var result = NotebookDiffEngine.Compute(saved, live, "Last Saved");

        Assert.AreEqual(1, result.Summary.Modified,
            "Without a registry the comparison cannot know which outputs are derived, so it " +
            "must keep comparing them all.");
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
