using System.Text.Json;
using Verso.Abstractions;
using Verso.Showcase.SlideStudio;
using Verso.Showcase.SlideStudio.Models;
using Verso.Testing.Stubs;

namespace Verso.Showcase.SlideStudio.Tests;

[TestClass]
public sealed class SlideStudioLayoutTests
{
    private readonly SlideStudioLayout _layout = new();
    private readonly StubVersoContext _context = new();

    private static CellModel Cell(string type = "code", string source = "var x = 1;", bool withOutput = true)
    {
        var cell = new CellModel { Type = type, Source = source };
        if (withOutput)
            cell.Outputs.Add(new CellOutput("text/plain", "hello"));
        return cell;
    }

    private static CellModel CellWithFlags(PresenterFlags flags, string source = "var x = 1;", bool withOutput = true)
    {
        var cell = Cell(source: source, withOutput: withOutput);
        flags.Write(cell);
        return cell;
    }

    /// <summary>Round-trips cell metadata through JSON, the shape a load from disk produces.</summary>
    private static CellModel AsLoadedFromDisk(CellModel cell)
    {
        foreach (var key in cell.Metadata.Keys.ToList())
            cell.Metadata[key] = JsonSerializer.SerializeToElement(cell.Metadata[key]);
        return cell;
    }

    // --- Identity ---

    [TestMethod]
    public void Identity_IsSlideStudio()
    {
        Assert.AreEqual("com.verso.showcase.slide-studio", _layout.ExtensionId);
        Assert.AreEqual("slide-studio", _layout.LayoutId);
        Assert.AreEqual("Slide Studio", _layout.DisplayName);
        Assert.IsTrue(_layout.RequiresCustomRenderer);
        Assert.IsTrue(_layout.SupportsPropertiesPanel);
    }

