using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Verso.Kernels;

/// <summary>
/// Manages Roslyn <see cref="ScriptState"/> chaining across cell executions.
/// Each successful execution appends to the state, preserving variables and definitions.
/// </summary>
internal sealed class ScriptStateManager : IAsyncDisposable
{
    private ScriptOptions _baseOptions;
    private ScriptState<object>? _lastState;

    public ScriptStateManager(ScriptOptions baseOptions)
    {
        _baseOptions = baseOptions ?? throw new ArgumentNullException(nameof(baseOptions));
    }

    /// <summary>
    /// Executes code, chaining onto the previous state if one exists.
    /// On the first execution, <paramref name="globals"/> is passed to Roslyn so its
    /// public properties become top-level identifiers in user scripts.
    /// </summary>
    public async Task<ScriptState<object>> RunAsync(
        string code, object? globals = null, CancellationToken cancellationToken = default)
    {
        ScriptState<object> state;

        if (_lastState is null)
        {
            state = await CSharpScript.RunAsync(code, _baseOptions,
                    globals: globals, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            state = await _lastState.ContinueWithAsync(code, _baseOptions,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        _lastState = state;
        return state;
    }

    /// <summary>
    /// Builds the script that <see cref="RunAsync"/> would run for this code, without running it,
    /// so the code can be examined against everything earlier cells declared.
    /// </summary>
    /// <param name="code">The code to compile as the next submission.</param>
    /// <param name="globalsType">The type of the globals object, used when nothing has run yet.</param>
    public Script<object> CreateScript(string code, Type? globalsType)
    {
        return _lastState is null
            ? CSharpScript.Create<object>(code, _baseOptions, globalsType)
            : _lastState.Script.ContinueWith<object>(code, _baseOptions);
    }

    /// <summary>
    /// Runs a script built by <see cref="CreateScript"/> as the next submission. Running the
    /// script that was examined, rather than its text again, saves compiling it a second time.
    /// </summary>
    public async Task<ScriptState<object>> RunScriptAsync(
        Script<object> script, object? globals = null, CancellationToken cancellationToken = default)
    {
        var state = _lastState is null
            ? await script.RunAsync(globals, cancellationToken).ConfigureAwait(false)
            : await script.RunFromAsync(_lastState, cancellationToken).ConfigureAwait(false);

        _lastState = state;
        return state;
    }

    /// <summary>
    /// Returns the most recent set of script variables, grouped by name with the last value winning.
    /// </summary>
    public IReadOnlyDictionary<string, object?> GetVariables()
    {
        if (_lastState is null) return new Dictionary<string, object?>();

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in _lastState.Variables)
        {
            dict[variable.Name] = variable.Value;
        }
        return dict;
    }

    /// <summary>
    /// Adds namespace imports so that subsequent executions can use types without fully qualifying them.
    /// </summary>
    public void AddImports(IEnumerable<string> namespaces)
    {
        var newImports = namespaces.Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();
        if (newImports.Length > 0)
            _baseOptions = _baseOptions.AddImports(newImports);
    }

    /// <summary>
    /// Adds metadata references from assembly paths so that subsequent executions can use those types.
    /// </summary>
    public void AddReferences(IEnumerable<string> assemblyPaths)
    {
        var refs = assemblyPaths
            .Where(File.Exists)
            .Select(p => MetadataReference.CreateFromFile(p))
            .ToArray();

        if (refs.Length > 0)
            _baseOptions = _baseOptions.AddReferences(refs);
    }

    /// <summary>
    /// Adds a loaded assembly as a reference, so that subsequent executions can use its types.
    /// </summary>
    public void AddReference(System.Reflection.Assembly assembly)
    {
        _baseOptions = _baseOptions.AddReferences(assembly);
    }

    public ValueTask DisposeAsync()
    {
        _lastState = null;
        return ValueTask.CompletedTask;
    }
}
