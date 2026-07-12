using System.Text.Json;
using Verso.Abstractions;

namespace Verso.Showcase.SlideStudio.Models;

/// <summary>
/// The three per-cell presenter switches Slide Studio persists in cell metadata:
/// whether the cell appears in presenter view at all, and whether its source and
/// output are shown on the slide. Stored under a single namespaced metadata key so
/// the flags travel with the cell through save, reorder, and copy, and so the
/// properties panel and the layout's own checkboxes edit the same state.
/// </summary>
public readonly record struct PresenterFlags(bool Include, bool ShowSource, bool ShowOutput)
{
    /// <summary>The cell metadata key the flags are stored under.</summary>
    public const string MetadataKey = "verso.showcase.slideStudio:presenter";

    /// <summary>
    /// The defaults: every cell is part of the deck, slides show output but not source.
    /// Cells whose flags match the defaults carry no metadata entry at all.
    /// </summary>
    public static PresenterFlags Default => new(Include: true, ShowSource: false, ShowOutput: true);

    /// <summary>Whether the cell contributes a slide (included with something to show).</summary>
    public bool ShowsAnything => Include && (ShowSource || ShowOutput);

    /// <summary>
    /// Reads the flags from a cell, tolerating both live CLR values and the
    /// <see cref="JsonElement"/> values metadata holds after a load from disk.
    /// </summary>
    public static PresenterFlags Read(CellModel cell)
    {
        if (!cell.Metadata.TryGetValue(MetadataKey, out var value) || value is null)
            return Default;

        var flags = Default;
        switch (value)
        {
            case IDictionary<string, object> dict:
                flags = new PresenterFlags(
                    ReadBool(dict.TryGetValue("include", out var i) ? i : null, flags.Include),
                    ReadBool(dict.TryGetValue("showSource", out var s) ? s : null, flags.ShowSource),
                    ReadBool(dict.TryGetValue("showOutput", out var o) ? o : null, flags.ShowOutput));
                break;

            case JsonElement { ValueKind: JsonValueKind.Object } element:
                flags = new PresenterFlags(
                    element.TryGetProperty("include", out var ie) ? ReadBool(ie, flags.Include) : flags.Include,
                    element.TryGetProperty("showSource", out var se) ? ReadBool(se, flags.ShowSource) : flags.ShowSource,
                    element.TryGetProperty("showOutput", out var oe) ? ReadBool(oe, flags.ShowOutput) : flags.ShowOutput);
                break;
        }

        return flags;
    }

    /// <summary>
    /// Writes the flags to a cell. Defaults are stored as an absent key so untouched
    /// cells stay clean in the .verso file.
    /// </summary>
    public void Write(CellModel cell)
    {
        if (this == Default)
        {
            cell.Metadata.Remove(MetadataKey);
            return;
        }

        cell.Metadata[MetadataKey] = new Dictionary<string, object>
        {
            ["include"] = Include,
            ["showSource"] = ShowSource,
            ["showOutput"] = ShowOutput,
        };
    }

    private static bool ReadBool(object? value, bool fallback) => value switch
    {
        bool b => b,
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.False } => false,
        string s when bool.TryParse(s, out var parsed) => parsed,
        _ => fallback,
    };
}
