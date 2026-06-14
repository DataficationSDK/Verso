using System.Text.Json;

namespace Verso.Layout.FormStudio;

/// <summary>One chart widget's binding: its frame-side id and the kernel variable it reads.</summary>
internal readonly record struct ChartBinding(string Id, string SourceVar);

/// <summary>
/// The layout's persisted document: the whole canvas the user built — every widget, its position,
/// size, binding, and configuration — plus the auto-run flag. Unlike Grid Studio (whose document is
/// just a single bound variable name), Form Studio is an app builder, so the built app *is* the
/// document, mirroring the image-editor layer document.
/// </summary>
/// <remarks>
/// The canvas is authored entirely in the frame, so the source of truth for geometry and widget
/// configuration is the JSON the frame sends. This class keeps that JSON verbatim for round-trip
/// persistence and parses out only what the C# side acts on: the auto-run flag and each chart's
/// variable binding (so a variable change can refresh the right chart). Storing the frame's own
/// JSON avoids re-encoding the widget schema in two places.
/// </remarks>
internal sealed class FormDocument
{
    // A built-in starter dashboard, shown whenever no saved layout has been applied. The host
    // loads an extension-provided layout from a "#!extension" code cell, which runs after it
    // restores saved layout metadata, so a fresh open has nothing to restore into this layout yet.
    // Shipping a default document here makes the sample present a configured dashboard on every
    // host without depending on that timing — two inputs (bound to minUnits / region) and two
    // charts over the kernel's chartData DataBlock.
    private const string DefaultJson = """
    {"autoRun":true,"widgets":[
      {"id":"w_slider","kind":"slider","x":24,"y":20,"w":300,"h":96,"label":"Minimum units","bindVar":"minUnits","value":40,"config":{"min":0,"max":200,"step":5}},
      {"id":"w_region","kind":"dropdown","x":340,"y":20,"w":260,"h":92,"label":"Region","bindVar":"region","value":"All","config":{"options":["All","North","South","East","West"]}},
      {"id":"w_bar","kind":"chart","x":24,"y":140,"w":560,"h":300,"label":"Units by month","bindVar":"","config":{"sourceVar":"chartData","chartType":"bar","xColumn":"Month","yColumns":["Units"],"color":"#5b8def"}},
      {"id":"w_line","kind":"chart","x":600,"y":140,"w":480,"h":300,"label":"Revenue by month","bindVar":"","config":{"sourceVar":"chartData","chartType":"line","xColumn":"Month","yColumns":["Revenue"],"color":"#3fb27f"}}
    ]}
    """;

    /// <summary>The canonical canvas document as authored by the frame.</summary>
    public string Json { get; private set; } = DefaultJson;

    /// <summary>Whether input changes auto re-run downstream cells.</summary>
    public bool AutoRun { get; private set; } = true;

    /// <summary>The variable binding of every chart widget on the canvas.</summary>
    public IReadOnlyList<ChartBinding> Charts { get; private set; } = Array.Empty<ChartBinding>();

    /// <summary>Starts from the built-in default dashboard until a saved document replaces it.</summary>
    public FormDocument() => Update(DefaultJson);

    /// <summary>Replaces the document from a frame-authored JSON payload, re-parsing the parts C# uses.</summary>
    public void Update(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;
        try
        {
            using var document = JsonDocument.Parse(json);
            Parse(document.RootElement);
            Json = json;
        }
        catch (JsonException)
        {
            // Keep the prior document rather than corrupting it with an unparseable payload.
        }
    }

    public Dictionary<string, object> ToMetadata() => new(StringComparer.Ordinal)
    {
        ["doc"] = Json,
    };

    public static FormDocument FromMetadata(Dictionary<string, object> metadata)
    {
        var document = new FormDocument();
        if (metadata.TryGetValue("doc", out var value) && AsString(value) is { } json)
            document.Update(json);
        return document;
    }

    private void Parse(JsonElement root)
    {
        AutoRun = !root.TryGetProperty("autoRun", out var autoRun)
            || autoRun.ValueKind != JsonValueKind.False;

        var charts = new List<ChartBinding>();
        if (root.TryGetProperty("widgets", out var widgets) && widgets.ValueKind == JsonValueKind.Array)
        {
            foreach (var widget in widgets.EnumerateArray())
            {
                if (widget.ValueKind != JsonValueKind.Object)
                    continue;
                if (ReadString(widget, "kind") != "chart")
                    continue;

                var id = ReadString(widget, "id");
                var source = widget.TryGetProperty("config", out var config) && config.ValueKind == JsonValueKind.Object
                    ? ReadString(config, "sourceVar")
                    : null;
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(source))
                    charts.Add(new ChartBinding(id!, source!));
            }
        }
        Charts = charts;
    }

    private static string? ReadString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    // Metadata round-trips through JSON, so a value may arrive as a string (in-memory) or a
    // JsonElement (rehydrated from the notebook file). Accept both.
    private static string? AsString(object? value) => value switch
    {
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        _ => value?.ToString(),
    };
}
