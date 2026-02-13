using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Verso.Kernels;

/// <summary>
/// Manages Roslyn <see cref="ScriptState"/> chaining across cell executions.
/// Each successful execution appends to the state, preserving variables and definitions.
/// </summary>
internal sealed class ScriptStateManager : IAsyncDisposable
{
    private readonly ScriptOptions _baseOptions;
    private ScriptState<object>? _lastState;

    public ScriptStateManager(ScriptOptions baseOptions)
    {
        _baseOptions = baseOptions ?? throw new ArgumentNullException(nameof(baseOptions));
    }

    /// <summary>
    /// Executes code, chaining onto the previous state if one exists.
    /// </summary>
    public async Task<ScriptState<object>> RunAsync(string code, CancellationToken cancellationToken = default)
    {
        ScriptState<object> state;

        if (_lastState is null)
        {
            state = await CSharpScript.RunAsync(code, _baseOptions, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            state = await _lastState.ContinueWithAsync(code, _baseOptions, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

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

    public ValueTask DisposeAsync()
    {
        _lastState = null;
        return ValueTask.CompletedTask;
    }
}
