using System.Text;
using System.Text.Json;
using Verso.Abstractions;

namespace Verso.Showcase.DagNotebook;

/// <summary>
/// A notebook layout with dependency-aware reactive execution. Visually it matches the built-in
/// notebook layout (live, editable cells in elevated cards with insert rails and drag-to-reorder);
/// on top of that, cells that share variables are linked with badge chips, and running a cell
/// automatically re-runs its dependents in dependency order.
///
/// How it works:
/// 1. On every render, <see cref="DependencyAnalyzer"/> extracts the variables each code cell
///    defines and uses and builds a producer/consumer graph. Each linked cell gets badge chips:
///    an up arrow names the cell a value comes from, a down arrow names a cell it feeds. Clicking
///    a chip scrolls to the linked cell.
/// 2. The layout script relays the host's cell execution events back into the interaction
///    handler. When a cell finishes successfully and auto-run is on, the layout executes the
///    cell's transitive dependents, one at a time, in topological order.
/// 3. A cell whose upstream producer runs before it does is marked stale (dashed border) until
///    it re-runs. Cells that assign the same variable, or that form a dependency cycle, get
///    warning chips and are excluded from automatic execution rather than guessed at.
///
/// The trigger is a completed run, not an edit: editing a cell changes nothing until you run it,
/// and the moment it completes its dependents follow. The host toolbar's Run All still runs cells
/// in document order; the header's Run DAG button runs them in dependency order instead. Batch
/// runs suppress the per-cell cascade (each newly starting cell cancels the pending trigger) so
/// Run All does not double-execute the notebook.
///
/// A widget control is the second trigger. A cell that shares a widget trait as a notebook
/// variable makes that variable change whenever the control moves, with no cell having run, so the
/// layout watches the shared store as well as the execution events: the control's cell counts as
/// the producer, and moving the control cascades its dependents exactly as finishing a run would.
/// That is what puts a slider in front of a computation written in another language.
/// </summary>
[VersoExtension]
public sealed class DagNotebookLayout : ILayoutEngine, ILayoutInteractionHandler
{
    /// <summary>How long a completed run waits before cascading. A following cell starting inside
    /// this window (a batch run) cancels the trigger, so document-order batch runs never fight
    /// the dependency-order cascade.</summary>
    private static readonly TimeSpan TriggerDebounce = TimeSpan.FromMilliseconds(300);

    /// <summary>How long a moved control waits before cascading. A dragged slider writes its
    /// variable many times a second and each write cancels the previous trigger, so a drag runs
    /// the notebook once, when the hand stops, rather than once per step.</summary>
    private static readonly TimeSpan ControlDebounce = TimeSpan.FromMilliseconds(450);

    private readonly object _gate = new();
    private IReadOnlyList<CellModel> _cells = Array.Empty<CellModel>();
    private DependencyAnalyzer.Graph _graph = DependencyAnalyzer.Analyze(Array.Empty<CellModel>());
    private bool _autoRun = true;

    /// <summary>1 while a cascade is executing cells; guards against overlapping cascades.</summary>
    private int _cascadeRunning;

    /// <summary>
    /// Completion events the running cascade expects to receive for its own ExecuteCellAsync
    /// calls. The script relays every completion, including the ones the cascade itself caused;
    /// consuming the expected ones here is what stops a cascade from re-triggering off its own
    /// executions and looping forever.
    /// </summary>
    private readonly Dictionary<Guid, int> _expectedCompletions = new();

    /// <summary>
    /// The last value seen for each variable that follows a control, rendered as a string. A
    /// change to the store is announced without saying what changed, so this is what turns
    /// "something changed" into "the control behind this cell moved". Values are compared as text
    /// because the comparison only has to separate one reading of a control from the next.
    /// </summary>
    private readonly Dictionary<string, string?> _boundValues = new(StringComparer.Ordinal);

    /// <summary>
    /// Cells left stale by a control that moved while auto-run was off. Execution events mark
    /// their own dependents stale on the client, but a control moving runs nothing, so this set
    /// travels out with the graph and is applied when the layout next paints.
    /// </summary>
    private readonly HashSet<Guid> _staleFromControls = new();

