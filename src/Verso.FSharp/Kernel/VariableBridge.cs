using Verso.Abstractions;

namespace Verso.FSharp.Kernel;

/// <summary>
/// Bridges variables between the FSI session and the shared <see cref="IVariableStore"/>.
/// </summary>
internal sealed class VariableBridge
{
    /// <summary>
    /// Names injected by the bridge that should not be published back to the variable store.
    /// </summary>
    private static readonly HashSet<string> InjectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Variables",
        "VersoHelpers",
        "it"
    };

    private readonly FSharpKernelOptions _options;
    private HashSet<string> _previousBoundNames = new(StringComparer.OrdinalIgnoreCase);

    public VariableBridge(FSharpKernelOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Injects the <see cref="IVariableStore"/> into the FSI session as <c>Variables</c>,
    /// and evaluates an <c>[&lt;AutoOpen&gt;]</c> helper module providing <c>tryGetVar</c> convenience function.
    /// </summary>
    public void InjectVariables(FsiSessionManager session, IVariableStore store)
    {
        session.AddBoundValue("Variables", store);

        // Inject an AutoOpen helper module so F# code can use typed variable access
        // without requiring 'open VersoHelpers'
        session.EvalSilent(@"
[<AutoOpen>]
module VersoHelpers =
    let tryGetVar<'T> (name: string) : 'T option =
        let mutable value = Unchecked.defaultof<'T>
        if Variables.TryGet<'T>(name, &value) then
            Some value
        else
            None
");
    }

    /// <summary>
    /// Publishes new or changed F# bound values to the <see cref="IVariableStore"/>.
    /// Removes stale bindings that no longer exist in the session.
    /// Excludes underscore-prefixed names (unless <see cref="FSharpKernelOptions.PublishPrivateBindings"/> is true),
    /// functions, unit values, and injected names.
    /// </summary>
    public void PublishVariables(FsiSessionManager session, IVariableStore store)
    {
        var currentValues = session.GetBoundValues();
        var currentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, value, type) in currentValues)
        {
            currentNames.Add(name);

            // Skip injected names
            if (InjectedNames.Contains(name))
                continue;

            // Skip underscore-prefixed names (private by convention) unless configured
            if (!_options.PublishPrivateBindings && name.StartsWith("_", StringComparison.Ordinal))
                continue;

            // Skip unit values
            if (type == typeof(Microsoft.FSharp.Core.Unit))
                continue;

            // Skip function types (FSharpFunc<,>)
            if (IsFSharpFunction(type))
                continue;

            // Only publish new or changed bindings
            if (!_previousBoundNames.Contains(name) || HasValueChanged(name, value, store))
            {
                store.Set(name, value);
            }
        }

        // Remove stale bindings that were previously published but no longer exist
        foreach (var previousName in _previousBoundNames)
        {
            if (InjectedNames.Contains(previousName))
                continue;

            if (!currentNames.Contains(previousName))
            {
                store.Remove(previousName);
            }
        }

        _previousBoundNames = currentNames;
    }

    /// <summary>
    /// Resets tracking state (e.g., on kernel restart).
    /// </summary>
    public void Reset()
    {
        _previousBoundNames.Clear();
    }

    private static bool IsFSharpFunction(Type type)
    {
        if (!type.IsGenericType)
            return false;

        var genericDef = type.GetGenericTypeDefinition();
        return genericDef.FullName?.StartsWith("Microsoft.FSharp.Core.FSharpFunc`", StringComparison.Ordinal) == true;
    }

    private static bool HasValueChanged(string name, object newValue, IVariableStore store)
    {
        if (!store.TryGet<object>(name, out var existing) || existing is null)
            return true;

        return !ReferenceEquals(existing, newValue) && !existing.Equals(newValue);
    }
}
