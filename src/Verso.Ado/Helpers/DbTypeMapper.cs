using System.Data;

namespace Verso.Ado.Helpers;

/// <summary>
/// Maps CLR types to <see cref="DbType"/> for parameter binding.
/// </summary>
internal static class DbTypeMapper
{
    private static readonly Dictionary<Type, DbType> Map = new()
    {
        [typeof(string)] = DbType.String,
        [typeof(int)] = DbType.Int32,
        [typeof(long)] = DbType.Int64,
        [typeof(short)] = DbType.Int16,
        [typeof(byte)] = DbType.Byte,
        [typeof(bool)] = DbType.Boolean,
        [typeof(decimal)] = DbType.Decimal,
        [typeof(double)] = DbType.Double,
        [typeof(float)] = DbType.Single,
        [typeof(DateTime)] = DbType.DateTime,
        [typeof(Guid)] = DbType.Guid,
    };

    /// <summary>
    /// Attempts to map a CLR type to its corresponding <see cref="DbType"/>.
    /// Returns <c>false</c> for unsupported types.
    /// </summary>
    internal static bool TryMapDbType(Type clrType, out DbType dbType)
    {
        return Map.TryGetValue(clrType, out dbType);
    }
}
