using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Verso.Abstractions;

namespace Verso.Showcase.GridStudio;

/// <summary>
/// "Grid Studio" — an isolated (iframe) layout that presents a kernel <c>DataBlock</c> as an
/// editable, Excel-like spreadsheet. The frame owns the grid surface (Jspreadsheet CE); this
/// class binds the grid to a kernel variable, streams the DataBlock's contents into the frame,
/// and rebuilds edits back into a DataBlock the rest of the notebook can query.
/// </summary>
/// <remarks>
/// <para>
/// The grid's data is not the layout's document — the bound <c>DataBlock</c> in the kernel's
/// variable store is. The layout persists only the binding (which variable) through its own
/// metadata, so a reopened notebook rebinds to the DataBlock a code cell produces.
/// </para>
/// <para>
/// DataBlock access is entirely reflection-based (see <see cref="DataBlockInterop"/>), so the
/// extension references only <c>Verso.Abstractions</c> and write-back uses the live instance's
/// own assembly — the one the kernel loaded — sidestepping assembly-identity mismatches.
/// </para>
/// </remarks>
[VersoExtension]
public sealed class GridStudioLayout
    : ILayoutEngine, ILayoutLifecycleHandler, ILayoutInteractionHandler
{
    // Frame-channel message type. The host prefixes it with "ext/", so the frame sees "ext/data".
    private const string DataMessage = "data";

    private GridDocument _doc = new();

    // The most recent DataBlock instance read from the store. Its assembly is the one the kernel
    // loaded, which is exactly what write-back must construct into.
    private object? _lastSource;

    // Set while we write our own rebuilt DataBlock back to the store, so the resulting
    // OnVariablesChanged does not bounce the data back and clobber the editor mid-edit.
    private volatile bool _applyingCommit;

    private readonly ConcurrentDictionary<string, ILayoutFrameChannel> _frames = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Action> _unsubscribers = new(StringComparer.Ordinal);

    // --- IExtension ---

    public string ExtensionId => "com.verso.showcase.grid-studio";
    public string Name => "Grid Studio Layout";
    public string Version => "1.0.0";
    public string? Author => "Datafication";
    public string? Description => "Isolated layout that presents a kernel DataBlock as an editable spreadsheet.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- ILayoutEngine ---

    public string LayoutId => "grid-studio";
    public string DisplayName => "Grid Studio";

    public string? Icon =>
        "<svg viewBox=\"0 0 16 16\" fill=\"none\" xmlns=\"http://www.w3.org/2000/svg\">" +
        "<rect x=\"2\" y=\"2.5\" width=\"12\" height=\"11\" rx=\"1.5\" stroke=\"currentColor\"/>" +
        "<path d=\"M2 6h12M2 9.5h12M6 6v7.5M10 6v7.5\" stroke=\"currentColor\"/>" +
        "</svg>";

    public bool RequiresCustomRenderer => true;

    // The frame owns rendering; the kernel still executes cells (they build the bound DataBlock).
    public LayoutCapabilities Capabilities => LayoutCapabilities.CellExecute;

    public LayoutRendererIsolation RendererIsolation => LayoutRendererIsolation.Isolated;

    public Task<LayoutRendererPackage?> GetRendererPackageAsync(IVersoContext context)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["main.js"] = Encoding.UTF8.GetBytes(RendererScript.MainJs),
        };

        // The grid renders with inline scripts/styles and inline data: images only — all within
        // the host's base frame policy — so no extra CSP is needed.
        var package = new LayoutRendererPackage(
            EntryPoint: "main.js",
            Files: files,
            ContentSecurityPolicy: null);

        return Task.FromResult<LayoutRendererPackage?>(package);
    }

    // Isolated layouts render inside the frame; RenderLayoutAsync is never consulted.
    public Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context)
        => Task.FromResult(new RenderResult("text/html", string.Empty));

    // No cell is placed on the grid surface — the spreadsheet is the document, not the cells.
    public Task<CellContainerInfo> GetCellContainerAsync(Guid cellId, IVersoContext context)
        => Task.FromResult(new CellContainerInfo(cellId, X: 0, Y: 0, Width: 0, Height: 0, IsVisible: false));

    public Task OnCellAddedAsync(Guid cellId, int index, IVersoContext context) => Task.CompletedTask;
    public Task OnCellRemovedAsync(Guid cellId, IVersoContext context) => Task.CompletedTask;
    public Task OnCellMovedAsync(Guid cellId, int newIndex, IVersoContext context) => Task.CompletedTask;

    public Dictionary<string, object> GetLayoutMetadata() => _doc.ToMetadata();

    public Task ApplyLayoutMetadata(Dictionary<string, object> metadata, IVersoContext context)
    {
        _doc = GridDocument.FromMetadata(metadata);
        return Task.CompletedTask;
    }

    // --- ILayoutLifecycleHandler ---

    public Task<IReadOnlyDictionary<string, object>?> OnRendererMountedAsync(LayoutRendererMountContext context)
    {
        var frame = context.Frame;
        var variables = context.Verso.Variables;
        _frames[context.FrameInstanceId] = frame;

        // Re-read the bound variable whenever any kernel variable changes (the store event does
        // not name the changed variable) and push the fresh DataBlock contents to the frame.
        void PushOnChange()
        {
            if (_applyingCommit || !frame.IsAlive)
                return;
            _ = frame.PostMessageAsync(DataMessage, BuildDataPayload(variables), context.CancellationToken);
        }

        variables.OnVariablesChanged += PushOnChange;
        _unsubscribers[context.FrameInstanceId] = () => variables.OnVariablesChanged -= PushOnChange;

        var seed = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["sourceVar"] = _doc.SourceVar,
            ["variables"] = ListDataBlockVars(variables),
        };
        if (ReadGrid(variables) is { } grid)
            seed["data"] = grid;

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
            case "set-source":
                if (ReadString(context.Payload, "sourceVar") is { } name && !string.IsNullOrWhiteSpace(name))
                {
                    _doc.SourceVar = name.Trim();
                    PushDataToAll(variables, context.CancellationToken);
                }
                break;

            case "commit":
                ApplyCommit(context.Payload, variables);
                break;

            case "refresh":
                PushDataToAll(variables, context.CancellationToken);
                break;

            case "export":
                HandleExport(context);
                break;
        }

        return Task.CompletedTask;
    }

    // --- Commit (frame -> DataBlock) ----------------------------------------

    private void ApplyCommit(string payload, IVariableStore variables)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return;

        var coreAssembly = ResolveCoreAssembly(variables);
        if (coreAssembly is null)
            return; // No DataBlock has ever been seen, so we cannot construct one.

        object? rebuilt;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            var columns = ReadStringArray(root, "columns");
            var types = ReadStringArray(root, "types");
            var rows = root.TryGetProperty("rows", out var rowsElement) && rowsElement.ValueKind == JsonValueKind.Array
                ? rowsElement.EnumerateArray().ToList()
                : new List<JsonElement>();

            if (columns.Count == 0)
                return;

            // Build while the JsonDocument is alive — the row elements borrow from it.
            rebuilt = DataBlockInterop.Build(coreAssembly, columns, types, rows);
        }
        catch (JsonException)
        {
            return;
        }

        if (rebuilt is null)
            return;

        // Write back under the bound name. Guard the self-induced change event so it does not
        // echo the data back into the frame the user is editing.
        _applyingCommit = true;
        try
        {
            variables.Set(_doc.SourceVar, rebuilt);
            _lastSource = rebuilt;
        }
        finally
        {
            _applyingCommit = false;
        }
    }

    private void HandleExport(LayoutInteractionContext context)
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
                string.IsNullOrWhiteSpace(fileName) ? "grid.csv" : fileName,
                "text/csv",
                Encoding.UTF8.GetBytes(content));
        }
        catch (NotSupportedException)
        {
            Console.Error.WriteLine("[grid-studio] Host does not support file download; export skipped.");
        }
    }

    // --- Reading the bound variable -----------------------------------------

    private GridData? ReadGrid(IVariableStore variables)
    {
        if (variables.TryGet<object>(_doc.SourceVar, out var value) && value is not null)
        {
            _lastSource = value;
            return DataBlockInterop.IsDataBlock(value) ? DataBlockInterop.Read(value) : null;
        }
        return null;
    }

    private object BuildDataPayload(IVariableStore variables)
        => new { data = ReadGrid(variables), sourceVar = _doc.SourceVar, variables = ListDataBlockVars(variables) };

    // The names of every variable currently holding a DataBlock, for the frame's source picker.
    // Recomputed on each push, so it tracks cells being run (including Run All) while the layout
    // is active, since executing cells raises the variable-store change event.
    private List<string> ListDataBlockVars(IVariableStore variables)
    {
        var names = new List<string>();
        foreach (var descriptor in variables.GetAll())
        {
            var isDataBlock = DataBlockInterop.IsDataBlock(descriptor.Value)
                || string.Equals(descriptor.Type?.FullName, "Datafication.Core.Data.DataBlock", StringComparison.Ordinal);
            if (isDataBlock && !names.Contains(descriptor.Name))
                names.Add(descriptor.Name);
        }
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private void PushDataToAll(IVariableStore variables, CancellationToken ct)
    {
        var payload = BuildDataPayload(variables);
        foreach (var frame in _frames.Values)
        {
            if (frame.IsAlive)
                _ = frame.PostMessageAsync(DataMessage, payload, ct);
        }
    }

    // Prefer the live instance's assembly (matches the kernel exactly); otherwise look for a
    // loaded Datafication.Core that exposes the DataBlock type.
    private Assembly? ResolveCoreAssembly(IVariableStore variables)
    {
        ReadGrid(variables); // refresh _lastSource from the current binding if present
        if (_lastSource is not null)
            return _lastSource.GetType().Assembly;

        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetType("Datafication.Core.Data.DataBlock") is not null);
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

    private static List<string> ReadStringArray(JsonElement root, string name)
    {
        var result = new List<string>();
        if (root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
                result.Add(item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.ToString());
        }
        return result;
    }
}
