using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Verso.Python.Host;

/// <summary>
/// Converts between the shared variable store and the JSON the host protocol carries.
///
/// <para>
/// A separate process cannot be handed a live object, so a value crosses as data or not at all.
/// What crosses is decided by <see cref="System.Text.Json"/> rather than by a list kept here, so
/// records, plain objects, dictionaries, and the collections of them that notebooks actually build
/// all convert without needing to be named. Converters supply the forms JSON lacks, and a value
/// that still cannot cross says why instead of disappearing.
/// </para>
/// </summary>
internal static class HostVariables
{
    /// <summary>
    /// Guards against self-referential or pathologically nested values. Matches the limit the
    /// host script applies in the other direction.
    /// </summary>
    private const int MaxDepth = 100;

    /// <summary>
    /// How many elements are taken from a sequence that does not know its own length. A generated
    /// sequence can be endless, and enumerating one while preparing a cell would hang it with no
    /// way for the author to tell why.
    /// </summary>
    private const int MaxLazyElements = 100_000;

    /// <summary>How far the salvage walk descends while isolating a failure.</summary>
    private const int MaxSalvageDepth = 4;

    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            MaxDepth = MaxDepth,

            // A back reference becomes null rather than an error, so a value that merely contains
            // a cycle still crosses with everything else about it intact.
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
        };

        options.Converters.Add(new VariableConverters.DateTimeConverter());
        options.Converters.Add(new VariableConverters.DateTimeOffsetConverter());
        options.Converters.Add(new VariableConverters.DateOnlyConverter());
        options.Converters.Add(new VariableConverters.TimeOnlyConverter());
        options.Converters.Add(new VariableConverters.TimeSpanConverter());
        options.Converters.Add(new VariableConverters.DecimalConverter());
        options.Converters.Add(new VariableConverters.BigIntegerConverter());
        options.Converters.Add(new VariableConverters.GuidConverter());
        options.Converters.Add(new VariableConverters.ByteArrayConverter());
        options.Converters.Add(new VariableConverters.DoubleConverter());
        options.Converters.Add(new VariableConverters.SingleConverter());
        options.Converters.Add(new VariableConverters.DataTableConverter());

        // An enum crosses as its member name. The number behind it means nothing on the far side.
        options.Converters.Add(new JsonStringEnumConverter());

        // Last, so a type any converter above claims is handled there first.
        options.Converters.Add(new VariableConverters.UnavailableConverterFactory());

        return options;
    }

    /// <summary>
    /// Convert a store value to JSON. Returns false when the value as a whole has no JSON form,
    /// which is the signal to explain its absence rather than to fail the cell.
    /// </summary>
    public static bool TryToJson(object? value, out JsonNode? node)
        => TryToJson(value, out node, out _);

    /// <summary>
    /// Convert a store value to JSON, reporting why when the value as a whole cannot cross.
    /// </summary>
    public static bool TryToJson(object? value, out JsonNode? node, out string? reason)
    {
        node = null;
        reason = null;

        if (value is null)
            return true;

        // Asked before serializing, because at the top level the answer is that the variable
        // itself cannot cross. The same test inside a value yields a tag in place of one member.
        if (VariableShapes.RefusalReason(value.GetType()) is { } refusal)
        {
            reason = refusal;
            return false;
        }

        var subject = Materialize(value);

        try
        {
            node = JsonSerializer.SerializeToNode(subject, subject.GetType(), Options);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Anything at all, because a property getter runs arbitrary notebook code and can
            // raise anything it likes. Rather than lose the whole value for it, walk one level
            // and convert the parts independently, so only the part that failed is replaced.
            node = Salvage(subject, 0);
            if (node is not null)
                return true;

            reason = Describe(ex, subject);
            return false;
        }
    }

    /// <summary>
    /// Reads a lazily generated sequence into a bounded list. A sequence that knows its own length
    /// is already finite and is left alone, so this costs nothing for the ordinary cases and, more
    /// importantly, leaves their shape intact: reading a map into a list would turn it into pairs.
    /// </summary>
    private static object Materialize(object value)
    {
        if (value is string || HasKnownCount(value.GetType()))
            return value;

        if (value is not IEnumerable sequence)
            return value;

        var items = new List<object?>();
        foreach (var item in sequence)
        {
            items.Add(item);
            if (items.Count >= MaxLazyElements)
                break;
        }

        return items;
    }

    private static readonly ConcurrentDictionary<Type, bool> CountedTypes = new();

    /// <summary>
    /// Whether a type reports its own size, which is what tells a finite collection apart from a
    /// sequence that may generate forever. The generic interfaces have to be included: an F# map
    /// implements only those, and treating it as uncounted would flatten it.
    /// </summary>
    private static bool HasKnownCount(Type type) => CountedTypes.GetOrAdd(type, static t =>
    {
        if (t.IsArray || typeof(ICollection).IsAssignableFrom(t) || typeof(IDictionary).IsAssignableFrom(t))
            return true;

        foreach (var contract in t.GetInterfaces())
        {
            if (!contract.IsGenericType)
                continue;

            var definition = contract.GetGenericTypeDefinition();
            if (definition == typeof(ICollection<>)
                || definition == typeof(IReadOnlyCollection<>)
                || definition == typeof(IDictionary<,>)
                || definition == typeof(IReadOnlyDictionary<,>))
            {
                return true;
            }
        }

        return false;
    });

    /// <summary>
    /// Convert the parts of a value one at a time, replacing only those that refuse. Runs after the
    /// whole-value attempt has already failed, so it trades speed for keeping as much as it can.
    /// </summary>
    private static JsonNode? Salvage(object? value, int depth)
    {
        if (value is null)
            return null;

        if (depth >= MaxSalvageDepth)
            return WireTag.Node(WireTag.Unavailable, "nested too deeply to convert");

        switch (Materialize(value))
        {
            case IDictionary dictionary:
            {
                var result = new JsonObject();
                foreach (DictionaryEntry entry in dictionary)
                {
                    var key = entry.Key as string ?? entry.Key.ToString();
                    if (key is not null)
                        result[key] = ConvertPart(entry.Value, depth);
                }

                return result;
            }

            case string:
                return null;

            case IEnumerable sequence:
            {
                var result = new JsonArray();
                foreach (var item in sequence)
                    result.Add(ConvertPart(item, depth));

                return result;
            }
        }

        var properties = value.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToArray();

        if (properties.Length == 0)
            return null;

        var members = new JsonObject();
        foreach (var property in properties)
        {
            object? member;
            try
            {
                member = property.GetValue(value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A property that throws when read is one member's problem, not the object's.
                members[property.Name] = WireTag.Node(
                    WireTag.Unavailable, $"reading it raised {(ex.InnerException ?? ex).GetType().Name}");
                continue;
            }

            members[property.Name] = ConvertPart(member, depth);
        }

        return members;
    }

    private static JsonNode? ConvertPart(object? part, int depth)
    {
        if (part is null)
            return null;

        if (VariableShapes.RefusalReason(part.GetType()) is { } refusal)
            return WireTag.Node(WireTag.Unavailable, refusal);

        try
        {
            return JsonSerializer.SerializeToNode(part, part.GetType(), Options);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Salvage(part, depth + 1) ?? WireTag.Node(WireTag.Unavailable, Describe(ex, part));
        }
    }

    private static string Describe(Exception ex, object value)
        => ex is NotSupportedException
            ? $"a {value.GetType().Name}, which has no JSON form"
            : $"converting it raised {ex.GetType().Name}";

    /// <summary>
    /// Convert a published value back to the plain CLR shapes the store and other kernels
    /// expect: <c>bool</c>, <c>long</c>, <c>double</c>, <c>string</c>, <c>List&lt;object&gt;</c>,
    /// and <c>Dictionary&lt;string, object&gt;</c>, plus the native types a tag names.
    /// </summary>
    public static object? FromJson(JsonNode? node)
    {
        if (WireTag.TryRead(node, out var tag, out var tagged))
            return FromTag(tag, tagged);

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
    /// Rebuild the .NET value a tag names. Anything that will not parse comes back as the text it
    /// arrived as: the caller iterates a whole cell's variables with no handler of its own, so one
    /// malformed value must not cost every value after it.
    /// </summary>
    private static object? FromTag(string tag, JsonNode? tagged)
    {
        var text = tagged?.GetValueKind() == JsonValueKind.String
            ? tagged.GetValue<string>()
            : tagged?.ToJsonString();

        try
        {
            switch (tag)
            {
                case WireTag.Unavailable:
                    // Nothing crossed, so there is nothing to store.
                    return null;

                case WireTag.DateTime when text is not null:
                    return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

                case WireTag.DateTimeOffset when text is not null:
                    return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

                case WireTag.Date when text is not null:
                    return DateOnly.Parse(text, CultureInfo.InvariantCulture);

                case WireTag.Time when text is not null:
                    return TimeOnly.Parse(text, CultureInfo.InvariantCulture);

                case WireTag.TimeSpan when tagged is not null:
                    return System.TimeSpan.FromSeconds(tagged.GetValue<double>());

                case WireTag.Decimal when text is not null:
                    return decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

                case WireTag.Guid when text is not null:
                    return System.Guid.Parse(text);

                case WireTag.BigInteger when text is not null:
                {
                    var big = BigInteger.Parse(text, CultureInfo.InvariantCulture);
                    return big >= long.MinValue && big <= long.MaxValue ? (long)big : (object)big;
                }

                case WireTag.Bytes when text is not null:
                    return Convert.FromBase64String(text);

                case WireTag.Float when text is not null:
                    return text switch
                    {
                        "NaN" => double.NaN,
                        "Infinity" => double.PositiveInfinity,
                        "-Infinity" => double.NegativeInfinity,
                        _ => (object)text,
                    };
            }
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidOperationException)
        {
            return text;
        }

        return text;
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
}
