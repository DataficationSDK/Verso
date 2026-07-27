using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Verso.Abstractions;
using Verso.Showcase.SlideStudio.Models;

namespace Verso.Showcase.SlideStudio;

/// <summary>
/// A slide-deck authoring layout. The editing view pairs a filmstrip of cell previews
/// with a splitter-divided workspace: the live cell editor on the left, its rendered
/// output on the right. Presenter view turns the included cells into a full-screen,
/// read-only deck navigated with the arrow keys.
///
/// Three checkboxes drive what the presenter shows, persisted per cell under
/// <see cref="PresenterFlags.MetadataKey"/>: the filmstrip checkbox includes or hides
/// the cell (a hidden slide), and the editor and output pane checkboxes choose whether
/// the slide shows the cell's source, its output, or both stacked. The same flags are
/// exposed through the cell properties panel, so either surface edits the same state.
///
/// Flag changes travel over the cell interaction channel (not the layout channel) on
/// purpose: cell interactions carry a dirty signal, so toggling a checkbox marks the
/// notebook edited in every host. Layout-only view state (active cell, splitter ratio,
/// last presented slide) persists through the layout metadata round trip instead.
/// </summary>
[VersoExtension]
public sealed class SlideStudioLayout :
    ILayoutEngine, ILayoutInteractionHandler, ICellInteractionHandler, ICellPropertyProvider
{
    private const double MinSplit = 0.2;
    private const double MaxSplit = 0.8;

    private readonly object _gate = new();
    private Guid? _activeCellId;
    private double _splitRatio = 0.55;
    private int _lastSlide;

    // --- IExtension ---

    public string ExtensionId => "com.verso.showcase.slide-studio";
    public string Name => "Slide Studio";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description =>
        "Slide-deck authoring layout: a filmstrip of cell previews, an editor and output " +
        "split view, and a full-screen presenter mode with per-cell source/output control.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- ILayoutEngine ---

    public string LayoutId => "slide-studio";
    public string DisplayName => "Slide Studio";
    public string? Icon => null;
    public bool RequiresCustomRenderer => true;

    /// <summary>
    /// The workspace edits and runs cells, and the script needs notebook events to
    /// refresh filmstrip previews and the output pane after executions. Insert, delete,
    /// and reorder are left to the notebook layout; a deck is arranged, not restructured.
    /// </summary>
    public LayoutCapabilities Capabilities =>
        LayoutCapabilities.CellEdit |
        LayoutCapabilities.CellExecute |
        LayoutCapabilities.NotebookEvents;

    public bool SupportsPropertiesPanel => true;

    private string RootPrefix =>
        $".verso-layout-root[data-extension-id=\"{ExtensionId}\"][data-layout-id=\"{LayoutId}\"] ";

    public Task<IReadOnlyList<LayoutStaticAsset>?> GetStaticAssetsAsync(
        IVersoContext context, LayoutHostCapabilities hostCapabilities)
    {
        var assets = new List<LayoutStaticAsset>();
        if (hostCapabilities.SupportedAssetContentTypes.Contains("text/css"))
        {
            assets.Add(new LayoutStaticAsset(
                AssetId: "slide-studio.css",
                ContentType: "text/css",
                Content: Encoding.UTF8.GetBytes(SlideStudioStyles.BuildCss(RootPrefix))));
        }
        if (hostCapabilities.SupportedAssetContentTypes.Contains("text/javascript"))
        {
            assets.Add(new LayoutStaticAsset(
                AssetId: "slide-studio.js",
                ContentType: "text/javascript",
                Content: Encoding.UTF8.GetBytes(SlideStudioStyles.BuildJs(ExtensionId, LayoutId)))
            {
                LoadHints = new LayoutStaticAssetLoadHints(
                    ModuleKind: LayoutScriptModuleKind.Classic,
                    LoadMode: LayoutScriptLoadMode.Defer,
                    Placement: LayoutScriptPlacement.AfterLayoutHtml),
            });
        }
        return Task.FromResult<IReadOnlyList<LayoutStaticAsset>?>(assets.Count == 0 ? null : assets);
    }

    public Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context)
    {
        Guid? activeId;
        double split;
        int lastSlide;
        lock (_gate)
        {
            activeId = _activeCellId;
            split = _splitRatio;
            lastSlide = _lastSlide;
        }

        // The remembered active cell may have been deleted, or nothing selected yet.
        if (activeId is null || cells.All(c => c.Id != activeId))
            activeId = cells.Count > 0 ? cells[0].Id : null;
        lock (_gate) _activeCellId = activeId;

        var slides = BuildSlideList(cells);
        var initialSlide = slides.Count == 0 ? 0 : Math.Clamp(lastSlide, 0, slides.Count - 1);

        var sb = new StringBuilder(16 * 1024);
        sb.Append("<div class=\"vss-root\" data-initial-slide=\"")
          .Append(initialSlide).Append("\">");

        AppendToolbar(sb, cells, slides.Count);

        // Order matters here: the workspace renders before the presenter deck, and the
        // host's portal fills slots in document order, so the active cell lands in the
        // editor slot rather than its (hidden) slide slot. The layout script hands the
        // active cell over to its slide while presenting and returns it afterwards.
        sb.Append("<div class=\"vss-body\">");
        AppendFilmstrip(sb, cells, activeId);
        AppendWorkspace(sb, cells, activeId, split);
        sb.Append("</div>");

        AppendPresenter(sb, slides);

        sb.Append("</div>");
        return Task.FromResult(new RenderResult("text/html", sb.ToString()));
    }

    public Task<CellContainerInfo> GetCellContainerAsync(Guid cellId, IVersoContext context)
    {
        bool isActive;
        lock (_gate) isActive = _activeCellId == cellId;
        return Task.FromResult(new CellContainerInfo(cellId, 0, 0, 900, 400, IsVisible: isActive));
    }

    public Task OnCellAddedAsync(Guid cellId, int index, IVersoContext context) => Task.CompletedTask;

    public Task OnCellRemovedAsync(Guid cellId, IVersoContext context)
    {
        lock (_gate)
        {
            if (_activeCellId == cellId)
                _activeCellId = null;
        }
        return Task.CompletedTask;
    }

    public Task OnCellMovedAsync(Guid cellId, int newIndex, IVersoContext context) => Task.CompletedTask;

    public Dictionary<string, object> GetLayoutMetadata()
    {
        lock (_gate)
        {
            var metadata = new Dictionary<string, object>
            {
                ["splitRatio"] = _splitRatio,
                ["lastSlide"] = _lastSlide,
            };
            if (_activeCellId is { } active)
                metadata["activeCell"] = active.ToString();
            return metadata;
        }
    }

    public Task ApplyLayoutMetadata(Dictionary<string, object> metadata, IVersoContext context)
    {
        lock (_gate)
        {
            if (metadata.TryGetValue("activeCell", out var cellObj))
            {
                var text = cellObj is JsonElement { ValueKind: JsonValueKind.String } je
                    ? je.GetString()
                    : cellObj?.ToString();
                if (Guid.TryParse(text, out var id))
                    _activeCellId = id;
            }

            if (metadata.TryGetValue("splitRatio", out var splitObj) && ReadDouble(splitObj) is { } split)
                _splitRatio = Math.Clamp(split, MinSplit, MaxSplit);

            if (metadata.TryGetValue("lastSlide", out var slideObj) && ReadInt(slideObj) is { } slide && slide >= 0)
                _lastSlide = slide;
        }
        return Task.CompletedTask;
    }

    // --- ILayoutInteractionHandler ---

    public Task OnLayoutInteractionAsync(LayoutInteractionContext context)
    {
        switch (context.InteractionType)
        {
            case "select-cell":
                if (Guid.TryParse(context.TargetId, out var cellId))
                {
                    bool changed;
                    lock (_gate)
                    {
                        changed = _activeCellId != cellId;
                        _activeCellId = cellId;
                    }
                    if (changed)
                        context.RequestRender();
                }
                break;

            case "set-split":
                // The client already applied the ratio to the live DOM; storing it is
                // enough, and skipping the re-render keeps the drag from disturbing an
                // open editor.
                if (ReadDouble(context.Payload) is { } ratio)
                {
                    lock (_gate) _splitRatio = Math.Clamp(ratio, MinSplit, MaxSplit);
                }
                break;

            case "presenter-closed":
                if (ReadInt(context.Payload) is { } slide && slide >= 0)
                {
                    lock (_gate) _lastSlide = slide;
                }
                break;
        }
        return Task.CompletedTask;
    }

    // --- ICellInteractionHandler ---

    /// <summary>
    /// Handles the layout script's checkbox toggles. The flags land in cell metadata and
    /// <see cref="CellInteractionContext.StateChanged"/> is set, so the host marks the
    /// notebook dirty and the change survives a save.
    /// </summary>
    public Task<string?> OnCellInteractionAsync(CellInteractionContext context)
    {
        if (context.InteractionType != "set-presenter-flag")
            return Task.FromResult<string?>(null);

        var cell = context.NotebookModel?.Cells.FirstOrDefault(c => c.Id == context.CellId);
        if (cell is null)
            return Task.FromResult<string?>(null);

        string? flag = null;
        bool? value = null;
        try
        {
            using var doc = JsonDocument.Parse(context.Payload);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("flag", out var f))
                    flag = f.GetString();
                if (doc.RootElement.TryGetProperty("value", out var v)
                    && v.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    value = v.GetBoolean();
            }
        }
        catch (JsonException)
        {
            return Task.FromResult<string?>(null);
        }

        if (flag is null || value is null)
            return Task.FromResult<string?>(null);

        var flags = PresenterFlags.Read(cell);
        var updated = flag switch
        {
            "include" => flags with { Include = value.Value },
            "source" => flags with { ShowSource = value.Value },
            "output" => flags with { ShowOutput = value.Value },
            _ => flags,
        };

        if (updated != flags)
        {
            updated.Write(cell);
            context.StateChanged = true;
        }
        return Task.FromResult<string?>(null);
    }

    // --- ICellPropertyProvider ---

    public int Order => 40;

    public bool AppliesTo(CellModel cell, ICellRenderContext context) => true;

    public Task<PropertySection> GetPropertiesSectionAsync(CellModel cell, ICellRenderContext context)
    {
        var flags = PresenterFlags.Read(cell);
        var fields = new List<PropertyField>
        {
            new("include", "Include in presenter", PropertyFieldType.Toggle, flags.Include,
                "Whether this cell appears as a slide in presenter view."),
            new("showSource", "Show source on slide", PropertyFieldType.Toggle, flags.ShowSource,
                "Show the cell's source on its slide."),
            new("showOutput", "Show output on slide", PropertyFieldType.Toggle, flags.ShowOutput,
                "Show the cell's output on its slide."),
        };
        return Task.FromResult(new PropertySection(
            "Presenter", "How this cell appears in Slide Studio's presenter view.", fields));
    }

    public Task OnPropertyChangedAsync(CellModel cell, string propertyName, object? value, ICellRenderContext context)
    {
        var flags = PresenterFlags.Read(cell);
        var toggled = ReadBool(value);
        var updated = propertyName switch
        {
            "include" => flags with { Include = toggled },
            "showSource" => flags with { ShowSource = toggled },
            "showOutput" => flags with { ShowOutput = toggled },
            _ => flags,
        };
        updated.Write(cell);
        return Task.CompletedTask;
    }

    // --- Slide selection -----------------------------------------------------------------

    /// <summary>
    /// A cell becomes a slide when it is included and its flags leave something to show:
    /// source, output, or both. Included cells whose enabled parts are empty (no source,
    /// or output requested but never produced) are skipped rather than presented blank.
    /// </summary>
    public static IReadOnlyList<(CellModel Cell, PresenterFlags Flags)> BuildSlideList(
        IReadOnlyList<CellModel> cells)
    {
        var slides = new List<(CellModel, PresenterFlags)>();
        foreach (var cell in cells)
        {
            var flags = PresenterFlags.Read(cell);
            if (!flags.ShowsAnything)
                continue;

            var hasSource = flags.ShowSource && !string.IsNullOrWhiteSpace(cell.Source);
            var hasOutput = flags.ShowOutput && cell.Outputs.Count > 0;
            if (hasSource || hasOutput)
                slides.Add((cell, flags));
        }
        return slides;
    }

    // --- HTML builders ---------------------------------------------------------------------

    private static void AppendToolbar(StringBuilder sb, IReadOnlyList<CellModel> cells, int slideCount)
    {
        sb.Append("<div class=\"vss-toolbar\">")
          .Append("<div class=\"vss-title\"><span class=\"vss-logo\">&#9654;</span> Slide Studio</div>")
          .Append("<div class=\"vss-toolbar-right\">")
          .Append("<span class=\"vss-deck-count\">")
          .Append(slideCount).Append(slideCount == 1 ? " slide" : " slides")
          .Append("</span>")
          .Append("<button type=\"button\" class=\"vss-btn vss-btn--primary vss-present\"")
          .Append(slideCount == 0 ? " disabled title=\"No cells are included in the presenter view\"" : "")
          .Append(">Present</button>")
          .Append("</div></div>");
    }

    private static void AppendFilmstrip(StringBuilder sb, IReadOnlyList<CellModel> cells, Guid? activeId)
    {
        sb.Append("<div class=\"vss-filmstrip\">");

        if (cells.Count == 0)
        {
            sb.Append("<div class=\"vss-empty\">This notebook has no cells yet. ")
              .Append("Switch to the Notebook layout to add some.</div>");
        }

        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var flags = PresenterFlags.Read(cell);
            sb.Append("<div class=\"vss-tile")
              .Append(cell.Id == activeId ? " is-active" : "")
              .Append(flags.Include ? "" : " is-excluded")
              .Append("\" data-tile-cell=\"").Append(cell.Id).Append("\">");

            sb.Append("<label class=\"vss-tile-include\" title=\"Include in presenter view\">")
              .Append("<input type=\"checkbox\" data-flag=\"include\" data-flag-cell=\"")
              .Append(cell.Id).Append('"').Append(flags.Include ? " checked" : "").Append("></label>");

            sb.Append("<span class=\"vss-tile-index\">").Append(i + 1).Append("</span>");

            sb.Append("<div class=\"vss-tile-preview\"><div class=\"vss-tile-scale\">");
            AppendTilePreview(sb, cell);
            sb.Append("</div></div>");

            sb.Append("<div class=\"vss-tile-caption\">")
              .Append(WebUtility.HtmlEncode(CaptionFor(cell)))
              .Append("</div>");

            sb.Append("</div>");
        }

        sb.Append("</div>");
    }

    private void AppendWorkspace(StringBuilder sb, IReadOnlyList<CellModel> cells, Guid? activeId, double split)
    {
        // One custom property drives the grid template and the output overlay's left
        // edge (see the stylesheet), so the two can never drift apart during a drag.
        var ratio = split.ToString("0.###", CultureInfo.InvariantCulture);
        sb.Append("<div class=\"vss-main\" style=\"--vss-split:").Append(ratio).Append(";\">");

        var active = activeId is { } id ? cells.FirstOrDefault(c => c.Id == id) : null;
        var flags = active is null ? PresenterFlags.Default : PresenterFlags.Read(active);

        // Editor pane: the live cell is portaled into the slot. Its rendered output
        // section is not hidden or copied; scoped CSS lays it over the output pane, so
        // the right half of the workspace IS the live Blazor-rendered output element.
        // Scripts in rich outputs, charts, and interactive HTML behave exactly as they
        // do in the notebook layout. Serializing a copy here instead would strand
        // script-driven outputs: layout HTML is injected via innerHTML, which never
        // executes script tags.
        sb.Append("<div class=\"vss-pane vss-pane--editor\">")
          .Append("<div class=\"vss-pane-header\"><span class=\"vss-pane-name\">Cell</span>");
        if (active is not null)
        {
            sb.Append("<label class=\"vss-pane-check\" title=\"Show this cell's source on its slide\">")
              .Append("<input type=\"checkbox\" data-flag=\"source\" data-flag-cell=\"")
              .Append(active.Id).Append('"').Append(flags.ShowSource ? " checked" : "")
              .Append("> Source on slide</label>");
        }
        sb.Append("</div>");
        if (active is not null)
            sb.Append("<div class=\"vss-editor-slot\" data-cell-slot=\"").Append(active.Id).Append("\"></div>");
        else
            sb.Append("<div class=\"vss-pane-empty\">Select a cell in the filmstrip.</div>");
        sb.Append("</div>");

        sb.Append("<div class=\"vss-splitter\" role=\"separator\" aria-orientation=\"vertical\"></div>");

        // Output pane: the live cell's output element is overlaid on this pane by the
        // stylesheet, and that overlay is the pane's real content. The body renders a
        // static fallback underneath for the states where the live element does not
        // exist (see below); whenever the live element exists, its opaque overlay
        // covers the fallback completely.
        sb.Append("<div class=\"vss-pane vss-pane--output\">")
          .Append("<div class=\"vss-pane-header\"><span class=\"vss-pane-name\">Output</span>");
        if (active is not null)
        {
            sb.Append("<label class=\"vss-pane-check\" title=\"Show this cell's output on its slide\">")
              .Append("<input type=\"checkbox\" data-flag=\"output\" data-flag-cell=\"")
              .Append(active.Id).Append('"').Append(flags.ShowOutput ? " checked" : "")
              .Append("> Output on slide</label>");
        }
        sb.Append("</div><div class=\"vss-output-body\">");
        if (active is null || active.Outputs.Count == 0)
        {
            sb.Append("<div class=\"vss-pane-empty\">No output yet. Run the cell to see its result here.</div>");
        }
        else
        {
            // The fallback: markdown-style cells only materialize their live output
            // element in preview mode (while editing, the editor stands in for it), and
            // a collapsed output section renders a summary chip instead. In those states
            // this copy is what the pane shows. Script-driven outputs are never copied
            // (their scripts would be inert and their duplicate element ids would hijack
            // renders aimed at the live element); the live overlay is their only
            // correct rendering.
            foreach (var output in active.Outputs)
            {
                if (!IsScriptDriven(output))
                    AppendOutput(sb, output);
            }
        }
        sb.Append("</div></div>");

        sb.Append("</div>");
    }

    private static void AppendPresenter(
        StringBuilder sb, IReadOnlyList<(CellModel Cell, PresenterFlags Flags)> slides)
    {
        sb.Append("<div class=\"vss-presenter\" hidden>");
        sb.Append("<div class=\"vss-slides\">");

        foreach (var (cell, flags) in slides)
        {
            // Each slide lays out on a fixed-width canvas the script scales to the
            // viewport, so a deck looks the same at any resolution without re-running
            // anything.
            sb.Append("<section class=\"vss-slide\" data-slide-cell=\"").Append(cell.Id).Append("\">")
              .Append("<div class=\"vss-slide-fit\">");

            if (flags.ShowSource && !string.IsNullOrWhiteSpace(cell.Source))
            {
                sb.Append("<div class=\"vss-slide-source\"><pre>")
                  .Append(WebUtility.HtmlEncode(cell.Source))
                  .Append("</pre></div>");
            }

            if (flags.ShowOutput && cell.Outputs.Count > 0)
            {
                // A real portal slot, not a serialized copy. The host moves the live cell
                // into the slide, so script-driven and interactive outputs present exactly
                // as they rendered; slide CSS strips the editing chrome and leaves the
                // outputs. A copy would be inert (innerHTML never executes scripts) and
                // its duplicate element ids would hijack renders aimed at the live output.
                sb.Append("<div class=\"vss-slide-live\" data-cell-slot=\"")
                  .Append(cell.Id).Append("\"></div>");

                // The same fallback the output pane uses: the live cell has no output
                // element while a markdown cell is being edited or an output section is
                // collapsed, so a script-free copy stands in. CSS shows it only in that
                // state (and hides the empty slot), so the two can never double up.
                var copies = cell.Outputs.Where(o => !IsScriptDriven(o)).ToList();
                if (copies.Count > 0)
                {
                    sb.Append("<div class=\"vss-slide-copy\">");
                    foreach (var output in copies)
                        AppendOutput(sb, output);
                    sb.Append("</div>");
                }
            }

            sb.Append("</div></section>");
        }

        sb.Append("</div>");

        sb.Append("<div class=\"vss-presenter-hud\">")
          .Append("<button type=\"button\" class=\"vss-hud-btn\" data-nav=\"prev\" title=\"Previous slide\">&#x25C0;</button>")
          .Append("<span class=\"vss-slide-counter\"></span>")
          .Append("<button type=\"button\" class=\"vss-hud-btn\" data-nav=\"next\" title=\"Next slide\">&#x25B6;</button>")
          .Append("<button type=\"button\" class=\"vss-hud-btn vss-hud-exit\" data-nav=\"exit\" title=\"Exit (Esc)\">&#x2715;</button>")
          .Append("</div>");

        sb.Append("</div>");
    }

    /// <summary>
    /// A cheap static preview for a filmstrip tile: the first meaningful output, or the
    /// source when the cell has never produced one. The tile CSS scales this down and
    /// disables pointer events, so heavy interactivity is not a concern. Script-driven
    /// HTML outputs get a labeled placeholder instead of a copy: the copy's scripts
    /// would be inert, and its duplicate element ids would steal renders (a chart
    /// library resolves its target by id and finds the first match in the document).
    /// </summary>
    private static void AppendTilePreview(StringBuilder sb, CellModel cell)
    {
        var output = cell.Outputs.FirstOrDefault(o => !o.IsError) ?? cell.Outputs.FirstOrDefault();
        if (output is not null)
        {
            if (IsScriptDriven(output))
                sb.Append("<div class=\"vss-tile-interactive\">Interactive output</div>");
            else
                AppendOutput(sb, output);
            return;
        }

        var source = cell.Source;
        if (source.Length > 240)
            source = source[..240];
        sb.Append("<pre class=\"vss-tile-source\">").Append(WebUtility.HtmlEncode(source)).Append("</pre>");
    }

    /// <summary>
    /// True when an output only works as the live rendered element, never as serialized
    /// HTML injected by the layout. Two kinds qualify: HTML that carries its own script
    /// payload (a chart library bootstrap, for example), and widget output, which is a
    /// whole document the host renders in a frame of its own. Serializing either one
    /// produces markup whose scripts never run, so the copy is suppressed instead.
    /// </summary>
    private static bool IsScriptDriven(CellOutput output) =>
        output.MimeType == CellOutput.WidgetMimeType
        || (output.MimeType == "text/html"
            && output.Content.Contains("<script", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Serializes one output as static HTML. Copies appear only in the filmstrip
    /// previews and as the output pane's covered fallback; the pane's real content and
    /// the presenter always show the live rendered output element, and script-driven
    /// outputs are never copied at all.
    /// </summary>
    private static void AppendOutput(StringBuilder sb, CellOutput output)
    {
        if (output.IsError)
        {
            sb.Append("<div class=\"vss-out vss-out--error\"><pre>")
              .Append(WebUtility.HtmlEncode(output.Content))
              .Append("</pre></div>");
        }
        else if (output.MimeType == "text/html")
        {
            sb.Append("<div class=\"vss-out vss-out--html\">").Append(output.Content).Append("</div>");
        }
        else if (output.MimeType == "image/svg+xml")
        {
            sb.Append("<div class=\"vss-out vss-out--html\">").Append(output.Content).Append("</div>");
        }
        else if (output.MimeType == "text/x-verso-mermaid")
        {
            // The copy carries the diagram source in the host's mermaid container markup,
            // so the client-side pipeline that renders the live output renders the copy
            // too. Unlike id-targeted chart scripts, this is copy-safe: every render
            // generates its own element ids.
            sb.Append("<div class=\"vss-out vss-out--mermaid verso-mermaid-container\"><pre class=\"mermaid\">")
              .Append(WebUtility.HtmlEncode(output.Content))
              .Append("</pre></div>");
        }
        else if (output.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append("<div class=\"vss-out vss-out--image\"><img src=\"data:")
              .Append(output.MimeType).Append(";base64,").Append(output.Content)
              .Append("\" alt=\"cell output\"></div>");
        }
        else
        {
            sb.Append("<div class=\"vss-out vss-out--text\"><pre>")
              .Append(WebUtility.HtmlEncode(output.Content))
              .Append("</pre></div>");
        }
    }

    private static string CaptionFor(CellModel cell)
    {
        if (string.Equals(cell.Type, "code", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrEmpty(cell.Language) ? "Code" : cell.Language!;
        return cell.Type.Length == 0
            ? "Cell"
            : char.ToUpperInvariant(cell.Type[0]) + cell.Type[1..];
    }

    // --- Value coercion ----------------------------------------------------------------------

    private static double? ReadDouble(object? value) => value switch
    {
        double d => d,
        int i => i,
        long l => l,
        JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetDouble(out var d) => d,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };

    private static int? ReadInt(object? value) => value switch
    {
        int i => i,
        long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
        double d when d is >= int.MinValue and <= int.MaxValue => (int)d,
        JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var i) => i,
        string s when int.TryParse(s, out var i) => i,
        _ => null,
    };

    private static bool ReadBool(object? value) => value switch
    {
        bool b => b,
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.False } => false,
        string s when bool.TryParse(s, out var parsed) => parsed,
        _ => false,
    };
}
