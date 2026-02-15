namespace Verso.Ado.Models;

internal sealed record SqlResultSet(
    IReadOnlyList<SqlColumnMetadata> Columns,
    IReadOnlyList<object?[]> Rows,
    int TotalRowCount,
    bool WasTruncated);

internal sealed record SqlColumnMetadata(
    string Name,
    string DataTypeName,
    Type ClrType,
    bool AllowsNull);
