using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Verso.Showcase.GridStudio;

/// <summary>
/// A column-and-row projection of a <c>DataBlock</c> in a shape that serializes cleanly to the
/// frame and rebuilds back into a <c>DataBlock</c>. Property names are PascalCase; the host
/// applies a camelCase policy when it serializes the message, so the frame reads
/// <c>columns</c> / <c>types</c> / <c>rows</c>.
/// </summary>
internal sealed class GridData
{
    /// <summary>Column names, in schema order.</summary>
    public List<string> Columns { get; set; } = new();

    /// <summary>Per-column editor hint: <c>numeric</c>, <c>text</c>, <c>checkbox</c>, or <c>calendar</c>.</summary>
    public List<string> Types { get; set; } = new();

    /// <summary>Row-major cell values. Numbers as <see cref="double"/>, dates as strings, etc.</summary>
    public List<object?[]> Rows { get; set; } = new();
}

/// <summary>
/// Reads and rebuilds a Datafication.Core <c>DataBlock</c> entirely by reflection, so the
/// extension takes no compile-time dependency on Datafication.Core and never fights assembly
/// identity: write-back uses the live instance's own assembly, which is exactly the one the
/// kernel loaded. The only knowledge encoded here is the small, stable public shape of
/// <c>DataBlock</c> / <c>DataColumn</c> / <c>DataSchema</c>.
/// </summary>
internal static class DataBlockInterop
{
    private const string DataBlockTypeName = "Datafication.Core.Data.DataBlock";
    private const string DataColumnTypeName = "Datafication.Core.Data.DataColumn";

    /// <summary>True when the value looks like a DataBlock (has the members we rely on).</summary>
    public static bool IsDataBlock(object? value)
        => value is not null && value.GetType().FullName == DataBlockTypeName;

