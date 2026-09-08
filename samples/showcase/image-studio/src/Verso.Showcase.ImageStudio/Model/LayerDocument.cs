using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Verso.Showcase.ImageStudio.Resources;

namespace Verso.Showcase.ImageStudio.Model;

/// <summary>
/// A single layer in the image document. A layer is a declarative recipe the frame draws
/// onto a canvas — there is no pixel buffer to carry around, so the whole document stays
/// small, JSON-clean, and version-control friendly.
/// </summary>
/// <remarks>
/// <see cref="Props"/> is kind-specific (gradient stops, colors, tile sizes, text, …). A
/// <c>procedural</c> layer leaves the drawing to a kernel variable named by
/// <see cref="SourceVar"/>: the layout pushes that variable's value into the frame whenever
/// it changes, so re-running a code cell repaints the layer live.
/// </remarks>
public sealed class Layer
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("n");

    /// <summary>
    /// The name the reader typed, or <c>null</c> for a layer still carrying the name it was
    /// created with. Only a name a person chose is stored, because only that one is theirs:
    /// a built-in name is stored as <see cref="NameKey"/> instead, so that it can be shown in
    /// whatever language the notebook is opened in.
    /// </summary>
    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>
    /// The resource key behind a built-in layer name, resolved through
    /// <see cref="DisplayName"/> every time the layer is shown. Ignored once
    /// <see cref="Name"/> is set.
    /// </summary>
    [JsonPropertyName("nameKey")] public string? NameKey { get; set; }

    /// <summary>
    /// The name to show: what the reader typed, else the built-in name in the reader's
    /// language, else the generic fallback. Never written to the notebook.
    /// </summary>
    [JsonIgnore]
    public string DisplayName => Name ?? ResolveNameKey(NameKey) ?? Strings.Layer_Default;

    /// <summary>
    /// Looks a built-in name up in the current language. An unknown key returns <c>null</c>
    /// rather than throwing, so a document written by a newer build still opens.
    /// </summary>
    private static string? ResolveNameKey(string? key)
        => string.IsNullOrEmpty(key) ? null : Strings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture);

    [JsonPropertyName("kind")] public string Kind { get; set; } = "solid";
    [JsonPropertyName("visible")] public bool Visible { get; set; } = true;
    [JsonPropertyName("opacity")] public double Opacity { get; set; } = 1.0;
    [JsonPropertyName("blend")] public string Blend { get; set; } = "normal";
    [JsonPropertyName("props")] public Dictionary<string, object> Props { get; set; } = new();

    /// <summary>For <c>procedural</c> layers, the kernel variable whose value drives the draw.</summary>
    [JsonPropertyName("sourceVar")] public string? SourceVar { get; set; }
}

/// <summary>
/// The editor's document: a canvas size and an ordered layer stack. The list is in paint
/// order — index 0 is the bottom layer, the last entry is the top — and the frame presents
/// it top-first in the layers panel.
/// </summary>
public sealed class LayerDocument
{
    [JsonPropertyName("width")] public int Width { get; set; } = 1024;
    [JsonPropertyName("height")] public int Height { get; set; } = 768;
    [JsonPropertyName("layers")] public List<Layer> Layers { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Serializes the document into a single metadata entry. Persisting one JSON string under
    /// one key sidesteps the <see cref="JsonElement"/>-vs-CLR ambiguity that would otherwise
    /// arise from round-tripping a nested object through the notebook's <c>layouts</c> block.
    /// </summary>
    public Dictionary<string, object> ToMetadata()
        => new() { ["document"] = JsonSerializer.Serialize(this, JsonOptions) };

    /// <summary>
    /// Restores a document from the metadata produced by <see cref="ToMetadata"/>, tolerating
    /// both a raw string and a deserialized <see cref="JsonElement"/> for the stored value.
    /// Returns a <see cref="Seed"/> document when nothing usable is present.
    /// </summary>
    public static LayerDocument FromMetadata(IDictionary<string, object>? metadata)
    {
        if (metadata is not null && metadata.TryGetValue("document", out var raw))
        {
            var json = raw switch
            {
                string s => s,
                JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
                JsonElement e => e.GetRawText(),
                _ => raw?.ToString(),
            };

            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    var document = JsonSerializer.Deserialize<LayerDocument>(json!, JsonOptions);
                    if (document is { Layers: not null })
                        return document;
                }
                catch (JsonException)
                {
                    // Corrupt or hand-edited metadata falls back to the seed below.
                }
            }
        }

