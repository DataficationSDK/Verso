using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Verso.Showcase.FormStudio.Resources;

namespace Verso.Showcase.FormStudio;

/// <summary>One chart widget's binding: its frame-side id and the kernel variable it reads.</summary>
internal readonly record struct ChartBinding(string Id, string SourceVar);

/// <summary>
/// The layout's persisted document: the whole canvas the user built — every widget, its position,
/// size, binding, and configuration — plus the auto-run flag. Unlike Grid Studio (whose document is
/// just a single bound variable name), Form Studio is an app builder, so the built app *is* the
/// document, mirroring the Image Studio layer document.
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
    // host without depending on that timing: two inputs (bound to minUnits / region) and two
    // charts over the kernel's chartData DataBlock. The labels are named by key rather than
    // written out, so they resolve in the language of whoever opens the notebook rather than the
    // one it was saved from; the dropdown's choices are values the notebook's own code compares,
    // so they are data and stay as written.
    private static string DefaultJson => JsonSerializer.Serialize(new
    {
        autoRun = true,
        widgets = new object[]
        {
            new
            {
                id = "w_slider", kind = "slider", x = 24, y = 20, w = 300, h = 96,
                labelKey = "Seed_MinimumUnits", bindVar = "minUnits", value = 40,
                config = new { min = 0, max = 200, step = 5 },
            },
            new
            {
                id = "w_region", kind = "dropdown", x = 340, y = 20, w = 260, h = 92,
                labelKey = "Seed_Region", bindVar = "region", value = "All",
                config = new { options = new[] { "All", "North", "South", "East", "West" } },
            },
            new
            {
                id = "w_bar", kind = "chart", x = 24, y = 140, w = 560, h = 300,
                labelKey = "Seed_UnitsByMonth", bindVar = "",
                config = new { sourceVar = "chartData", chartType = "bar", xColumn = "Month", yColumns = new[] { "Units" }, color = "#5b8def" },
            },
            new
            {
                id = "w_line", kind = "chart", x = 600, y = 140, w = 480, h = 300,
                labelKey = "Seed_RevenueByMonth", bindVar = "",
                config = new { sourceVar = "chartData", chartType = "line", xColumn = "Month", yColumns = new[] { "Revenue" }, color = "#3fb27f" },
            },
        },
    });

    // Set once the frame authors a document or the notebook restores one. Until then it stays
    // null and Json falls back to DefaultJson, which is a property rather than a constant so
    // that its labels resolve in the language of whoever is asking. Resolving them once at
    // construction would serve the first reader's language to everybody after them.
    private string? _authored;

    /// <summary>The canonical canvas document as authored by the frame.</summary>
    public string Json => _authored ?? DefaultJson;

    /// <summary>Whether input changes auto re-run downstream cells.</summary>
    public bool AutoRun { get; private set; } = true;

    /// <summary>The variable binding of every chart widget on the canvas.</summary>
    public IReadOnlyList<ChartBinding> Charts { get; private set; } = Array.Empty<ChartBinding>();

    /// <summary>Starts from the built-in default dashboard until a saved document replaces it.</summary>
    /// <remarks>
    /// Only the parts C# reads are parsed here. The labels are not among them, so nothing the
    /// reader will see is resolved at this point, and the chart bindings and auto-run flag this
    /// does read are the same in every language.
    /// </remarks>
    public FormDocument()
    {
        try
        {
            using var document = JsonDocument.Parse(DefaultJson);
            Parse(document.RootElement);
        }
        catch (JsonException)
        {
            // The built-in document is written here, so this cannot happen in a shipped build.
        }
    }

    /// <summary>Replaces the document from a frame-authored JSON payload, re-parsing the parts C# uses.</summary>
    public void Update(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;
        try
        {
            using var document = JsonDocument.Parse(json);
            Parse(document.RootElement);
            _authored = WithoutResolvedLabels(json);
        }
        catch (JsonException)
        {
            // Keep the prior document rather than corrupting it with an unparseable payload.
        }
    }

    /// <summary>
    /// The document as the frame should see it, with every keyed label resolved into the
    /// reader's language.
    /// </summary>
    /// <remarks>
    /// The frame draws labels; the notebook stores keys. Sending the stored shape would put
    /// <c>Seed_Region</c> on a dropdown, and storing the sent shape would freeze the notebook in
    /// the language whoever saved it last happened to be reading. The key is left in place beside
    /// the resolved label so the frame can hand it back untouched, and so that clearing it stays
    /// the frame's way of saying the reader typed a label of their own.
    /// </remarks>
    public string ForDisplay()
    {
        return Rewrite(Json, widget =>
        {
            if (widget["labelKey"]?.GetValue<string>() is not { Length: > 0 } key)
                return;

            if (Resolve(key, widget["labelArgKey"]?.GetValue<string>()) is { } label)
                widget["label"] = label;
        });
    }

    /// <summary>
    /// Drops the resolved label from any widget still carrying a key, which is the form the
    /// notebook keeps. Doing this on the way in rather than trusting the frame means a renderer
    /// that forgets to strip the label cannot bake a language into the file.
    /// </summary>
    private static string WithoutResolvedLabels(string json)
        => Rewrite(json, widget =>
        {
            if (widget["labelKey"] is not null)
                widget.Remove("label");
        });

    /// <summary>
    /// Applies an edit to every widget in a document and returns the result, leaving the document
    /// untouched if it cannot be read as one.
    /// </summary>
    private static string Rewrite(string json, Action<JsonObject> edit)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["widgets"] is not JsonArray widgets)
                return json;

            foreach (var widget in widgets)
            {
                if (widget is JsonObject obj)
                    edit(obj);
            }

            return root.ToJsonString();
        }
        catch (JsonException)
        {
            return json;
        }
    }

    /// <summary>
    /// Resolves a label key in the current language, optionally composing it with a second key.
    /// A chart's default title is "{0} chart" over a chart-kind word, and both halves have to
    /// move together or the label ends up half translated.
    /// </summary>
    private static string? Resolve(string key, string? argKey)
    {
        if (Strings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) is not { } text)
            return null;

        if (string.IsNullOrEmpty(argKey))
            return text;

        var arg = Strings.ResourceManager.GetString(argKey, CultureInfo.CurrentUICulture) ?? argKey;
        return string.Format(CultureInfo.CurrentCulture, text, arg);
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