    [TestMethod]
    public void Capabilities_EditAndExecuteWithNotebookEvents_NoStructuralEditing()
    {
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellEdit));
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellExecute));
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.NotebookEvents));
        Assert.IsFalse(_layout.Capabilities.HasFlag(LayoutCapabilities.CellInsert));
        Assert.IsFalse(_layout.Capabilities.HasFlag(LayoutCapabilities.CellDelete));
        Assert.IsFalse(_layout.Capabilities.HasFlag(LayoutCapabilities.CellReorder));
    }

    // --- PresenterFlags ---

    [TestMethod]
    public void PresenterFlags_NoMetadata_ReadsDefaults()
    {
        var flags = PresenterFlags.Read(Cell());
        Assert.IsTrue(flags.Include);
        Assert.IsFalse(flags.ShowSource);
        Assert.IsTrue(flags.ShowOutput);
    }

    [TestMethod]
    public void PresenterFlags_WriteThenRead_RoundTrips()
    {
        var cell = Cell();
        var flags = new PresenterFlags(Include: false, ShowSource: true, ShowOutput: false);

        flags.Write(cell);

        Assert.AreEqual(flags, PresenterFlags.Read(cell));
    }

    [TestMethod]
    public void PresenterFlags_ReadsJsonElementMetadata()
    {
        var cell = AsLoadedFromDisk(CellWithFlags(new PresenterFlags(false, true, true)));

        var flags = PresenterFlags.Read(cell);

        Assert.AreEqual(new PresenterFlags(false, true, true), flags);
    }

    [TestMethod]
    public void PresenterFlags_WritingDefaults_RemovesMetadataKey()
    {
        var cell = CellWithFlags(new PresenterFlags(false, false, false));
        Assert.IsTrue(cell.Metadata.ContainsKey(PresenterFlags.MetadataKey));

        PresenterFlags.Default.Write(cell);

        Assert.IsFalse(cell.Metadata.ContainsKey(PresenterFlags.MetadataKey));
    }

    // --- Slide selection ---

    [TestMethod]
    public void BuildSlideList_DefaultCellWithOutput_BecomesSlide()
    {
        var slides = SlideStudioLayout.BuildSlideList(new[] { Cell() });
        Assert.AreEqual(1, slides.Count);
    }

    [TestMethod]
    public void BuildSlideList_ExcludedCell_IsSkipped()
    {
        var slides = SlideStudioLayout.BuildSlideList(new[]
        {
            Cell(),
            CellWithFlags(PresenterFlags.Default with { Include = false }),
        });
        Assert.AreEqual(1, slides.Count);
    }

    [TestMethod]
    public void BuildSlideList_OutputOnlyCellWithoutOutputs_IsSkipped()
    {
        var slides = SlideStudioLayout.BuildSlideList(new[] { Cell(withOutput: false) });
        Assert.AreEqual(0, slides.Count);
    }

    [TestMethod]
    public void BuildSlideList_SourceOnlyCell_UsesSource()
    {
        var withSource = CellWithFlags(new PresenterFlags(true, true, false), withOutput: false);
        var withoutSource = CellWithFlags(new PresenterFlags(true, true, false), source: "  ", withOutput: false);

        var slides = SlideStudioLayout.BuildSlideList(new[] { withSource, withoutSource });

        Assert.AreEqual(1, slides.Count);
        Assert.AreEqual(withSource.Id, slides[0].Cell.Id);
    }

    [TestMethod]
    public void BuildSlideList_IncludedButNothingEnabled_IsSkipped()
    {
        var slides = SlideStudioLayout.BuildSlideList(new[]
        {
            CellWithFlags(new PresenterFlags(true, false, false)),
        });
        Assert.AreEqual(0, slides.Count);
    }

    // --- Cell interaction (checkbox channel) ---

    private static CellInteractionContext InteractionFor(CellModel cell, string payload)
    {
        var notebook = new NotebookModel();
        notebook.Cells.Add(cell);
        return new CellInteractionContext
        {
            InteractionType = "set-presenter-flag",
            Payload = payload,
            CellId = cell.Id,
            NotebookModel = notebook,
        };
    }

    [TestMethod]
    public async Task SetPresenterFlag_Include_WritesMetadataAndMarksDirty()
    {
        var cell = Cell();
        var context = InteractionFor(cell, "{\"flag\":\"include\",\"value\":false}");

        await _layout.OnCellInteractionAsync(context);

        Assert.IsTrue(context.StateChanged);
        Assert.IsFalse(PresenterFlags.Read(cell).Include);
    }

    [TestMethod]
    public async Task SetPresenterFlag_SourceAndOutput_UpdateTheirFlags()
    {
        var cell = Cell();

        await _layout.OnCellInteractionAsync(InteractionFor(cell, "{\"flag\":\"source\",\"value\":true}"));
        await _layout.OnCellInteractionAsync(InteractionFor(cell, "{\"flag\":\"output\",\"value\":false}"));

        var flags = PresenterFlags.Read(cell);
        Assert.IsTrue(flags.ShowSource);
        Assert.IsFalse(flags.ShowOutput);
    }

    [TestMethod]
    public async Task SetPresenterFlag_NoOpChange_DoesNotMarkDirty()
    {
        var cell = Cell();
        var context = InteractionFor(cell, "{\"flag\":\"include\",\"value\":true}");

        await _layout.OnCellInteractionAsync(context);

        Assert.IsFalse(context.StateChanged);
    }

    [TestMethod]
    public async Task SetPresenterFlag_MalformedPayload_IsIgnored()
    {
        var cell = Cell();
        var context = InteractionFor(cell, "not json");

        await _layout.OnCellInteractionAsync(context);

        Assert.IsFalse(context.StateChanged);
        Assert.AreEqual(PresenterFlags.Default, PresenterFlags.Read(cell));
    }

    // --- Properties panel provider ---

    [TestMethod]
    public async Task PropertiesSection_ExposesThreeTogglesWithCurrentValues()
    {
        var cell = CellWithFlags(new PresenterFlags(false, true, true));

        var section = await _layout.GetPropertiesSectionAsync(cell, null!);

        Assert.AreEqual(3, section.Fields.Count);
        Assert.IsTrue(section.Fields.All(f => f.FieldType == PropertyFieldType.Toggle));
        Assert.AreEqual(false, section.Fields.Single(f => f.Name == "include").CurrentValue);
        Assert.AreEqual(true, section.Fields.Single(f => f.Name == "showSource").CurrentValue);
    }

    [TestMethod]
    public async Task PropertyChanged_WritesTheSameMetadataAsTheCheckboxes()
    {
        var cell = Cell();

        await _layout.OnPropertyChangedAsync(cell, "showSource", true, null!);

        Assert.IsTrue(PresenterFlags.Read(cell).ShowSource);
    }

    // --- Layout interactions ---

    [TestMethod]
    public async Task SelectCell_ChangesActiveCellAndRequestsRender()
    {
        var cells = new[] { Cell(), Cell() };
        await _layout.RenderLayoutAsync(cells, _context);

        var rendered = false;
        await _layout.OnLayoutInteractionAsync(new LayoutInteractionContext
        {
            InteractionType = "select-cell",
            TargetId = cells[1].Id.ToString(),
            Verso = _context,
            RequestRender = () => rendered = true,
        });

        Assert.IsTrue(rendered);
        var container = await _layout.GetCellContainerAsync(cells[1].Id, _context);
        Assert.IsTrue(container.IsVisible);
        var other = await _layout.GetCellContainerAsync(cells[0].Id, _context);
        Assert.IsFalse(other.IsVisible);
    }

    [TestMethod]
    public async Task SetSplit_ClampsAndPersists()
    {
        await _layout.OnLayoutInteractionAsync(new LayoutInteractionContext
        {
            InteractionType = "set-split",
            Payload = "0.95",
            Verso = _context,
        });

        Assert.AreEqual(0.8, (double)_layout.GetLayoutMetadata()["splitRatio"], 0.0001);
    }

    // --- Layout metadata round trip ---

    [TestMethod]
    public async Task LayoutMetadata_RoundTripsThroughJsonElements()
    {
        var cellId = Guid.NewGuid();
        var saved = new Dictionary<string, object>
        {
            ["activeCell"] = cellId.ToString(),
            ["splitRatio"] = 0.7,
            ["lastSlide"] = 3,
        };
        var loaded = saved.ToDictionary(
            kv => kv.Key,
            kv => (object)JsonSerializer.SerializeToElement(kv.Value));

        await _layout.ApplyLayoutMetadata(loaded, _context);

        var metadata = _layout.GetLayoutMetadata();
        Assert.AreEqual(cellId.ToString(), metadata["activeCell"]);
        Assert.AreEqual(0.7, (double)metadata["splitRatio"], 0.0001);
        Assert.AreEqual(3, metadata["lastSlide"]);
    }

    // --- Render smoke ---

    [TestMethod]
    public async Task Render_EmitsFilmstripWorkspaceAndPresenterDeck()
    {
        var included = Cell();
        var excluded = CellWithFlags(PresenterFlags.Default with { Include = false });
        var result = await _layout.RenderLayoutAsync(new[] { included, excluded }, _context);

        StringAssert.Contains(result.Content, "vss-root");

        // Both cells get filmstrip tiles; the excluded one is dimmed.
        StringAssert.Contains(result.Content, $"data-tile-cell=\"{included.Id}\"");
        StringAssert.Contains(result.Content, $"data-tile-cell=\"{excluded.Id}\"");
        StringAssert.Contains(result.Content, "is-excluded");

        // The active (first) cell gets the editor slot; the excluded cell, being neither
        // active nor a slide, gets no slot at all.
        StringAssert.Contains(result.Content, $"data-cell-slot=\"{included.Id}\"");
        Assert.IsFalse(result.Content.Contains($"data-cell-slot=\"{excluded.Id}\""));

        // The presenter deck holds exactly the included cell's slide. The slide hosts
        // the live cell through a portal slot, plus a script-free fallback copy that
        // CSS shows only when the live cell renders no output element.
        StringAssert.Contains(result.Content, $"data-slide-cell=\"{included.Id}\"");
        StringAssert.Contains(result.Content, $"vss-slide-live\" data-cell-slot=\"{included.Id}\"");
        StringAssert.Contains(result.Content, "vss-slide-copy");
        Assert.IsFalse(result.Content.Contains($"data-slide-cell=\"{excluded.Id}\""));
    }

    [TestMethod]
    public async Task Render_OutputPane_FallbackCopyUnderTheLiveOverlay()
    {
        // With output present the body holds a static fallback copy; the live output
        // element overlays and covers it whenever the live element exists (markdown
        // cells being edited and collapsed outputs are the states that expose it).
        var withOutput = await _layout.RenderLayoutAsync(new[] { Cell() }, _context);
        StringAssert.Contains(withOutput.Content, "vss-output-body\"><div class=\"vss-out vss-out--text\"");
        Assert.IsFalse(withOutput.Content.Contains("No output yet"));

        // Without output the body shows the empty-state hint.
        var noOutput = await new SlideStudioLayout()
            .RenderLayoutAsync(new[] { Cell(withOutput: false) }, _context);
        StringAssert.Contains(noOutput.Content, "No output yet");
    }

    [TestMethod]
    public async Task Render_ScriptDrivenOutput_IsNeverCopied()
    {
        var cell = new CellModel { Type = "code", Source = "chart" };
        cell.Outputs.Add(new CellOutput(
            "text/html", "<div id='plot'></div><script>window.__marker__ = 1;</script>"));

        var result = await new SlideStudioLayout().RenderLayoutAsync(new[] { cell }, _context);

        // The filmstrip shows a placeholder, the output pane body stays empty (no
        // fallback copy), and no copy of the script markup exists anywhere in the
        // layout HTML (a copy would be inert and its duplicate element ids would
        // hijack renders aimed at the live output).
        StringAssert.Contains(result.Content, "vss-tile-interactive");
        StringAssert.Contains(result.Content, "vss-output-body\"></div>");
        Assert.IsFalse(result.Content.Contains("__marker__"));

        // The slide presents the live cell through its portal slot, and a script-only
        // output gets no fallback copy either.
        StringAssert.Contains(result.Content, $"vss-slide-live\" data-cell-slot=\"{cell.Id}\"");
        Assert.IsFalse(result.Content.Contains("vss-slide-copy"));
    }

    [TestMethod]
    public async Task Render_RemovedActiveCell_FallsBackToFirstCell()
    {
        var cells = new List<CellModel> { Cell(), Cell() };
        await _layout.RenderLayoutAsync(cells, _context);

        await _layout.OnLayoutInteractionAsync(new LayoutInteractionContext
        {
            InteractionType = "select-cell",
            TargetId = cells[1].Id.ToString(),
            Verso = _context,
        });
        await _layout.OnCellRemovedAsync(cells[1].Id, _context);
        cells.RemoveAt(1);

        var result = await _layout.RenderLayoutAsync(cells, _context);
        StringAssert.Contains(result.Content, $"data-cell-slot=\"{cells[0].Id}\"");
    }
}
