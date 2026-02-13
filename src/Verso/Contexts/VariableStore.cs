using System.Collections.Concurrent;
using Verso.Abstractions;

namespace Verso.Contexts;

/// <summary>
/// Thread-safe <see cref="IVariableStore"/> backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
public sealed class VariableStore : IVariableStore
{
    private readonly ConcurrentDictionary<string, object> _variables = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Set(string name, object value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        _variables[name] = value;
    }

    /// <inheritdoc />
    public T? Get<T>(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_variables.TryGetValue(name, out var value) && value is T typed)
            return typed;
        return default;
    }

    /// <inheritdoc />
    public bool TryGet<T>(string name, out T? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_variables.TryGetValue(name, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }
        value = default;
        return false;
    }

    /// <inheritdoc />
    public IReadOnlyList<VariableDescriptor> GetAll()
    {
        return _variables.Select(kvp =>
            new VariableDescriptor(kvp.Key, kvp.Value, kvp.Value.GetType()))
            .ToList();
    }

    /// <inheritdoc />
    public bool Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _variables.TryRemove(name, out _);
    }

    /// <inheritdoc />
    public void Clear() => _variables.Clear();
}