    private CancellationTokenSource? _pendingTrigger;

    // --- IExtension ---

    public string ExtensionId => "com.verso.showcase.dag-notebook";
    public string Name => "DAG Notebook";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description =>
        "Notebook layout with dependency-aware reactive execution: cells that share variables are " +
        "linked with badges, and running a cell re-runs its dependents in dependency order.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- ILayoutEngine ---

    public string LayoutId => "dag-notebook";
    public string DisplayName => "DAG Notebook";
    public string? Icon => null;
    public bool RequiresCustomRenderer => true;

    /// <summary>
    /// The notebook layout's cell affordances plus <c>NotebookEvents</c>, which this layout
    /// depends on twice over: the script keeps the slot set in sync on structural changes, and
    /// the execution-event relay that drives the reactive cascade only fires when this
    /// capability is declared.
    /// </summary>
    public LayoutCapabilities Capabilities =>
        LayoutCapabilities.CellInsert |
        LayoutCapabilities.CellDelete |
        LayoutCapabilities.CellReorder |
        LayoutCapabilities.CellEdit |
        LayoutCapabilities.CellResize |
        LayoutCapabilities.CellExecute |
        LayoutCapabilities.MultiSelect |
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
                AssetId: "dag-notebook.css",
                ContentType: "text/css",
                Content: Encoding.UTF8.GetBytes(DagNotebookStyles.BuildCss(RootPrefix))));
        }
        if (hostCapabilities.SupportedAssetContentTypes.Contains("text/javascript"))
        {
            assets.Add(new LayoutStaticAsset(
                AssetId: "dag-notebook.js",
                ContentType: "text/javascript",
                Content: Encoding.UTF8.GetBytes(DagNotebookStyles.BuildJs(ExtensionId, LayoutId)))
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
        var graph = DependencyAnalyzer.Analyze(cells);
        bool autoRun;
        lock (_gate)
        {
            _cells = cells;
            _graph = graph;
            autoRun = _autoRun;
        }

        SeedControlReadings(context.Variables, graph);

        var cellTypes = GetAvailableCellTypes(context);
        var collapsed = context.CollapsedSections;

        var sb = new StringBuilder(12 * 1024);
        sb.Append("<div class=\"vmd-root\">");

        AppendHeader(sb, autoRun);

        sb.Append("<div class=\"vmd-cells\">");
        if (cells.Count == 0)
        {
            sb.Append("<div class=\"vmd-empty\">")
              .Append("<p class=\"vmd-empty-title\">This notebook is empty</p>")
              .Append("<p class=\"vmd-empty-sub\">Add your first cell below.</p>")
              .Append("</div>");
        }

        int? skipUntilLevel = null;
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var headingLevel = GetHeadingLevel(cell);

            if (skipUntilLevel is not null)
            {
                if (headingLevel is not null && headingLevel.Value <= skipUntilLevel.Value)
                    skipUntilLevel = null;   // this heading closes the collapsed range; render it
                else
                    continue;                // still inside a collapsed section; omit the slot
            }

            var typeClass = string.Equals(cell.Type, "markdown", StringComparison.OrdinalIgnoreCase)
                ? "vmd-cell--markdown"
                : "vmd-cell--code";

            // Each cell lives in a unit that pairs its dependency badge row with its slot. The
            // badges sit outside the slot on purpose: the portal owns the slot's children, so
            // chrome inside it would not survive the live cell mounting.
            var failed = string.Equals(cell.LastStatus, "Failed", StringComparison.OrdinalIgnoreCase);
            sb.Append("<div class=\"vdag-unit").Append(failed ? " vdag-failed" : "")
              .Append("\" data-dag-cell=\"").Append(cell.Id).Append("\">");

            AppendBadges(sb, cell.Id, graph);

            sb.Append("<section class=\"vmd-cell ").Append(typeClass)
              .Append("\" data-cell-slot=\"").Append(cell.Id)
              .Append("\" data-cell-index=\"").Append(i).Append("\">");
            sb.Append("</section>");

            sb.Append("</div>"); // .vdag-unit

            if (headingLevel is not null && collapsed.Contains(cell.Id))
                skipUntilLevel = headingLevel;

            if (i < cells.Count - 1)
                AppendInsertRail(sb, i + 1, cellTypes);
        }

        sb.Append("</div>"); // .vmd-cells

        sb.Append("<div class=\"vmd-add-row\">");
        foreach (var ct in cellTypes)
        {
            sb.Append("<button type=\"button\" class=\"vmd-add-btn\" data-action=\"insert-cell\" data-index=\"")
              .Append(cells.Count)
              .Append("\" data-type=\"").Append(ct.Id).Append("\">")
              .Append("<span class=\"vmd-add-glyph\">&#x2B;</span> ")
              .Append(ct.DisplayName).Append(" Cell</button>");
        }
        sb.Append("</div>");

        // The dependency graph the layout script uses for client-side stale marking and chip
        // navigation. Content is cell id guids and numbers only, so it is safe to inline.
        sb.Append("<script type=\"application/json\" data-vdag-graph>")
          .Append(BuildGraphJson(graph))
          .Append("</script>");

        sb.Append("</div>"); // .vmd-root
        return Task.FromResult(new RenderResult("text/html", sb.ToString()));
    }

    public Task<CellContainerInfo> GetCellContainerAsync(Guid cellId, IVersoContext context)
        => Task.FromResult(new CellContainerInfo(cellId, 0, 0, 800, 120));

    public Task OnCellAddedAsync(Guid cellId, int index, IVersoContext context) => Task.CompletedTask;
    public Task OnCellRemovedAsync(Guid cellId, IVersoContext context) => Task.CompletedTask;
    public Task OnCellMovedAsync(Guid cellId, int newIndex, IVersoContext context) => Task.CompletedTask;

    public Dictionary<string, object> GetLayoutMetadata()
    {
        lock (_gate) return new Dictionary<string, object> { ["autoRun"] = _autoRun };
    }

    public Task ApplyLayoutMetadata(Dictionary<string, object> metadata, IVersoContext context)
    {
        if (metadata.TryGetValue("autoRun", out var raw))
        {
            bool? parsed = raw switch
            {
                bool b => b,
                JsonElement { ValueKind: JsonValueKind.True } => true,
                JsonElement { ValueKind: JsonValueKind.False } => false,
                string s when bool.TryParse(s, out var b) => b,
                _ => null,
            };
            if (parsed is not null)
            {
                lock (_gate) _autoRun = parsed.Value;
            }
        }
        return Task.CompletedTask;
    }

    // --- ILayoutInteractionHandler ---

    public async Task OnLayoutInteractionAsync(LayoutInteractionContext context)
    {
        switch (context.InteractionType)
        {
            case "insert-cell":
                if (int.TryParse(context.Payload, out var index) && index >= 0)
                {
                    var type = string.IsNullOrWhiteSpace(context.TargetId) ? "code" : context.TargetId!;
                    await context.Verso.Notebook.InsertCellAsync(index, type).ConfigureAwait(false);
                    context.RequestRender();
                }
                break;

            case "move-cell":
                if (int.TryParse(context.Payload, out var newIndex) && newIndex >= 0
                    && Guid.TryParse(context.TargetId, out var movedId))
                {
                    await context.Verso.Notebook.MoveCellAsync(movedId, newIndex).ConfigureAwait(false);
                    context.RequestRender();
                }
                break;

            case "dag-toggle-auto":
                lock (_gate) _autoRun = !_autoRun;
                CancelPendingTrigger();
                context.RequestRender();
                break;

            case "dag-run-all":
            {
                CancelPendingTrigger();
                var fresh = ReanalyzeAndStore();
                StartCascade(fresh.TopologicalOrder(), context.Verso, context.RequestRender);
                break;
            }

            case "dag-cell-executing":
                // A cell the cascade did not start is beginning to run (a user click, or the
                // host's Run All working through the notebook). Cancel any pending trigger so a
                // batch run settles before anything cascades.
                if (Guid.TryParse(context.Payload, out var startedId) && !IsCascadeInitiated(startedId))
                    CancelPendingTrigger();
                break;

            case "dag-variables-changed":
            {
                // Every write to the shared store arrives here, including the ones cells make as
                // they run. Only a change that happens with nothing running can be a control being
                // moved, and the client relays it only then; a cascade in flight is the other case
                // worth refusing, because its own cells are writing.
                if (Volatile.Read(ref _cascadeRunning) != 0) break;

                var graph = ReanalyzeAndStore();
                var moved = TakeMovedControls(context.Verso.Variables, graph);
                if (moved.Count == 0) break;

                var affected = new HashSet<Guid>();
                foreach (var name in moved)
                {
                    if (!graph.BoundVariables.TryGetValue(name, out var producer)) continue;
                    foreach (var dependent in graph.Downstream(producer))
                        affected.Add(dependent);
                }
                if (affected.Count == 0) break;

                bool autoRunOnChange;
                lock (_gate) autoRunOnChange = _autoRun;

                if (!autoRunOnChange)
                {
                    // Nothing ran, so nothing told the client these cells are behind. Say so.
                    lock (_gate)
                    {
                        foreach (var id in affected) _staleFromControls.Add(id);
                    }
                    context.RequestRender();
                    break;
                }

                ScheduleCascade(
                    graph.TopologicalOrder().Where(affected.Contains).ToList(),
                    context.Verso, context.RequestRender, ControlDebounce);
                break;
            }

            case "dag-cell-completed":
            {
                if (!Guid.TryParse(context.Payload, out var completedId)) break;

                // A cell that has run is no longer behind whatever moved.
                lock (_gate) _staleFromControls.Remove(completedId);

                // Completions the cascade itself caused must not schedule another cascade.
                if (TryConsumeExpectedCompletion(completedId)) break;

                // A completed run is the moment edited sources get to matter: re-analyze so the
                // cascade sees fresh edges, and re-render the badges if the dependency picture
                // changed (a new cell's first run is the common case).
                DependencyAnalyzer.Graph previous;
                lock (_gate) previous = _graph;
                var graph = ReanalyzeAndStore();
                if (!GraphSignature(graph).Equals(GraphSignature(previous), StringComparison.Ordinal))
                    context.RequestRender();

                // A completed run is also where a bind lands and where a cell may have written a
                // bound variable itself. Taking the reading here means the first move of a control
                // has something to differ from, and that a cell's own write is never mistaken for
                // one, which is what stops a cell that writes the variable it reads from looping.
                RefreshControlReadings(context.Verso.Variables);

                bool autoRun;
                CellModel? completedCell;
                lock (_gate)
                {
                    autoRun = _autoRun;
                    completedCell = _cells.FirstOrDefault(c => c.Id == completedId);
                }
                var downstream = graph.Nodes.ContainsKey(completedId)
                    ? graph.Downstream(completedId)
                    : Array.Empty<Guid>();

                if (!autoRun || downstream.Count == 0) break;
                if (!string.Equals(completedCell?.LastStatus, "Success", StringComparison.OrdinalIgnoreCase))
                    break;   // a failed run marks dependents stale but never executes them

                ScheduleCascade(downstream, context.Verso, context.RequestRender);
                break;
            }
        }
    }

    // --- Cascade execution -----------------------------------------------------------------

    /// <summary>
    /// Debounces the cascade behind <see cref="TriggerDebounce"/>. If another cell starts
    /// executing inside the window (a document-order batch run), the trigger is cancelled and
    /// the batch is left alone.
    /// </summary>
    private void ScheduleCascade(
        IReadOnlyList<Guid> order, IVersoContext verso, Action requestRender, TimeSpan? debounce = null)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            _pendingTrigger?.Cancel();
            _pendingTrigger = cts = new CancellationTokenSource();
        }

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(debounce ?? TriggerDebounce, cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            StartCascade(order, verso, requestRender);
        });
    }

    /// <summary>
    /// Executes the given cells one at a time, in order, off the interaction thread. Strictly
    /// sequential on purpose: a dependent must not run before its producer finishes, and the
    /// engine serializes execution per kernel, so a cascade never fires overlapping runs. The
    /// first failure stops the cascade; the cells that never ran keep their stale marker.
    /// </summary>
    private void StartCascade(IReadOnlyList<Guid> order, IVersoContext verso, Action requestRender)
    {
        if (order.Count == 0) return;
        if (Interlocked.CompareExchange(ref _cascadeRunning, 1, 0) != 0) return;

        lock (_gate)
        {
            foreach (var id in order) _staleFromControls.Remove(id);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var cellId in order)
                {
                    CellModel? cell;
                    lock (_gate) cell = _cells.FirstOrDefault(c => c.Id == cellId);
                    if (cell is null) continue;   // deleted while the cascade was in flight

                    RegisterExpectedCompletion(cellId);
                    try
                    {
                        await verso.Notebook.ExecuteCellAsync(cellId).ConfigureAwait(false);
                    }
                    catch
                    {
                        UnregisterExpectedCompletion(cellId);
                        break;
                    }

                    // ExecuteCellAsync stamps the result on the live model before returning.
                    if (!string.Equals(cell.LastStatus, "Success", StringComparison.OrdinalIgnoreCase))
                        break;
                }
            }
            finally
            {
                // Take the reading the cascade leaves behind before accepting another change, so
                // anything the cascade's own cells wrote is not mistaken for a control moving.
                try { RefreshControlReadings(verso.Variables); } catch { /* nothing to compare to */ }

                Interlocked.Exchange(ref _cascadeRunning, 0);
                // Settle the rendered state (failed borders, execution counts) once, at the end.
                try { requestRender(); } catch { /* the client may have re-rendered already */ }
            }
        });
    }

    /// <summary>
    /// Re-runs the dependency analysis over the current cell sources and makes the result the
    /// active graph. Cell models are live references, so this picks up edits made since the last
    /// render without any host support for source-change events.
    /// </summary>
    private DependencyAnalyzer.Graph ReanalyzeAndStore()
    {
        IReadOnlyList<CellModel> cells;
        lock (_gate) cells = _cells;
        var graph = DependencyAnalyzer.Analyze(cells);
        lock (_gate) _graph = graph;
        return graph;
    }

    /// <summary>
    /// A canonical string of the graph's edges, conflicts, and cycle members. Two graphs with the
    /// same signature render the same badges, so a signature match skips the re-render. Node
    /// numbers are excluded on purpose: they only change with structural edits, which re-render
    /// through the host's own notebook-changed path.
    /// </summary>
    private static string GraphSignature(DependencyAnalyzer.Graph graph)
    {
        var sb = new StringBuilder(256);
        foreach (var edge in graph.Edges
                     .OrderBy(e => e.From).ThenBy(e => e.To)
                     .ThenBy(e => e.Variable, StringComparer.Ordinal))
            sb.Append(edge.From).Append('>').Append(edge.To).Append(':').Append(edge.Variable).Append(';');
        sb.Append('|');
        foreach (var (name, writers) in graph.MultiWriterVariables.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.Append(name).Append('=');
            foreach (var id in writers.OrderBy(id => id)) sb.Append(id).Append(',');
            sb.Append(';');
        }
        sb.Append('|');
        foreach (var id in graph.CyclicCells.OrderBy(id => id)) sb.Append(id).Append(',');
        return sb.ToString();
    }

    // --- Controls ----------------------------------------------------------------------------

    /// <summary>
    /// The bound variables whose value differs from the last reading, taking the new reading as it
    /// goes. A name read here for the first time is recorded and not reported, so the change that
    /// brings a variable into being settles rather than cascading.
    /// </summary>
    private IReadOnlyList<string> TakeMovedControls(IVariableStore store, DependencyAnalyzer.Graph graph)
    {
        // Read the store first and hold the lock only over the comparison.
        var readings = graph.BoundVariables.Keys.ToDictionary(n => n, n => Read(store, n), StringComparer.Ordinal);
        var moved = new List<string>();

        lock (_gate)
        {
            foreach (var (name, reading) in readings)
            {
                var known = _boundValues.TryGetValue(name, out var last);
                _boundValues[name] = reading;

                if (known && !string.Equals(last, reading, StringComparison.Ordinal))
                    moved.Add(name);
            }

            // Names no longer bound stop being watched, so re-binding one starts clean.
            foreach (var gone in _boundValues.Keys.Where(n => !readings.ContainsKey(n)).ToList())
                _boundValues.Remove(gone);
        }

        return moved;
    }

    /// <summary>
    /// Records a first reading for any bound variable the layout has not read yet, leaving the
    /// ones it already knows alone so a change waiting to be reported is never masked. Called from
    /// the render so a baseline exists however the layout came to be active. A layout switched on
    /// partway through a run misses the execution events before it, so the completions cannot be
    /// relied on for this; a control moved before the layout ever read the store would otherwise
    /// register as the first reading and be swallowed.
    /// </summary>
    private void SeedControlReadings(IVariableStore store, DependencyAnalyzer.Graph graph)
    {
        List<string> unseen;
        lock (_gate)
        {
            unseen = graph.BoundVariables.Keys.Where(n => !_boundValues.ContainsKey(n)).ToList();
        }
        if (unseen.Count == 0) return;

        // A name the store does not hold yet is left unseeded, so the write that brings it into
        // being is the reading rather than a change away from nothing.
        var readings = unseen.Select(n => (Name: n, Value: Read(store, n)))
            .Where(r => r.Value is not null).ToList();

        lock (_gate)
        {
            foreach (var (name, value) in readings)
                if (!_boundValues.ContainsKey(name)) _boundValues[name] = value;
        }
    }

    /// <summary>Takes a fresh reading of every bound variable without reporting any change.</summary>
    private void RefreshControlReadings(IVariableStore store)
    {
        DependencyAnalyzer.Graph graph;
        lock (_gate) graph = _graph;

        var readings = graph.BoundVariables.Keys.ToDictionary(n => n, n => Read(store, n), StringComparer.Ordinal);

        lock (_gate)
        {
            foreach (var (name, reading) in readings)
                _boundValues[name] = reading;
        }
    }

    /// <summary>
    /// A variable rendered for comparison. Text is all this needs to be: two readings of the same
    /// control differ in their text exactly when the control moved, and a value with no useful
    /// text of its own compares by its type, which never changes, so it never reads as a move.
    /// </summary>
    private static string? Read(IVariableStore store, string name)
    {
        if (!store.TryGet<object>(name, out var value) || value is null) return null;
        try
        {
            return value is IFormattable formattable
                ? formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture)
                : value.ToString();
        }
        catch
        {
            return value.GetType().FullName;
        }
    }

    private void CancelPendingTrigger()
    {
        lock (_gate)
        {
            _pendingTrigger?.Cancel();
            _pendingTrigger = null;
        }
    }

    private void RegisterExpectedCompletion(Guid cellId)
    {
        lock (_gate)
        {
            _expectedCompletions.TryGetValue(cellId, out var n);
            _expectedCompletions[cellId] = n + 1;
        }
    }

    private void UnregisterExpectedCompletion(Guid cellId)
    {
        lock (_gate)
        {
            if (!_expectedCompletions.TryGetValue(cellId, out var n)) return;
            if (n <= 1) _expectedCompletions.Remove(cellId);
            else _expectedCompletions[cellId] = n - 1;
        }
    }

    private bool TryConsumeExpectedCompletion(Guid cellId)
    {
        lock (_gate)
        {
            if (!_expectedCompletions.TryGetValue(cellId, out var n) || n <= 0) return false;
            if (n == 1) _expectedCompletions.Remove(cellId);
            else _expectedCompletions[cellId] = n - 1;
            return true;
        }
    }

    private bool IsCascadeInitiated(Guid cellId)
    {
        lock (_gate) return _expectedCompletions.TryGetValue(cellId, out var n) && n > 0;
    }

    // --- HTML helpers ------------------------------------------------------------------------

    private static void AppendHeader(StringBuilder sb, bool autoRun)
    {
        sb.Append("<div class=\"vdag-header\">")
          .Append("<div class=\"vdag-title\"><span class=\"vdag-logo\">&#8649;</span> DAG Notebook</div>")
          .Append("<div class=\"vdag-controls\">")
          .Append("<button type=\"button\" class=\"vdag-btn vdag-btn--primary\" data-action=\"dag-run-all\" ")
          .Append("title=\"Run every cell in dependency order\">Run DAG</button>")
          .Append("<button type=\"button\" class=\"vdag-btn vdag-toggle ").Append(autoRun ? "is-on" : "is-off")
          .Append("\" data-action=\"dag-toggle-auto\" title=\"When on, running a cell or moving a bound control re-runs the cells that depend on it\">")
          .Append("<span class=\"vdag-dot\"></span>Auto-run dependents: ").Append(autoRun ? "On" : "Off")
          .Append("</button>")
          .Append("</div></div>");
    }

    private static void AppendBadges(StringBuilder sb, Guid cellId, DependencyAnalyzer.Graph graph)
    {
        if (!graph.Nodes.TryGetValue(cellId, out _)) return;

        var inbound = graph.InboundEdges(cellId).OrderBy(e => graph.Nodes[e.From].Number)
            .ThenBy(e => e.Variable, StringComparer.Ordinal).ToList();
        var outbound = graph.OutboundEdges(cellId).OrderBy(e => graph.Nodes[e.To].Number)
            .ThenBy(e => e.Variable, StringComparer.Ordinal).ToList();
        var conflicts = graph.ConflictsFor(cellId);
        var cyclic = graph.CyclicCells.Contains(cellId);
        var bindings = graph.BindingsFor(cellId);

        if (inbound.Count == 0 && outbound.Count == 0 && conflicts.Count == 0
            && bindings.Count == 0 && !cyclic) return;

        sb.Append("<div class=\"vdag-badges\">");

        foreach (var name in bindings)
        {
            sb.Append("<span class=\"vdag-chip vdag-chip--live\" title=\"")
              .Append(name).Append(" follows a control on this page; moving it runs the cells below\">")
              .Append("<span class=\"vdag-arrow\">&#8635;</span>")
              .Append("<span class=\"vdag-var\">").Append(name).Append("</span></span>");
        }

        foreach (var edge in inbound)
        {
            var producer = graph.Nodes[edge.From].Number;
            sb.Append("<button type=\"button\" class=\"vdag-chip vdag-chip--in\" data-goto=\"").Append(edge.From)
              .Append("\" title=\"Reads ").Append(edge.Variable).Append(" from cell ").Append(producer).Append("\">")
              .Append("<span class=\"vdag-arrow\">&#8593;</span>").Append(producer)
              .Append("&nbsp;<span class=\"vdag-var\">").Append(edge.Variable).Append("</span></button>");
        }

        foreach (var edge in outbound)
        {
            var consumer = graph.Nodes[edge.To].Number;
            sb.Append("<button type=\"button\" class=\"vdag-chip vdag-chip--out\" data-goto=\"").Append(edge.To)
              .Append("\" title=\"Feeds ").Append(edge.Variable).Append(" to cell ").Append(consumer).Append("\">")
              .Append("<span class=\"vdag-arrow\">&#8595;</span>").Append(consumer)
              .Append("&nbsp;<span class=\"vdag-var\">").Append(edge.Variable).Append("</span></button>");
        }

        foreach (var name in conflicts)
        {
            sb.Append("<span class=\"vdag-chip vdag-chip--warn\" title=\"")
              .Append(name).Append(" is assigned in more than one cell, so its links are not tracked\">")
              .Append("&#9888; <span class=\"vdag-var\">").Append(name).Append("</span></span>");
        }

        if (cyclic)
        {
            sb.Append("<span class=\"vdag-chip vdag-chip--warn\" ")
              .Append("title=\"This cell is on a dependency cycle; cycle edges are excluded from auto-run\">")
              .Append("&#9888; cycle</span>");
        }

        sb.Append("</div>");
    }

    private string BuildGraphJson(DependencyAnalyzer.Graph graph)
    {
        var cells = new Dictionary<string, object>();
        foreach (var node in graph.Nodes.Values)
        {
            cells[node.Id.ToString()] = new
            {
                n = node.Number,
                dependents = graph.OutboundEdges(node.Id).Select(e => e.To.ToString()).Distinct().ToArray(),
            };
        }

        string[] stale;
        lock (_gate) stale = _staleFromControls.Select(id => id.ToString()).ToArray();

        // hasControls tells the script whether a store change can matter at all here, so a
        // notebook with no bound control never relays one.
        return JsonSerializer.Serialize(new
        {
            cells,
            stale,
            hasControls = graph.BoundVariables.Count > 0,
        });
    }

    private static void AppendInsertRail(StringBuilder sb, int index, IReadOnlyList<CellTypeOption> cellTypes)
    {
        sb.Append("<div class=\"vmd-insert\"><div class=\"vmd-insert-buttons\">");
        foreach (var ct in cellTypes)
        {
            sb.Append("<button type=\"button\" class=\"vmd-insert-btn\" data-action=\"insert-cell\" data-index=\"")
              .Append(index)
              .Append("\" data-type=\"").Append(ct.Id)
              .Append("\" title=\"Insert a ").Append(ct.DisplayName).Append(" cell here\">")
              .Append("<span class=\"vmd-insert-glyph\">&#x2B;</span>")
              .Append(ct.DisplayName).Append("</button>");
        }
        sb.Append("</div></div>");
    }

    private static IReadOnlyList<CellTypeOption> GetAvailableCellTypes(IVersoContext context)
    {
        var types = new List<CellTypeOption> { new("code", "Code") };
        var host = context.ExtensionHost;

        var registeredTypes = host.GetCellTypes();
        var hasMarkdown =
            registeredTypes.Any(ct => string.Equals(ct.CellTypeId, "markdown", StringComparison.OrdinalIgnoreCase))
            || host.GetRenderers().Any(r => string.Equals(r.CellTypeId, "markdown", StringComparison.OrdinalIgnoreCase));
        if (hasMarkdown)
            types.Add(new CellTypeOption("markdown", "Markdown"));

        foreach (var ct in registeredTypes)
        {
            if (!string.Equals(ct.CellTypeId, "code", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ct.CellTypeId, "markdown", StringComparison.OrdinalIgnoreCase))
                types.Add(new CellTypeOption(ct.CellTypeId, ct.DisplayName));
        }

        return types;
    }

    private readonly record struct CellTypeOption(string Id, string DisplayName);

    private static int? GetHeadingLevel(CellModel cell)
    {
        var source = cell.Source;
        if (string.IsNullOrEmpty(source)) return null;

        if (string.Equals(cell.Type, "markdown", StringComparison.OrdinalIgnoreCase))
        {
            var text = source.TrimStart();
            var hashes = 0;
            while (hashes < text.Length && text[hashes] == '#') hashes++;
            if (hashes is >= 1 and <= 6 && hashes < text.Length
                && (text[hashes] == ' ' || text[hashes] == '\t'))
                return hashes;
            return null;
        }

        if (string.Equals(cell.Type, "html", StringComparison.OrdinalIgnoreCase))
        {
            var text = source.TrimStart();
            if (text.Length >= 3 && text[0] == '<' && (text[1] == 'h' || text[1] == 'H')
                && text[2] is >= '1' and <= '6')
                return text[2] - '0';
        }

        return null;
    }
}
