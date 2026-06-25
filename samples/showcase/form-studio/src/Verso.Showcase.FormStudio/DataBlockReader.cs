using System.Collections;
using System.Globalization;
using System.Reflection;

namespace Verso.Showcase.FormStudio;

/// <summary>
/// A column-and-row projection of a <c>DataBlock</c> in a shape that serializes cleanly to the
/// frame for charting. Property names are PascalCase; the host applies a camelCase policy when it
/// serializes the message, so the frame reads <c>columns</c> / <c>types</c> / <c>rows</c>.
/// </summary>
internal sealed class BlockData
{
    /// <summary>Column names, in schema order.</summary>
    public List<string> Columns { get; set; } = new();

    /// <summary>Per-column hint: <c>numeric</c>, <c>text</c>, <c>checkbox</c>, or <c>calendar</c>.</summary>
    public List<string> Types { get; set; } = new();

    /// <summary>Row-major cell values. Numbers as <see cref="double"/>, dates as strings, etc.</summary>
    public List<object?[]> Rows { get; set; } = new();
}

/// <summary>
/// Reads a Datafication.Core <c>DataBlock</c> into a serializable shape entirely by reflection, so
/// the extension takes no compile-time dependency on Datafication.Core and never fights assembly
/// identity. Form Studio only ever reads DataBlocks (charts consume them); it writes scalar kernel
/// variables, not DataBlocks, so there is no rebuild counterpart here. The only knowledge encoded
/// is the small, stable public shape of <c>DataBlock</c> / <c>DataColumn</c> / <c>DataSchema</c>.
/// </summary>
internal static class DataBlockReader
{
    private const string DataBlockTypeName = "Datafication.Core.Data.DataBlock";

    /// <summary>True when the value looks like a DataBlock.</summary>
    public static bool IsDataBlock(object? value)
        => value is not null && value.GetType().FullName == DataBlockTypeName;

    /// <summary>
    /// Projects a DataBlock instance into a <see cref="BlockData"/>. Returns <c>null</c> if the
    /// value is not a DataBlock or its shape is not what we expect.
    /// </summary>
    public static BlockData? Read(object dataBlock)
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
            var data = new BlockData();

            // Per-column value lists, captured once, plus the type hint from the declared type.
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

    private static bool IsNumeric(Type t) => Type.GetTypeCode(t) is
        TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or
        TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or
        TypeCode.Single or TypeCode.Double or TypeCode.Decimal;

    // Normalize a stored cell for the wire: numbers to double, dates to a friendly string,
    // everything else to a string. Keeps the frame's value handling simple.
    private static object? MapOut(object? raw) => raw switch
    {
        null => null,
        bool b => b,
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
            => Convert.ToDouble(raw, CultureInfo.InvariantCulture),
        _ => raw.ToString(),
    };
}
