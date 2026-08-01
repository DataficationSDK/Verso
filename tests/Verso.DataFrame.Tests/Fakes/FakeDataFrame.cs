using System.Collections;

namespace Microsoft.Data.Analysis;

internal sealed class DataFrame
{
    public DataFrame(
        IEnumerable<DataFrameColumn>? columns = null,
        IEnumerable<DataFrameRow>? rows = null)
    {
        Columns = new DataFrameColumnCollection(columns ?? Array.Empty<DataFrameColumn>());
        Rows = new DataFrameRowCollection(rows ?? Array.Empty<DataFrameRow>());
    }

    public DataFrameColumnCollection Columns { get; }
    public DataFrameRowCollection Rows { get; }
}

internal sealed class DataFrameColumn
{
    public DataFrameColumn(string name, Type dataType)
    {
        Name = name;
        DataType = dataType;
    }

    public string Name { get; }
    public Type DataType { get; }
}

internal sealed class DataFrameColumnCollection : IEnumerable<DataFrameColumn>
{
    private readonly IReadOnlyList<DataFrameColumn> _columns;

    public DataFrameColumnCollection(IEnumerable<DataFrameColumn> columns) =>
        _columns = columns.ToArray();

    public IEnumerator<DataFrameColumn> GetEnumerator() => _columns.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class DataFrameRow : IEnumerable<object?>
{
    private readonly IReadOnlyList<object?> _values;

    public DataFrameRow(params object?[] values) => _values = values;

    public IEnumerator<object?> GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class DataFrameRowCollection : IEnumerable<DataFrameRow>
{
    private readonly IReadOnlyList<DataFrameRow> _rows;

    public DataFrameRowCollection(IEnumerable<DataFrameRow> rows) => _rows = rows.ToArray();

    public long Count => _rows.Count;
    public IEnumerator<DataFrameRow> GetEnumerator() => _rows.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
