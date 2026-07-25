using System.Collections;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Verso.Python.Host;

/// <summary>
/// Converts between the shared variable store and the JSON the host protocol carries.
///
/// Only values JSON can describe make the trip. The in-process kernel could hand Python a live
/// .NET object when conversion failed, which a separate process cannot do, so anything outside
/// the JSON shapes is left behind rather than reduced into something of a different type.
/// </summary>
internal static class HostVariables
{
    /// <summary>
    /// Guards against self-referential or pathologically nested values. Matches the limit the
    /// host script applies in the other direction.
    /// </summary>
    private const int MaxDepth = 100;

    /// <summary>
    /// Convert a store value to JSON. Returns false when the value has no JSON form, which is
    /// the signal to skip the variable rather than to fail the cell.
    /// </summary>
    public static bool TryToJson(object? value, out JsonNode? node)
    {
        node = null;
        if (value is null)
            return true;

        if (IsSkipped(value))
            return false;

        return TryConvert(value, 0, new HashSet<object>(ReferenceEqualityComparer.Instance), out node);
    }

    /// <summary>
    /// Convert a published value back to the plain CLR shapes the store and other kernels
    /// expect: <c>bool</c>, <c>long</c>, <c>double</c>, <c>string</c>, <c>List&lt;object&gt;</c>,
    /// and <c>Dictionary&lt;string, object&gt;</c>.
    /// </summary>
    public static object? FromJson(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;

            case JsonArray array:
            {
                var items = new List<object?>(array.Count);
                foreach (var item in array)
                    items.Add(FromJson(item));
                return items;
            }

            case JsonObject obj:
            {
                var map = new Dictionary<string, object?>(obj.Count, StringComparer.Ordinal);
                foreach (var pair in obj)
                    map[pair.Key] = FromJson(pair.Value);
                return map;
            }

            case JsonValue value:
            {
                if (value.TryGetValue<bool>(out var flag)) return flag;
                if (value.TryGetValue<long>(out var integer)) return integer;
                if (value.TryGetValue<double>(out var number)) return number;
                if (value.TryGetValue<string>(out var text)) return text;
                return value.ToString();
            }

            default:
                return node.ToString();
        }
    }

    /// <summary>
    /// A stable fingerprint of a serialized value, used to avoid re-sending variables that have
    /// not changed. This replaces the reference-identity check the in-process kernel used, which
    /// says nothing useful once a value has been serialized.
    /// </summary>
    public static string ComputeHash(JsonNode? node)
    {
        var text = node?.ToJsonString() ?? "null";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToBase64String(digest);
    }

    private static bool IsSkipped(object value)
        => value is Delegate or CancellationToken or Task or IAsyncDisposable;

    private static bool TryConvert(object? value, int depth, HashSet<object> visited, out JsonNode? node)
    {
        node = null;

        if (value is null)
            return true;

        if (depth >= MaxDepth)
            return false;

        switch (value)
        {
            case bool flag:
                node = JsonValue.Create(flag);
                return true;

            case string text:
                node = JsonValue.Create(text);
                return true;

            case char character:
                node = JsonValue.Create(character.ToString());
                return true;

            case sbyte or byte or short or ushort or int or uint or long:
                node = JsonValue.Create(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                return true;

            case ulong unsigned:
                // Values past long.MaxValue would wrap, so they travel as text rather than wrong.
                if (unsigned <= long.MaxValue)
                {
                    node = JsonValue.Create((long)unsigned);
                    return true;
                }
                node = JsonValue.Create(unsigned.ToString(CultureInfo.InvariantCulture));
                return true;

            case float or double:
            {
                var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                // JSON has no NaN or infinity, and the host script refuses the bare tokens.
                if (double.IsNaN(number) || double.IsInfinity(number))
                    return false;
                node = JsonValue.Create(number);
                return true;
            }

            case decimal number:
                node = JsonValue.Create((double)number);
                return true;
        }

        if (IsSkipped(value))
            return false;

        // A container that already appears above this point would recurse forever.
        if (!visited.Add(value))
            return false;

        try
        {
            if (value is IDictionary dictionary)
                return TryConvertDictionary(dictionary, depth, visited, out node);

            if (value is IEnumerable sequence)
                return TryConvertSequence(sequence, depth, visited, out node);
        }
        finally
        {
            visited.Remove(value);
        }

        return false;
    }

    private static bool TryConvertDictionary(
        IDictionary dictionary, int depth, HashSet<object> visited, out JsonNode? node)
    {
        node = null;
        var result = new JsonObject();

        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not string key)
                return false;
            if (!TryConvert(entry.Value, depth + 1, visited, out var child))
                return false;
            result[key] = child;
        }

        node = result;
        return true;
    }

    private static bool TryConvertSequence(
        IEnumerable sequence, int depth, HashSet<object> visited, out JsonNode? node)
    {
        node = null;
        var result = new JsonArray();

        foreach (var item in sequence)
        {
            if (!TryConvert(item, depth + 1, visited, out var child))
                return false;
            result.Add(child);
        }

        node = result;
        return true;
    }
}