        return Seed();
    }

    /// <summary>The layer at the top of the stack, or <c>null</c> when the document is empty.</summary>
    [JsonIgnore]
    public Layer? Top => Layers.Count > 0 ? Layers[^1] : null;

    /// <summary>
    /// The document as the frame should see it, with every layer name already resolved into
    /// the reader's language.
    /// </summary>
    /// <remarks>
    /// The frame draws names; the notebook stores keys. Sending the stored shape would make the
    /// panel show <c>Seed_Sky</c>, and storing the sent shape would freeze the notebook in the
    /// language whoever saved it last happened to be reading. Keeping the two apart is what lets
    /// one file open correctly for everybody.
    /// </remarks>
    public object ForDisplay() => new
    {
        width = Width,
        height = Height,
        layers = Layers.Select(l => new
        {
            id = l.Id,
            name = l.DisplayName,
            kind = l.Kind,
            visible = l.Visible,
            opacity = l.Opacity,
            blend = l.Blend,
            props = l.Props,
            sourceVar = l.SourceVar,
        }).ToArray(),
    };

    /// <summary>
    /// A pleasing starter stack so a fresh notebook opens with something on the canvas:
    /// a sunset gradient, a soft radial sun, a dot grid, and a title.
    /// </summary>
    public static LayerDocument Seed() => new()
    {
        Width = 1024,
        Height = 768,
        Layers =
        {
            new Layer
            {
                NameKey = "Seed_Sky",
                Kind = "linear-gradient",
                Props = new()
                {
                    ["angle"] = 90.0,
                    ["stops"] = new object[]
                    {
                        new Dictionary<string, object> { ["pos"] = 0.0, ["color"] = "#20123a" },
                        new Dictionary<string, object> { ["pos"] = 0.55, ["color"] = "#7b2d6b" },
                        new Dictionary<string, object> { ["pos"] = 1.0, ["color"] = "#f0803c" },
                    },
                },
            },
            new Layer
            {
                NameKey = "Seed_Sun",
                Kind = "radial-gradient",
                Blend = "screen",
                Props = new()
                {
                    ["cx"] = 0.72,
                    ["cy"] = 0.30,
                    ["radius"] = 0.26,
                    ["stops"] = new object[]
                    {
                        new Dictionary<string, object> { ["pos"] = 0.0, ["color"] = "#fff4c2" },
                        new Dictionary<string, object> { ["pos"] = 0.45, ["color"] = "#ffd166" },
                        new Dictionary<string, object> { ["pos"] = 1.0, ["color"] = "#ffd16600" },
                    },
                },
            },
            new Layer
            {
                NameKey = "Seed_DotGrid",
                Kind = "dots",
                Blend = "overlay",
                Opacity = 0.30,
                Props = new()
                {
                    ["size"] = 46.0,
                    ["radius"] = 2.4,
                    ["color"] = "#ffffff",
                },
            },
            new Layer
            {
                NameKey = "Seed_Scripted",
                Kind = "procedural",
                SourceVar = "ops",
            },
            new Layer
            {
                NameKey = "Seed_Title",
                Kind = "text",
                Opacity = 0.92,
                Props = new()
                {
                    ["text"] = "VERSO",
                    ["size"] = 0.20,
                    ["color"] = "#ffffff",
                    ["x"] = 0.5,
                    ["y"] = 0.78,
                    ["align"] = "center",
                    ["weight"] = "800",
                },
            },
        },
    };
}
