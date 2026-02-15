namespace Verso.Ado.Models;

public sealed record SqlResultSet(
    IReadOnlyList<SqlColumnMetadata> Columns,
    IReadOnlyList<object?[]> Rows,
    int TotalRowCount,
    bool WasTruncated);

public sealed record SqlColumnMetadata(
    string Name,
    string DataTypeName,
    Type ClrType,
    bool AllowsNull);
