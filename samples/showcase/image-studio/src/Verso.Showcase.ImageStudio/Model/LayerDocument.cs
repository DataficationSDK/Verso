using System.Text.Json;
using System.Text.Json.Serialization;

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
    [JsonPropertyName("name")] public string Name { get; set; } = "Layer";
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
    public Layer? Top => Layers.Count > 0 ? Layers[^1] : null;

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
                Name = "Sky",
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
                Name = "Sun",
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
                Name = "Dot grid",
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
                Name = "Scripted",
                Kind = "procedural",
                SourceVar = "ops",
            },
            new Layer
            {
                Name = "Title",
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
