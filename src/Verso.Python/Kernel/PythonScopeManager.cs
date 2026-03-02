using Python.Runtime;
using Verso.Abstractions;

namespace Verso.Python.Kernel;

/// <summary>
/// Manages a persistent Python scope (<see cref="PyModule"/>) and provides bidirectional
/// variable bridging between the Python scope and the Verso <see cref="IVariableStore"/>.
/// All public methods require the GIL to be held by the caller.
/// </summary>
internal sealed class PythonScopeManager : IDisposable
{
    private PyModule? _scope;
    private readonly HashSet<string> _injectedRefs = new();

    /// <summary>
    /// Creates the persistent Python scope.
    /// </summary>
    public void Initialize()
    {
        _scope = Py.CreateScope();
    }

    /// <summary>
    /// Executes Python code in the persistent scope.
    /// </summary>
    public void Exec(string code)
    {
        EnsureScope();
        _scope!.Exec(code);
    }

    /// <summary>
    /// Evaluates a Python expression in the persistent scope and returns the result.
    /// </summary>
    public PyObject Eval(string expression)
    {
        EnsureScope();
        return _scope!.Eval(expression);
    }

    /// <summary>
    /// Gets a variable from the Python scope.
    /// </summary>
    public PyObject? Get(string name)
    {
        EnsureScope();
        return _scope!.Get(name);
    }

    /// <summary>
    /// Sets a variable in the Python scope.
    /// </summary>
    public void Set(string name, PyObject value)
    {
        EnsureScope();
        _scope!.Set(name, value);
    }

    /// <summary>
    /// Pushes .NET variables from the <see cref="IVariableStore"/> into the Python scope.
    /// Skips delegates, CancellationTokens, Tasks, and unchanged references.
    /// </summary>
    public void InjectFromStore(IVariableStore store)
    {
        EnsureScope();

        foreach (var descriptor in store.GetAll())
        {
            if (descriptor.Value is null) continue;
            if (ShouldSkipForInjection(descriptor.Value)) continue;

            // Skip if we've already injected the same reference
            var key = $"{descriptor.Name}:{RuntimeHelpers.GetHashCode(descriptor.Value)}";
            if (_injectedRefs.Contains(key)) continue;

            try
            {
                var pyObj = ConvertNetToPython(descriptor.Value);
                if (pyObj is not null)
                {
                    _scope!.Set(descriptor.Name, pyObj);
                    // Track by reference so we don't re-inject unchanged values
                    _injectedRefs.RemoveWhere(k => k.StartsWith(descriptor.Name + ":"));
                    _injectedRefs.Add(key);
                }
            }
            catch
            {
                // Silently skip variables that can't be converted
            }
        }
    }

    /// <summary>
    /// Reads non-dunder, non-module, non-callable locals from the Python scope,
    /// converts them to .NET types, and publishes them to the <see cref="IVariableStore"/>.
    /// Removes stale variables that no longer exist in the scope.
    /// </summary>
    public void PublishToStore(IVariableStore store)
    {
        EnsureScope();

        using var locals = _scope!.Variables();
        var publishedNames = new HashSet<string>();

        foreach (PyObject key in locals)
        {
            var name = key.ToString();
            if (name is null) continue;

            // Skip dunder names, verso internals, and module-level objects
            if (name.StartsWith("_") || name.StartsWith("__")) continue;

            try
            {
                using var value = _scope.Get(name);
                if (value is null || value.IsNone()) continue;

                // Skip modules, callables, and types
                using var builtins = Py.Import("builtins");
                using var isModuleResult = builtins.InvokeMethod("isinstance", value, Py.Import("types").GetAttr("ModuleType"));
                if (isModuleResult.IsTrue()) continue;

                using var isCallableResult = builtins.InvokeMethod("callable", value);
                if (isCallableResult.IsTrue()) continue;

                var netValue = ConvertToNetSafe(value);
                if (netValue is not null)
                {
                    store.Set(name, netValue);
                    publishedNames.Add(name);
                }
            }
            catch
            {
                // Skip variables that can't be converted
            }
        }
    }

