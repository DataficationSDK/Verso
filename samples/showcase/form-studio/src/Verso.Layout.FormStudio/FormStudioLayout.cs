using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Verso.Abstractions;

namespace Verso.Layout.FormStudio;

/// <summary>
/// "Form Studio" — an isolated (iframe) layout that turns a notebook into a live, parameterized
/// app. The frame is a drag-and-drop canvas: input widgets (sliders, dropdowns, toggles, text)
/// bind to kernel variables, and chart widgets visualize a kernel <c>DataBlock</c>. Moving an
/// input writes its bound variable and (when auto-run is on) re-runs the notebook, so downstream
/// cells recompute and the charts refresh.
/// </summary>
/// <remarks>
/// <para>
/// The canvas itself is authored entirely in the frame and is instant — dragging, resizing, and
/// configuring widgets never round-trip to the kernel. Only meaningful events cross the bridge:
/// an input value changed, a chart needs data, or the document structure changed (persisted via
/// layout metadata). The built app is the layout's document.
/// </para>
/// <para>
/// DataBlock access is reflection-based (see <see cref="DataBlockReader"/>), so the extension
/// references only <c>Verso.Abstractions</c> and reads whichever Core assembly the kernel loaded.
/// </para>
/// </remarks>
[VersoExtension]
public sealed class FormStudioLayout
    : ILayoutEngine, ILayoutLifecycleHandler, ILayoutInteractionHandler
{
    private FormDocument _doc = new();

    private readonly ConcurrentDictionary<string, ILayoutFrameChannel> _frames = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Action> _unsubscribers = new(StringComparer.Ordinal);

    // Coalesces rapid input changes (e.g. dragging a slider) into a single re-run.
    private readonly object _runGate = new();
    private CancellationTokenSource? _runCts;

    // True while a recompute is in flight. Chart pushes are suppressed during the run and sent
    // once at the end, so an input change updates the charts in a single step rather than
    // flickering through each intermediate variable-store change the run produces.
    private volatile bool _running;

    // --- IExtension ---

    public string ExtensionId => "com.verso.sample.form-studio";
    public string Name => "Form Studio Layout";
    public string Version => "1.0.0";
    public string? Author => "Datafication";
    public string? Description => "Isolated layout that turns a notebook into a live, parameterized app built from draggable widgets and charts.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- ILayoutEngine ---

    public string LayoutId => "form-studio";
    public string DisplayName => "Form Studio";

    public string? Icon =>
        "<svg viewBox=\"0 0 16 16\" fill=\"none\" xmlns=\"http://www.w3.org/2000/svg\">" +
        "<rect x=\"2\" y=\"2.5\" width=\"5\" height=\"5\" rx=\"1\" stroke=\"currentColor\"/>" +
        "<rect x=\"9\" y=\"2.5\" width=\"5\" height=\"5\" rx=\"1\" stroke=\"currentColor\"/>" +
        "<rect x=\"2\" y=\"9\" width=\"5\" height=\"4.5\" rx=\"1\" stroke=\"currentColor\"/>" +
        "<path d=\"M9.5 13.5v-3M11.5 13.5v-2M13.5 13.5V9.5\" stroke=\"currentColor\" stroke-linecap=\"round\"/>" +
        "</svg>";

    public bool RequiresCustomRenderer => true;

    // The frame owns rendering; the kernel still executes cells (the app's recompute loop).
    public LayoutCapabilities Capabilities => LayoutCapabilities.CellExecute;

    public LayoutRendererIsolation RendererIsolation => LayoutRendererIsolation.Isolated;

    public Task<LayoutRendererPackage?> GetRendererPackageAsync(IVersoContext context)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["main.js"] = Encoding.UTF8.GetBytes(RendererScript.MainJs),
        };

        // The renderer uses inline scripts/styles only (the chart library is injected as an inline
        // classic script), all within the host's base frame policy, so no extra CSP is needed.
        var package = new LayoutRendererPackage(
            EntryPoint: "main.js",
            Files: files,
            ContentSecurityPolicy: null);

        return Task.FromResult<LayoutRendererPackage?>(package);
    }

    // Isolated layouts render inside the frame; RenderLayoutAsync is never consulted.
    public Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context)
        => Task.FromResult(new RenderResult("text/html", string.Empty));

    // No cell is placed on the canvas — the dashboard is the document, not the cells.
    public Task<CellContainerInfo> GetCellContainerAsync(Guid cellId, IVersoContext context)
        => Task.FromResult(new CellContainerInfo(cellId, X: 0, Y: 0, Width: 0, Height: 0, IsVisible: false));

    public Task OnCellAddedAsync(Guid cellId, int index, IVersoContext context) => Task.CompletedTask;
    public Task OnCellRemovedAsync(Guid cellId, IVersoContext context) => Task.CompletedTask;
    public Task OnCellMovedAsync(Guid cellId, int newIndex, IVersoContext context) => Task.CompletedTask;

    public Dictionary<string, object> GetLayoutMetadata() => _doc.ToMetadata();

    public Task ApplyLayoutMetadata(Dictionary<string, object> metadata, IVersoContext context)
    {
        _doc = FormDocument.FromMetadata(metadata);
        return Task.CompletedTask;
    }

    // --- ILayoutLifecycleHandler ---

    public Task<IReadOnlyDictionary<string, object>?> OnRendererMountedAsync(LayoutRendererMountContext context)
    {
        var frame = context.Frame;
        var variables = context.Verso.Variables;
        _frames[context.FrameInstanceId] = frame;

        // Any kernel variable change (a cell ran, or an input wrote a variable) may change a chart's
        // source DataBlock. The store event does not name the changed variable, so re-read and push
        // every chart, plus the refreshed variable list for the pickers.
        void PushOnChange()
        {
            if (_running || !frame.IsAlive)
                return;
            _ = frame.PostMessageAsync("vars", BuildVars(variables), context.CancellationToken);
            foreach (var chart in _doc.Charts)
                PushChart(frame, chart.Id, chart.SourceVar, variables, context.CancellationToken);
        }

        variables.OnVariablesChanged += PushOnChange;
        _unsubscribers[context.FrameInstanceId] = () => variables.OnVariablesChanged -= PushOnChange;

        var seed = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["doc"] = _doc.Json,
            ["vars"] = BuildVars(variables),
        };
        return Task.FromResult<IReadOnlyDictionary<string, object>?>(seed);
    }

    public Task OnRendererUnmountedAsync(LayoutRendererUnmountContext context)
    {
        _frames.TryRemove(context.FrameInstanceId, out _);
        if (_unsubscribers.TryRemove(context.FrameInstanceId, out var unsubscribe))
            unsubscribe();

        return Task.CompletedTask;
    }

    // --- ILayoutInteractionHandler ---

    public Task OnLayoutInteractionAsync(LayoutInteractionContext context)
    {
        var variables = context.Verso.Variables;
        switch (context.InteractionType)
        {
            case "save-doc":
                _doc.Update(ReadString(context.Payload, "doc"));
                break;

            case "set-value":
                ApplyValue(context.Payload, variables, context.Verso);
                break;

            case "bind-chart":
                BindChart(context, variables);
                break;

            case "request-vars":
                if (_frames.TryGetValue(context.FrameInstanceId, out var varsFrame) && varsFrame.IsAlive)
                    _ = varsFrame.PostMessageAsync("vars", BuildVars(variables), context.CancellationToken);
                break;

            case "run":
                CancelPendingRun();
                _running = true;
                _ = SafeRunAsync(context.Verso);
                break;

            case "export":
                HandleExport(context);
                break;
        }

        return Task.CompletedTask;
    }

    // --- Input value -> kernel variable -------------------------------------

    private void ApplyValue(string payload, IVariableStore variables, IVersoContext verso)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return;

        string? name, kind;
        object? value;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            name = ReadString(root, "var");
            kind = ReadString(root, "kind");
            if (string.IsNullOrWhiteSpace(name) || !root.TryGetProperty("value", out var raw))
                return;
            value = Coerce(kind, raw);
        }
        catch (JsonException)
        {
            return;
        }

        if (value is null)
            return;

        // Mark the run as starting before the write, so the echo from this Set is suppressed and
        // the charts update only once, after the recompute, rather than flashing the old data first.
        if (_doc.AutoRun)
            _running = true;

        variables.Set(name!.Trim(), value);

        if (_doc.AutoRun)
            DebouncedRun(verso);
    }

    // Coerce a widget's value to the kernel type implied by its kind. Numeric kinds become double,
    // toggles bool, the rest string. An unparseable numeric value is dropped (no write).
    private static object? Coerce(string? kind, JsonElement value) => kind switch
    {
        "toggle" => value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.GetDouble() != 0,
            JsonValueKind.String => IsTruthy(value.GetString()),
            _ => false,
        },
        "slider" or "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n) ? n
            : value.ValueKind == JsonValueKind.String
              && double.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed
            : (object?)null,
        _ => value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText(),
    };

    private static bool IsTruthy(string? s)
        => s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "on", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase);

    // --- The recompute loop -------------------------------------------------

    // Re-run the notebook a short moment after the last input change, cancelling any run already
    // queued so a dragged slider fires one recompute, not dozens.
    private void DebouncedRun(IVersoContext verso)
    {
        CancellationToken token;
        lock (_runGate)
        {
            _runCts?.Cancel();
            _runCts = new CancellationTokenSource();
            token = _runCts.Token;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            await SafeRunAsync(verso).ConfigureAwait(false);
        });
    }

    private void CancelPendingRun()
    {
        lock (_runGate)
        {
            _runCts?.Cancel();
            _runCts = null;
        }
    }

    // A notebook can register a focused recompute snippet in the reserved "__verso_recompute"
    // variable (for example "Recompute();"). When present, Form Studio runs just that snippet, which
    // is fast and, crucially, correct: re-running only the compute step means no other cell
    // republishes a stale copy of an input variable over the value the dashboard just wrote. With no
    // snippet registered it falls back to running the whole notebook. Either way, pushes are
    // suppressed during the run (see _running) and a single refresh is sent at the end.
    private async Task SafeRunAsync(IVersoContext verso)
    {
        try
        {
            var recompute = verso.Variables.Get<string>("__verso_recompute");
            if (!string.IsNullOrWhiteSpace(recompute))
                await verso.Notebook.ExecuteCodeAsync(recompute, "csharp").ConfigureAwait(false);
            else
                await verso.Notebook.ExecuteAllAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A failed recompute should not crash the layout; the charts simply keep their data.
            Console.Error.WriteLine($"[form-studio] Recompute failed: {ex.Message}");
        }
        finally
        {
            _running = false;
            PushAllFrames(verso.Variables);
        }
    }

    // Push the refreshed variable list and every bound chart's data to all live frames. Used after a
    // recompute completes (pushes were suppressed while it ran).
    private void PushAllFrames(IVariableStore variables)
    {
        foreach (var frame in _frames.Values)
        {
            if (!frame.IsAlive)
                continue;
            _ = frame.PostMessageAsync("vars", BuildVars(variables), CancellationToken.None);
            foreach (var chart in _doc.Charts)
                PushChart(frame, chart.Id, chart.SourceVar, variables, CancellationToken.None);
        }
    }

    // --- Chart data (DataBlock -> frame) ------------------------------------

    private void BindChart(LayoutInteractionContext context, IVariableStore variables)
    {
        string? id, sourceVar;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(context.Payload) ? "{}" : context.Payload);
            id = ReadString(document.RootElement, "id");
            sourceVar = ReadString(document.RootElement, "sourceVar");
        }
        catch (JsonException)
        {
            return;
        }

        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(sourceVar))
            return;
        if (_frames.TryGetValue(context.FrameInstanceId, out var frame) && frame.IsAlive)
            PushChart(frame, id!, sourceVar!, variables, context.CancellationToken);
    }

    private void PushChart(ILayoutFrameChannel frame, string id, string sourceVar, IVariableStore variables, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sourceVar))
            return;

        BlockData? data = null;
        if (variables.TryGet<object>(sourceVar, out var value) && value is not null && DataBlockReader.IsDataBlock(value))
            data = DataBlockReader.Read(value);

        _ = frame.PostMessageAsync("chart-data", new { id, sourceVar, data }, ct);
    }

    // The names of every variable currently holding a DataBlock, for the chart source pickers.
    private object BuildVars(IVariableStore variables)
    {
        var dataBlockVars = new List<string>();
        foreach (var descriptor in variables.GetAll())
        {
            var isDataBlock = DataBlockReader.IsDataBlock(descriptor.Value)
                || string.Equals(descriptor.Type?.FullName, "Datafication.Core.Data.DataBlock", StringComparison.Ordinal);
            if (isDataBlock && !dataBlockVars.Contains(descriptor.Name))
                dataBlockVars.Add(descriptor.Name);
        }
        dataBlockVars.Sort(StringComparer.Ordinal);
        return new { dataBlockVars };
    }

    // --- Export -------------------------------------------------------------

    private static void HandleExport(LayoutInteractionContext context)
    {
        string? fileName, content;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(context.Payload) ? "{}" : context.Payload);
            fileName = ReadString(document.RootElement, "fileName");
            content = ReadString(document.RootElement, "content");
        }
        catch (JsonException)
        {
            return;
        }

        if (string.IsNullOrEmpty(content))
            return;

        try
        {
            _ = context.Verso.RequestFileDownloadAsync(
                string.IsNullOrWhiteSpace(fileName) ? "dashboard.json" : fileName,
                "application/json",
                Encoding.UTF8.GetBytes(content));
        }
        catch (NotSupportedException)
        {
            Console.Error.WriteLine("[form-studio] Host does not support file download; export skipped.");
        }
    }

    // --- JSON helpers -------------------------------------------------------

    private static string? ReadString(string payload, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
            return ReadString(document.RootElement, name);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
