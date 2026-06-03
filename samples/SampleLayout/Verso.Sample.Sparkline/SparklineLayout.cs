using System.Collections;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Verso.Abstractions;

namespace Verso.Sample.Sparkline;

/// <summary>
/// Isolated (iframe) layout that draws a sparkline from a numeric kernel variable.
///
/// <para>
/// Unlike inline layouts, this layout runs its renderer inside a host-provided
/// sandboxed iframe. It ships a single self-contained <c>main.js</c> entry module
/// (no external scripts, so no extra Content Security Policy is required), subscribes
/// to a kernel variable and pushes updates into the live frame, and surfaces the user's
/// point selection back to the kernel as another variable.
/// </para>
///
/// <para>
/// The renderer reads the host theme tokens (<c>--verso-*</c> custom properties the host
/// applies to the frame's <c>:root</c>) so the chart tracks the active theme without any
/// extension code.
/// </para>
/// </summary>
[VersoExtension]
public sealed class SparklineLayout
    : ILayoutEngine, ILayoutLifecycleHandler, ILayoutInteractionHandler
{
    /// <summary>Name of the numeric kernel variable the sparkline plots.</summary>
    public const string SeriesVariable = "series";

    /// <summary>Name of the kernel variable that receives the selected point index.</summary>
    public const string SelectedVariable = "selectedPoint";

    /// <summary>Interaction type raised by the frame when the user picks a point.</summary>
    public const string SelectPointInteraction = "select-point";

    // Frame-channel message type used to push fresh data into a live renderer. The host
    // prefixes user-supplied types with "ext/" on delivery, so the frame receives this
    // as "ext/data".
    private const string DataMessageType = "data";

    // One unsubscribe action per live renderer instance, keyed by FrameInstanceId.
    private readonly ConcurrentDictionary<string, Action> _unsubscribers = new();

    // --- IExtension ---

    public string ExtensionId => "com.verso.sample.sparkline";
    public string Name => "Sparkline Layout";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Isolated (iframe) layout that draws a sparkline from a kernel variable.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- ILayoutEngine ---

    public string LayoutId => "sparkline";
    public string DisplayName => "Sparkline";
    public string? Icon => null;
    public bool RequiresCustomRenderer => true;

    // This layout owns its rendering surface entirely; the kernel still executes cells.
    public LayoutCapabilities Capabilities => LayoutCapabilities.CellExecute;

    // Run the renderer inside the host's isolation boundary (a sandboxed iframe on
    // browser-hosted clients) rather than sharing the host page.
    public LayoutRendererIsolation RendererIsolation => LayoutRendererIsolation.Isolated;

    public Task<LayoutRendererPackage?> GetRendererPackageAsync(IVersoContext context)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["main.js"] = Encoding.UTF8.GetBytes(RendererScript.MainJs),
        };

        // The renderer is pure <canvas> with no external script, so it needs no extra
        // CSP: the host's default-deny base policy already permits the inline module and
        // inline styles it uses. Returning null keeps the package within that base policy.
        var package = new LayoutRendererPackage(
            EntryPoint: "main.js",
            Files: files,
            ContentSecurityPolicy: null);

        return Task.FromResult<LayoutRendererPackage?>(package);
    }

    // Isolated layouts render inside the frame, so RenderLayoutAsync is never consulted.
    // It returns an empty result to satisfy the interface.
    public Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context)
        => Task.FromResult(new RenderResult("text/html", string.Empty));

    // The frame draws from a kernel variable rather than placing cell components, so no
    // cell is given a container in this layout.
    public Task<CellContainerInfo> GetCellContainerAsync(Guid cellId, IVersoContext context)
        => Task.FromResult(new CellContainerInfo(cellId, X: 0, Y: 0, Width: 0, Height: 0, IsVisible: false));

    public Task OnCellAddedAsync(Guid cellId, int index, IVersoContext context) => Task.CompletedTask;
    public Task OnCellRemovedAsync(Guid cellId, IVersoContext context) => Task.CompletedTask;
    public Task OnCellMovedAsync(Guid cellId, int newIndex, IVersoContext context) => Task.CompletedTask;

    public Dictionary<string, object> GetLayoutMetadata() => new();
    public Task ApplyLayoutMetadata(Dictionary<string, object> metadata, IVersoContext context)
        => Task.CompletedTask;

    // --- ILayoutLifecycleHandler ---

    public Task<IDictionary<string, object>?> OnRendererMountedAsync(LayoutRendererMountContext context)
    {
        var frame = context.Frame;
        var variables = context.Verso.Variables;

        // Push the current series whenever any kernel variable changes. The variable-store
        // change event does not name the changed variable, so we re-read `series` each time.
        void PushSeries()
        {
            if (!frame.IsAlive)
                return;

            var values = ReadSeries(variables);
            _ = frame.PostMessageAsync(DataMessageType, new { values }, context.CancellationToken);
        }

        variables.OnVariablesChanged += PushSeries;
        _unsubscribers[context.FrameInstanceId] = () => variables.OnVariablesChanged -= PushSeries;

        // Hand the renderer its authoritative initial state. This dictionary is delivered
        // to the frame on the host's init message under the "extension" key.
        var init = new Dictionary<string, object>
        {
            ["variable"] = SeriesVariable,
            ["values"] = ReadSeries(variables),
        };

        return Task.FromResult<IDictionary<string, object>?>(init);
    }

    public Task OnRendererUnmountedAsync(LayoutRendererUnmountContext context)
    {
        if (_unsubscribers.TryRemove(context.FrameInstanceId, out var unsubscribe))
            unsubscribe();

        return Task.CompletedTask;
    }

    // --- ILayoutInteractionHandler ---

    public Task OnLayoutInteractionAsync(LayoutInteractionContext context)
    {
        if (context.InteractionType == SelectPointInteraction && TryParseIndex(context.Payload, out var index))
        {
            // Surface the user's choice as a kernel variable. We deliberately do not call
            // RequestRender: the isolated frame owns its DOM and highlights the selection
            // itself.
            context.Verso.Variables.Set(SelectedVariable, index);
        }

        return Task.CompletedTask;
    }

    // --- Helpers ---

    /// <summary>
    /// Reads the <see cref="SeriesVariable"/> as a numeric array, coercing the common
    /// shapes a kernel might store (double[], int[], or any numeric sequence).
    /// </summary>
    private static double[] ReadSeries(IVariableStore variables)
    {
        if (variables.TryGet<double[]>(SeriesVariable, out var doubles) && doubles is not null)
            return doubles;

        if (variables.TryGet<int[]>(SeriesVariable, out var ints) && ints is not null)
            return Array.ConvertAll(ints, static x => (double)x);

        if (variables.TryGet<IEnumerable>(SeriesVariable, out var sequence)
            && sequence is not null and not string)
        {
            var values = new List<double>();
            foreach (var item in sequence)
            {
                if (item is not null && TryToDouble(item, out var value))
                    values.Add(value);
            }

            return values.ToArray();
        }

        return Array.Empty<double>();
    }

    private static bool TryToDouble(object item, out double value)
    {
        switch (item)
        {
            case double d: value = d; return true;
            case float f: value = f; return true;
            case int i: value = i; return true;
            case long l: value = l; return true;
            case decimal m: value = (double)m; return true;
            case IConvertible:
                try { value = Convert.ToDouble(item); return true; }
                catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
                {
                    value = 0;
                    return false;
                }
            default:
                value = 0;
                return false;
        }
    }

    /// <summary>
    /// Parses the selected point index from the interaction payload, accepting either a
    /// bare integer string or a JSON object of the form <c>{ "index": n }</c>.
    /// </summary>
    private static bool TryParseIndex(string payload, out int index)
    {
        index = -1;

        if (string.IsNullOrWhiteSpace(payload))
            return false;

        if (int.TryParse(payload, out index))
            return true;

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Number && root.TryGetInt32(out index))
                return true;

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("index", out var idx)
                && idx.TryGetInt32(out index))
                return true;
        }
        catch (JsonException)
        {
            // Not JSON and not a bare integer; ignore.
        }

        return false;
    }
}
