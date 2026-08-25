using System.Collections;

namespace Microsoft.Data.Analysis;

internal sealed class DataFrame
{
    public DataFrame(
        IEnumerable<DataFrameColumn>? columns = null,
        IEnumerable<DataFrameRow>? rows = null)
        : this(columns, new DataFrameRowCollection(rows ?? Array.Empty<DataFrameRow>()))
    {
    }

    public DataFrame(IEnumerable<DataFrameColumn>? columns, DataFrameRowCollection rows)
    {
        Columns = new DataFrameColumnCollection(columns ?? Array.Empty<DataFrameColumn>());
        Rows = rows;
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

    public int Count => _columns.Count;
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
    private readonly IReadOnlyList<DataFrameRow>? _rows;
    private readonly Exception? _enumerationError;

    public DataFrameRowCollection(IEnumerable<DataFrameRow> rows) => _rows = rows.ToArray();

    public DataFrameRowCollection(Exception enumerationError) =>
        _enumerationError = enumerationError;

    public long Count => _rows?.Count ?? 0;

    public IEnumerator<DataFrameRow> GetEnumerator()
    {
        if (_enumerationError is not null)
            throw _enumerationError;

        return _rows!.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
