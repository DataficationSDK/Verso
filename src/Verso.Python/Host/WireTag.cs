using System.Text.Json;
using System.Text.Json.Nodes;

namespace Verso.Python.Host;

/// <summary>
/// The envelope that carries a value JSON has no form of its own for.
///
/// A JSON object with exactly the two keys <c>__verso_type__</c> and <c>__verso_value__</c> is a
/// tagged value; anything else on the wire is ordinary JSON and is read as it appears. Requiring
/// both keys and no others is what keeps a dictionary that happens to contain one of these names
/// from being mistaken for a tag.
///
/// The double underscore follows the convention already applied to store names, where that prefix
/// marks what belongs to the interpreter or to Verso rather than to the notebook.
/// </summary>
internal static class WireTag
{
    public const string TypeKey = "__verso_type__";
    public const string ValueKey = "__verso_value__";

    public const string DateTime = "datetime";
    public const string DateTimeOffset = "datetimeoffset";
    public const string Date = "date";
    public const string Time = "time";
    public const string TimeSpan = "timespan";
    public const string Decimal = "decimal";
    public const string Guid = "guid";
    public const string BigInteger = "bigint";
    public const string Bytes = "bytes";

    /// <summary>Carries NaN and the infinities, which JSON cannot express as numbers.</summary>
    public const string Float = "float";

    /// <summary>
    /// A value that could not cross, carrying the reason. Used both for a whole variable and for a
    /// single element or property inside one that otherwise converted.
    /// </summary>
    public const string Unavailable = "unavailable";

    public static void Write(Utf8JsonWriter writer, string tag, string value)
    {
        writer.WriteStartObject();
        writer.WriteString(TypeKey, tag);
        writer.WriteString(ValueKey, value);
        writer.WriteEndObject();
    }

    public static void Write(Utf8JsonWriter writer, string tag, double value)
    {
        writer.WriteStartObject();
        writer.WriteString(TypeKey, tag);
        writer.WriteNumber(ValueKey, value);
        writer.WriteEndObject();
    }

    public static JsonObject Node(string tag, string value)
        => new() { [TypeKey] = tag, [ValueKey] = value };

    /// <summary>
    /// Read a tagged value. Returns false for every other node, including an object that carries
    /// one of the two keys among others, which is an ordinary dictionary rather than a tag.
    /// </summary>
    public static bool TryRead(JsonNode? node, out string tag, out JsonNode? value)
    {
        tag = "";
        value = null;

        if (node is not JsonObject obj || obj.Count != 2)
            return false;

        if (!obj.TryGetPropertyValue(TypeKey, out var tagNode)
            || !obj.TryGetPropertyValue(ValueKey, out var valueNode))
        {
            return false;
        }

        if (tagNode is not JsonValue candidate || !candidate.TryGetValue<string>(out var text))
            return false;

        tag = text;
        value = valueNode;
        return true;
    }
}