    /// <summary>
    /// Projects a DataBlock instance into a <see cref="GridData"/>. Returns <c>null</c> if the
    /// value is not a DataBlock or its shape is not what we expect.
    /// </summary>
    public static GridData? Read(object dataBlock)
    {
        try
        {
            var dbType = dataBlock.GetType();
            var schema = dbType.GetProperty("Schema")?.GetValue(dataBlock);
            if (schema is null)
                return null;

            if (schema.GetType().GetMethod("GetColumnNames")?.Invoke(schema, null) is not IEnumerable rawNames)
                return null;

            var names = rawNames.Cast<object?>().Select(n => n?.ToString() ?? "").ToList();
            var rowCount = dbType.GetProperty("RowCount")?.GetValue(dataBlock) is int rc ? rc : 0;

            var getColumn = dbType.GetMethod("GetColumn", new[] { typeof(string) });
            var data = new GridData();

            // Per-column value lists, captured once, plus the editor hint from the declared type.
            var columnValues = new List<IList?>(names.Count);
            foreach (var name in names)
            {
                var column = getColumn?.Invoke(dataBlock, new object?[] { name });
                var clr = ClrTypeOf(column);
                data.Columns.Add(name);
                data.Types.Add(HintFor(clr));
                columnValues.Add(column?.GetType().GetProperty("Values")?.GetValue(column) as IList);
            }

            for (var r = 0; r < rowCount; r++)
            {
                var row = new object?[names.Count];
                for (var c = 0; c < names.Count; c++)
                {
                    var values = columnValues[c];
                    row[c] = values is not null && r < values.Count ? MapOut(values[r]) : null;
                }
                data.Rows.Add(row);
            }

            return data;
        }
        catch (Exception ex) when (ex is TargetInvocationException or InvalidCastException or AmbiguousMatchException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds a new DataBlock from edited grid data using <paramref name="coreAssembly"/> — the
    /// assembly the kernel's own DataBlock came from. Returns <c>null</c> if the types cannot be
    /// located or constructed.
    /// </summary>
    public static object? Build(Assembly coreAssembly, IReadOnlyList<string> columns, IReadOnlyList<string> types, IReadOnlyList<JsonElement> rows)
    {
        var dbType = coreAssembly.GetType(DataBlockTypeName);
        var columnType = coreAssembly.GetType(DataColumnTypeName);
        if (dbType is null || columnType is null)
            return null;

        var columnCtor = columnType.GetConstructors()
            .FirstOrDefault(c =>
            {
                var p = c.GetParameters();
                return p.Length >= 2 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(Type);
            });
        var addColumn = dbType.GetMethod("AddColumn");
        var addRow = dbType.GetMethod("AddRow", new[] { typeof(object[]) });
        if (columnCtor is null || addColumn is null || addRow is null)
            return null;

        var dataBlock = Activator.CreateInstance(dbType);
        if (dataBlock is null)
            return null;

        var clrTypes = new Type[columns.Count];
        for (var c = 0; c < columns.Count; c++)
        {
            var clr = ClrFor(c < types.Count ? types[c] : "text");
            clrTypes[c] = clr;
            addColumn.Invoke(dataBlock, new[] { BuildColumn(columnCtor, columns[c], clr) });
        }

        foreach (var rowElement in rows)
        {
            if (rowElement.ValueKind != JsonValueKind.Array)
                continue;

            var cells = rowElement.EnumerateArray().ToArray();
            var values = new object?[columns.Count];
            for (var c = 0; c < columns.Count; c++)
                values[c] = c < cells.Length ? Coerce(cells[c], clrTypes[c]) : EmptyCell(clrTypes[c]);

            addRow.Invoke(dataBlock, new object[] { values });
        }

        return dataBlock;
    }

    private static object BuildColumn(ConstructorInfo ctor, string name, Type clr)
    {
        // Fill the required (string name, Type dataType) head; let everything else fall back to
        // its declared default (nullable column, no unique/index constraint, etc.).
        var parameters = ctor.GetParameters();
        var args = new object?[parameters.Length];
        args[0] = name;
        args[1] = clr;
        for (var i = 2; i < parameters.Length; i++)
        {
            var p = parameters[i];
            args[i] = p.HasDefaultValue ? p.DefaultValue
                : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType)
                : null;
        }
        return ctor.Invoke(args)!;
    }

    // --- Type mapping -------------------------------------------------------

    private static Type? ClrTypeOf(object? column)
    {
        var dataType = column?.GetType().GetProperty("DataType")?.GetValue(column);
        if (dataType is null)
            return null;
        // SerializableType exposes GetClrType(); fall back to an implicit Type conversion.
        if (dataType.GetType().GetMethod("GetClrType", Type.EmptyTypes)?.Invoke(dataType, null) is Type clr)
            return clr;
        return dataType as Type;
    }

    private static string HintFor(Type? clr)
    {
        if (clr == typeof(bool)) return "checkbox";
        if (clr == typeof(DateTime)) return "calendar";
        if (clr is not null && IsNumeric(clr)) return "numeric";
        return "text";
    }

    private static Type ClrFor(string hint) => hint switch
    {
        "numeric" => typeof(double),
        "checkbox" => typeof(bool),
        "calendar" => typeof(DateTime),
        _ => typeof(string),
    };

    private static bool IsNumeric(Type t) => Type.GetTypeCode(t) is
        TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or
        TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or
        TypeCode.Single or TypeCode.Double or TypeCode.Decimal;

    // Normalize a stored cell for the wire: numbers to double, dates to a calendar-friendly
    // string, everything else to a string. Keeps the frame's value handling simple.
    private static object? MapOut(object? raw) => raw switch
    {
        null => null,
        bool b => b,
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
            => Convert.ToDouble(raw, CultureInfo.InvariantCulture),
        _ => raw.ToString(),
    };

    // Coerce an incoming JSON cell to the target column type. Unparseable numeric/date cells
    // become null (an empty cell); text is always representable.
    private static object? Coerce(JsonElement cell, Type clr)
    {
        if (cell.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return EmptyCell(clr);

        if (clr == typeof(double))
        {
            if (cell.ValueKind == JsonValueKind.Number && cell.TryGetDouble(out var n))
                return n;
            if (cell.ValueKind == JsonValueKind.String
                && double.TryParse(cell.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return null;
        }

        if (clr == typeof(bool))
        {
            return cell.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => cell.GetDouble() != 0,
                JsonValueKind.String => IsTruthy(cell.GetString()),
                _ => false,
            };
        }

        if (clr == typeof(DateTime))
        {
            var text = cell.ValueKind == JsonValueKind.String ? cell.GetString() : cell.GetRawText();
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt
                : (object?)null;
        }

        return cell.ValueKind == JsonValueKind.String ? cell.GetString() : cell.GetRawText();
    }

    private static bool IsTruthy(string? s)
        => s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "on", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase);

    // Empty text cells should round-trip as "" rather than null; other empty types as null
    // (the rebuilt columns are nullable, so a blank numeric/date cell stays blank).
    private static object? EmptyCell(Type clr) => clr == typeof(string) ? "" : null;
}
