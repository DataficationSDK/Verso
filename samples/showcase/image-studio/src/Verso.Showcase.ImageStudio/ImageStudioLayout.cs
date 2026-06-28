using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Verso.Abstractions;
using Verso.Showcase.ImageStudio.Model;

namespace Verso.Showcase.ImageStudio;

/// <summary>
/// "Image Studio" — an isolated (iframe) layout that presents the notebook as a layered image
/// document. The frame owns the editor surface (canvas compositor, layer panel, tool palette);
/// this class owns the document model, persists it through the layout's own metadata, and
/// streams updates into the live frame.
/// </summary>
/// <remarks>
/// <para>
/// The layer stack is the layout's document, persisted into the notebook's <c>layouts</c>
/// block via <see cref="GetLayoutMetadata"/> / <see cref="ApplyLayoutMetadata"/> — no host
/// changes, just the existing metadata round-trip.
/// </para>
/// <para>
/// A <c>procedural</c> layer defers its drawing to a kernel variable. The lifecycle handler
/// subscribes to the variable store and pushes the variable's value into the frame whenever it
/// changes, so editing and re-running a code cell repaints that layer live — any layer can be
/// code.
/// </para>
/// </remarks>
[VersoExtension]
public sealed class ImageStudioLayout
    : ILayoutEngine, ILayoutLifecycleHandler, ILayoutInteractionHandler
{
    // Frame-channel message types. The host prefixes these with "ext/" on delivery, so the
    // frame receives them as "ext/document", "ext/vars", and "ext/export-request".
    private const string DocumentMessage = "document";
    private const string VarsMessage = "vars";
    private const string ExportRequestMessage = "export-request";

    // The live document for this notebook session. Never null; replaced on metadata load.
    private LayerDocument _doc = LayerDocument.Seed();

    // One live frame channel per mounted renderer instance, and one unsubscribe action each.
    private readonly ConcurrentDictionary<string, ILayoutFrameChannel> _frames = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Action> _unsubscribers = new(StringComparer.Ordinal);

    // --- IExtension ---

    public string ExtensionId => "com.verso.showcase.image-studio";
    public string Name => "Image Studio Layout";
    public string Version => "1.0.0";
    public string? Author => "Datafication";
    public string? Description => "Isolated layout that presents the notebook as a layered image document.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- ILayoutEngine ---

    public string LayoutId => "image-studio";
    public string DisplayName => "Image Studio";

    public string? Icon =>
        "<svg viewBox=\"0 0 16 16\" fill=\"none\" xmlns=\"http://www.w3.org/2000/svg\">" +
        "<rect x=\"2\" y=\"3\" width=\"12\" height=\"9\" rx=\"1.5\" stroke=\"currentColor\"/>" +
        "<circle cx=\"6\" cy=\"6.5\" r=\"1.2\" fill=\"currentColor\"/>" +
        "<path d=\"M3 11l3-3 2 2 3-3 2 2\" stroke=\"currentColor\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" +
        "</svg>";

    public bool RequiresCustomRenderer => true;

    // The frame owns rendering; the kernel still executes cells (procedural layers read its variables).
    public LayoutCapabilities Capabilities => LayoutCapabilities.CellExecute;

    public LayoutRendererIsolation RendererIsolation => LayoutRendererIsolation.Isolated;

    public Task<LayoutRendererPackage?> GetRendererPackageAsync(IVersoContext context)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["main.js"] = Encoding.UTF8.GetBytes(RendererScript.MainJs),
        };

        // Pure canvas + inline styles + data: thumbnails — all permitted by the host's base
        // frame CSP, so no extra policy is needed.
        var package = new LayoutRendererPackage(
            EntryPoint: "main.js",
            Files: files,
            ContentSecurityPolicy: null);

        return Task.FromResult<LayoutRendererPackage?>(package);
    }

    // Isolated layouts render inside the frame; RenderLayoutAsync is never consulted.
    public Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context)
        => Task.FromResult(new RenderResult("text/html", string.Empty));

    // No cell is placed on the editor surface — the document is the layer stack, not the cells.
    public Task<CellContainerInfo> GetCellContainerAsync(Guid cellId, IVersoContext context)
        => Task.FromResult(new CellContainerInfo(cellId, X: 0, Y: 0, Width: 0, Height: 0, IsVisible: false));

    public Task OnCellAddedAsync(Guid cellId, int index, IVersoContext context) => Task.CompletedTask;
    public Task OnCellRemovedAsync(Guid cellId, IVersoContext context) => Task.CompletedTask;
    public Task OnCellMovedAsync(Guid cellId, int newIndex, IVersoContext context) => Task.CompletedTask;

    public Dictionary<string, object> GetLayoutMetadata() => _doc.ToMetadata();

    public Task ApplyLayoutMetadata(Dictionary<string, object> metadata, IVersoContext context)
    {
        _doc = LayerDocument.FromMetadata(metadata);
        return Task.CompletedTask;
    }

    // --- ILayoutLifecycleHandler ---

    public Task<IReadOnlyDictionary<string, object>?> OnRendererMountedAsync(LayoutRendererMountContext context)
    {
        var frame = context.Frame;
        var variables = context.Verso.Variables;
        _frames[context.FrameInstanceId] = frame;

        // Push procedural-layer variable values whenever any kernel variable changes. The store
        // event does not name the changed variable, so we re-read the referenced set each time.
        void PushVars()
        {
            if (!frame.IsAlive)
                return;

            var vars = CollectProceduralVars(variables);
            if (vars.Count > 0)
                _ = frame.PostMessageAsync(VarsMessage, new { vars }, context.CancellationToken);
        }

        variables.OnVariablesChanged += PushVars;
        _unsubscribers[context.FrameInstanceId] = () => variables.OnVariablesChanged -= PushVars;

        // Hand the renderer its authoritative initial state on the host's init message (under
        // the "extension" key). Include any procedural variables a cell already produced.
        var seed = new Dictionary<string, object> { ["document"] = _doc };
        var initialVars = CollectProceduralVars(variables);
        if (initialVars.Count > 0)
            seed["vars"] = initialVars;

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
        if (context.InteractionType == "export")
        {
            HandleExport(context);
            return Task.CompletedTask;
        }

        if (ApplyInteraction(context.InteractionType, context.Payload))
        {
            PushDocumentToAll(context.CancellationToken);
            // Re-push procedural data so a layer just bound to (or created on) a variable that
            // already exists paints immediately, without waiting for the next variable change.
            PushVarsToAll(context.Verso.Variables, context.CancellationToken);
        }

        return Task.CompletedTask;
    }

    // --- Interaction handling ---

    private bool ApplyInteraction(string type, string payload)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return false;
        }

        switch (type)
        {
            case "add-layer":
            {
                var kind = GetString(root, "kind") ?? "solid";
                var layer = new Layer
                {
                    Kind = kind,
                    Name = GetString(root, "name") ?? DefaultName(kind),
                    Props = DefaultProps(kind),
                    SourceVar = kind == "procedural" ? (GetString(root, "sourceVar") ?? "ops") : null,
                };
                _doc.Layers.Add(layer); // new layers land on top of the stack
                return true;
            }

            case "remove-layer":
            {
                var id = GetString(root, "id");
                return id is not null && _doc.Layers.RemoveAll(l => l.Id == id) > 0;
            }

            case "reorder":
            {
                if (!root.TryGetProperty("order", out var order) || order.ValueKind != JsonValueKind.Array)
                    return false;

                var ids = order.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .ToList();

                var byId = _doc.Layers.ToDictionary(l => l.Id, StringComparer.Ordinal);
                var reordered = new List<Layer>(_doc.Layers.Count);
                foreach (var id in ids)
                    if (byId.Remove(id, out var layer))
                        reordered.Add(layer);
                // Preserve any layers the client did not mention (defensive).
                reordered.AddRange(_doc.Layers.Where(l => byId.ContainsKey(l.Id)));

                if (reordered.Count != _doc.Layers.Count)
                    return false;

                _doc.Layers = reordered;
                return true;
            }

            case "set-visible":
            {
                var layer = Find(root);
                if (layer is null) return false;
                layer.Visible = GetBool(root, "visible") ?? !layer.Visible;
                return true;
            }

            case "set-opacity":
            {
                var layer = Find(root);
                if (layer is null || GetDouble(root, "opacity") is not { } value) return false;
                layer.Opacity = Math.Clamp(value, 0.0, 1.0);
                return true;
            }

            case "set-blend":
            {
                var layer = Find(root);
                var blend = GetString(root, "blend");
                if (layer is null || blend is null) return false;
                layer.Blend = blend;
                return true;
            }

            case "rename":
            {
                var layer = Find(root);
                var name = GetString(root, "name");
                if (layer is null || name is null) return false;
                layer.Name = name;
                return true;
            }

            case "set-source":
            {
                var layer = Find(root);
                if (layer is null) return false;
                var src = GetString(root, "sourceVar");
                layer.SourceVar = string.IsNullOrWhiteSpace(src) ? null : src.Trim();
                return true;
            }

            case "set-prop":
            {
                var layer = Find(root);
                var key = GetString(root, "key");
                if (layer is null || string.IsNullOrEmpty(key) || !root.TryGetProperty("value", out var value))
                    return false;
                var clr = ToClr(value);
                if (clr is null) layer.Props.Remove(key);
                else layer.Props[key] = clr;
                return true;
            }

            default:
                return false;
        }
    }

    private void HandleExport(LayoutInteractionContext context)
    {
        string? format, dataUrl, svg;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(context.Payload) ? "{}" : context.Payload);
            format = GetString(document.RootElement, "format");
            dataUrl = GetString(document.RootElement, "dataUrl");
            svg = GetString(document.RootElement, "svg");
        }
        catch (JsonException)
        {
            return;
        }

        // SVG export carries the markup directly (the layers are vector primitives, so the frame
        // re-emits them as SVG). PNG export carries a base64 data URL of the rasterized canvas.
        if (string.Equals(format, "svg", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(svg))
                return;
            DeliverDownload(context, "composite.svg", "image/svg+xml", Encoding.UTF8.GetBytes(svg));
            return;
        }

        if (string.IsNullOrEmpty(dataUrl))
            return;

        var comma = dataUrl.IndexOf(',');
        var base64 = comma >= 0 ? dataUrl[(comma + 1)..] : dataUrl;

        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch (FormatException) { return; }

        DeliverDownload(context, "composite.png", "image/png", bytes);
    }

    private static void DeliverDownload(LayoutInteractionContext context, string fileName, string contentType, byte[] bytes)
    {
        try
        {
            // Best effort: not every host can deliver a download. A host that cannot throws
            // NotSupportedException, which we swallow rather than fail the interaction.
            _ = context.Verso.RequestFileDownloadAsync(fileName, contentType, bytes);
        }
        catch (NotSupportedException)
        {
            Console.Error.WriteLine("[image-studio] Host does not support file download; export skipped.");
        }
    }

    // --- Host-driven export ---

    /// <summary>
    /// Asks every live frame to produce an export of the given format ("png" or "svg"). The
    /// frame builds the bytes and sends them back as an "export" interaction, which
    /// <see cref="HandleExport"/> turns into a host file download. Triggered by the Export
    /// menu actions so the export lives in the host chrome rather than the frame.
    /// </summary>
    internal void RequestFrameExport(string format, CancellationToken ct)
    {
        foreach (var frame in _frames.Values)
        {
            if (frame.IsAlive)
                _ = frame.PostMessageAsync(ExportRequestMessage, new { format }, ct);
        }
    }

    private void PushDocumentToAll(CancellationToken ct)
    {
        foreach (var frame in _frames.Values)
        {
            if (frame.IsAlive)
                _ = frame.PostMessageAsync(DocumentMessage, new { document = _doc }, ct);
        }
    }

    private void PushVarsToAll(IVariableStore variables, CancellationToken ct)
    {
        var vars = CollectProceduralVars(variables);
        if (vars.Count == 0)
            return;

        foreach (var frame in _frames.Values)
        {
            if (frame.IsAlive)
                _ = frame.PostMessageAsync(VarsMessage, new { vars }, ct);
        }
    }

    private Dictionary<string, object> CollectProceduralVars(IVariableStore variables)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var layer in _doc.Layers)
        {
            if (layer.Kind != "procedural" || string.IsNullOrWhiteSpace(layer.SourceVar))
                continue;
            if (result.ContainsKey(layer.SourceVar))
                continue;
            if (variables.TryGet<object>(layer.SourceVar, out var value) && value is not null)
                result[layer.SourceVar] = value;
        }
        return result;
    }

    private Layer? Find(JsonElement root)
    {
        var id = GetString(root, "id");
        return id is null ? null : _doc.Layers.FirstOrDefault(l => l.Id == id);
    }

    // --- Defaults for newly added layers ---

    private static string DefaultName(string kind) => kind switch
    {
        "solid" => "Solid",
        "linear-gradient" => "Gradient",
        "radial-gradient" => "Radial",
        "checkerboard" => "Checker",
        "stripes" => "Stripes",
        "dots" => "Dots",
        "rings" => "Rings",
        "text" => "Text",
        "procedural" => "Procedural",
        _ => "Layer",
    };

    private static Dictionary<string, object> DefaultProps(string kind) => kind switch
    {
        "solid" => new() { ["color"] = "#5b8def" },
        "linear-gradient" => new()
        {
            ["angle"] = 90.0,
            ["stops"] = new object[]
            {
                new Dictionary<string, object> { ["pos"] = 0.0, ["color"] = "#5b8def" },
                new Dictionary<string, object> { ["pos"] = 1.0, ["color"] = "#a06bd6" },
            },
        },
        "radial-gradient" => new()
        {
            ["cx"] = 0.5,
            ["cy"] = 0.5,
            ["radius"] = 0.4,
            ["stops"] = new object[]
            {
                new Dictionary<string, object> { ["pos"] = 0.0, ["color"] = "#ffffff" },
                new Dictionary<string, object> { ["pos"] = 1.0, ["color"] = "#5b8def00" },
            },
        },
        "checkerboard" => new() { ["size"] = 32.0, ["color1"] = "#ffffff", ["color2"] = "#c8c8c8" },
        "stripes" => new() { ["width"] = 24.0, ["angle"] = 45.0, ["color1"] = "#222831", ["color2"] = "#3a4150" },
        "dots" => new() { ["size"] = 40.0, ["radius"] = 3.0, ["color"] = "#ffffff" },
        "rings" => new() { ["cx"] = 0.5, ["cy"] = 0.5, ["count"] = 6.0, ["color"] = "#ffffff", ["width"] = 2.0 },
        "text" => new()
        {
            ["text"] = "Text",
            ["size"] = 0.15,
            ["color"] = "#ffffff",
            ["x"] = 0.5,
            ["y"] = 0.5,
            ["align"] = "center",
            ["weight"] = "700",
        },
        "procedural" => new(),
        _ => new(),
    };

    // --- JSON helpers ---

    private static string? GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    private static bool? GetBool(JsonElement root, string name)
        => root.TryGetProperty(name, out var e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? e.GetBoolean()
            : null;

    private static double? GetDouble(JsonElement root, string name)
        => root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : null;

    // Converts an interaction-supplied value into a plain CLR graph so it survives both the
    // re-serialization to the frame and persistence into metadata (a borrowed JsonElement would
    // dangle once its JsonDocument is disposed). Numbers are normalized to double.
    private static object? ToClr(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => element.EnumerateArray().Select(ToClr).ToList(),
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ToClr(p.Value)),
        _ => element.GetRawText(),
    };
}
