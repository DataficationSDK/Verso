namespace Verso.Ado.Helpers;

/// <summary>
/// Maps SQL data type strings to CLR <see cref="Type"/> and generates C# type name strings.
/// Reverse of <see cref="DbTypeMapper"/> (which maps CLR -> DbType).
/// </summary>
internal static class SqlClrTypeMapper
{
    private static readonly Dictionary<string, Type> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // Integer types
        ["int"] = typeof(int),
        ["integer"] = typeof(int),
        ["int4"] = typeof(int),
        ["mediumint"] = typeof(int),
        ["bigint"] = typeof(long),
        ["int8"] = typeof(long),
        ["smallint"] = typeof(short),
        ["int2"] = typeof(short),
        ["tinyint"] = typeof(byte),

        // Boolean
        ["bit"] = typeof(bool),
        ["boolean"] = typeof(bool),
        ["bool"] = typeof(bool),

        // Decimal / numeric
        ["decimal"] = typeof(decimal),
        ["numeric"] = typeof(decimal),
        ["money"] = typeof(decimal),
        ["smallmoney"] = typeof(decimal),

        // Floating point
        ["float"] = typeof(double),
        ["double"] = typeof(double),
        ["double precision"] = typeof(double),
        ["real"] = typeof(float),

        // String types
        ["varchar"] = typeof(string),
        ["nvarchar"] = typeof(string),
        ["char"] = typeof(string),
        ["nchar"] = typeof(string),
        ["text"] = typeof(string),
        ["ntext"] = typeof(string),
        ["clob"] = typeof(string),
        ["character varying"] = typeof(string),
        ["character"] = typeof(string),
        ["xml"] = typeof(string),
        ["json"] = typeof(string),
        ["jsonb"] = typeof(string),

        // Date / time
        ["datetime"] = typeof(DateTime),
        ["datetime2"] = typeof(DateTime),
        ["date"] = typeof(DateTime),
        ["smalldatetime"] = typeof(DateTime),
        ["timestamp"] = typeof(DateTime),
        ["timestamp without time zone"] = typeof(DateTime),
        ["timestamp with time zone"] = typeof(DateTimeOffset),
        ["datetimeoffset"] = typeof(DateTimeOffset),
        ["time"] = typeof(TimeSpan),

        // GUID
        ["uniqueidentifier"] = typeof(Guid),
        ["uuid"] = typeof(Guid),

        // Binary
        ["varbinary"] = typeof(byte[]),
        ["binary"] = typeof(byte[]),
        ["image"] = typeof(byte[]),
        ["blob"] = typeof(byte[]),
        ["bytea"] = typeof(byte[]),

        // SQLite affinity types (only those that don't conflict with standard SQL entries above)
        // TEXT, BLOB, NUMERIC already match standard entries case-insensitively.
        // INTEGER and REAL are handled as standard SQL types (int and float respectively).
    };

    /// <summary>
    /// Maps a SQL data type string to its corresponding CLR <see cref="Type"/>.
    /// Strips length/precision suffixes (e.g. "varchar(50)" -> "varchar").
    /// Falls back to <see cref="object"/> for unknown types.
    /// </summary>
    internal static Type MapSqlType(string sqlType)
    {
        if (string.IsNullOrWhiteSpace(sqlType))
            return typeof(object);

        // Strip parenthesized length/precision: "varchar(50)" -> "varchar"
        var normalized = sqlType.Trim();
        int parenIdx = normalized.IndexOf('(');
        if (parenIdx > 0)
            normalized = normalized.Substring(0, parenIdx).TrimEnd();

        if (Map.TryGetValue(normalized, out var clrType))
            return clrType;

        return typeof(object);
    }

    /// <summary>
    /// Returns a C# type name suitable for code generation.
    /// Handles nullable value types ("int?"), nullable reference types ("string?"),
    /// and special types ("byte[]").
    /// </summary>
    internal static string GetCSharpTypeName(Type clrType, bool isNullable)
    {
        if (clrType == typeof(byte[]))
            return isNullable ? "byte[]?" : "byte[]";

        string name = clrType switch
        {
            _ when clrType == typeof(int) => "int",
            _ when clrType == typeof(long) => "long",
            _ when clrType == typeof(short) => "short",
            _ when clrType == typeof(byte) => "byte",
            _ when clrType == typeof(bool) => "bool",
            _ when clrType == typeof(decimal) => "decimal",
            _ when clrType == typeof(double) => "double",
            _ when clrType == typeof(float) => "float",
            _ when clrType == typeof(string) => "string",
            _ when clrType == typeof(DateTime) => "DateTime",
            _ when clrType == typeof(DateTimeOffset) => "DateTimeOffset",
            _ when clrType == typeof(TimeSpan) => "TimeSpan",
            _ when clrType == typeof(Guid) => "Guid",
            _ when clrType == typeof(object) => "object",
            _ => clrType.Name,
        };

        if (isNullable)
            return name + "?";

        return name;
    }
}