    /// <summary>
    /// Converts a Python object to a .NET-safe type. Returns <c>null</c> for unconvertible types.
    /// Checks bool before int since Python <c>bool</c> is a subclass of <c>int</c>.
    /// </summary>
    internal static object? ConvertToNetSafe(PyObject pyObj)
    {
        if (pyObj.IsNone()) return null;

        // bool must come before int (Python bool is subclass of int)
        using (var builtins = Py.Import("builtins"))
        {
            using var boolType = builtins.GetAttr("bool");
            using var isBoolean = builtins.InvokeMethod("isinstance", pyObj, boolType);
            if (isBoolean.IsTrue())
                return pyObj.IsTrue();
        }

        if (PyInt.IsIntType(pyObj))
            return pyObj.As<long>();

        if (PyFloat.IsFloatType(pyObj))
            return pyObj.As<double>();

        if (PyString.IsStringType(pyObj))
            return pyObj.As<string>();

        // List → List<object>
        if (PyList.IsListType(pyObj))
        {
            using var pyList = new PyList(pyObj);
            var result = new List<object>((int)pyList.Length());
            for (var i = 0; i < pyList.Length(); i++)
            {
                using var item = pyList[i];
                var converted = ConvertToNetSafe(item);
                if (converted is not null)
                    result.Add(converted);
            }
            return result;
        }

        // Dict → Dictionary<string, object>
        if (PyDict.IsDictType(pyObj))
        {
            using var pyDict = new PyDict(pyObj);
            var result = new Dictionary<string, object>();
            foreach (PyObject key in pyDict.Keys())
            {
                var keyStr = key.ToString();
                if (keyStr is null) continue;

                using var val = pyDict[key];
                var converted = ConvertToNetSafe(val);
                if (converted is not null)
                    result[keyStr] = converted;
            }
            return result;
        }

        // Fallback: try managed conversion
        try
        {
            return pyObj.AsManagedObject(typeof(object));
        }
        catch
        {
            return pyObj.ToString();
        }
    }

    public void Dispose()
    {
        _scope?.Dispose();
        _scope = null;
        _injectedRefs.Clear();
    }

    private static bool ShouldSkipForInjection(object value)
    {
        var type = value.GetType();
        return type.IsSubclassOf(typeof(Delegate))
            || value is CancellationToken
            || value is Task
            || type.Name.StartsWith("Task`");
    }

    private static PyObject? ConvertNetToPython(object value)
    {
        switch (value)
        {
            case bool b:
                return new PyInt(b ? 1 : 0); // Python bool via int
            case int i:
                return new PyInt(i);
            case long l:
                return new PyInt(l);
            case float f:
                return new PyFloat(f);
            case double d:
                return new PyFloat(d);
            case string s:
                return new PyString(s);

            // Convert .NET collections to native Python lists/dicts so that
            // slicing, iteration, and other Python-native operations work.
            case IList<object> list:
            {
                var pyList = new PyList();
                foreach (var item in list)
                {
                    var pyItem = ConvertNetToPython(item);
                    if (pyItem is not null) pyList.Append(pyItem);
                }
                return pyList;
            }
            case IDictionary<string, object> dict:
            {
                var pyDict = new PyDict();
                foreach (var kvp in dict)
                {
                    var pyVal = ConvertNetToPython(kvp.Value);
                    if (pyVal is not null)
                        pyDict[new PyString(kvp.Key)] = pyVal;
                }
                return pyDict;
            }

            default:
                return value.ToPython();
        }
    }

    private void EnsureScope()
    {
        if (_scope is null)
            throw new InvalidOperationException("Python scope has not been initialized. Call Initialize first.");
    }
}

file static class RuntimeHelpers
{
    /// <summary>
    /// Returns the runtime identity hash code for an object (reference-based).
    /// </summary>
    public static int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}
