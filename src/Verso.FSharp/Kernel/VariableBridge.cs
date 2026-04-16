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

    private FSharpKernelOptions _options;
    private HashSet<string> _previousBoundNames = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, object>? _preExecutionSnapshot;

    public VariableBridge(FSharpKernelOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Updates the options reference so that runtime setting changes (e.g. publishPrivateBindings)
    /// take effect without requiring a kernel restart.
    /// </summary>
    internal void UpdateOptions(FSharpKernelOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Injects the <see cref="IVariableStore"/> into the FSI session as <c>Variables</c>,
    /// and evaluates an <c>[&lt;AutoOpen&gt;]</c> helper module providing <c>tryGetVar</c> convenience function.
    /// Called once during kernel initialization.
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

        // Display functions are injected separately so that the type resolution
        // for Verso.Abstractions.DisplayExtensions does not pollute the FSI
        // type environment within the VersoHelpers module definition.
        session.EvalSilent(@"
let display (value: obj) : unit =
    Verso.Abstractions.DisplayExtensions.Display(value, null)

let displayAs (mimeType: string) (value: obj) : unit =
    Verso.Abstractions.DisplayExtensions.Display(value, mimeType)
");
    }

    /// <summary>
    /// Injects all shared variables from the <see cref="IVariableStore"/> as top-level
    /// FSI bindings so other kernels' outputs are accessible by name.
    /// Called before every cell execution so values stay current.
    /// </summary>
    public void InjectFromStore(FsiSessionManager session, IVariableStore store)
    {
        foreach (var desc in store.GetAll())
        {
            if (desc.Value is null) continue;
            if (InjectedNames.Contains(desc.Name)) continue;
            if (desc.Name.StartsWith("__verso_", StringComparison.Ordinal)) continue;

            // Skip variables that already exist as native FSI bindings from this kernel.
            // Re-injecting them via AddBoundValue would shadow the F#-typed binding
            // with a .NET-typed one (e.g. System.Int32 instead of int), breaking
            // printf format specifiers and other F# type-sensitive operations.
            if (_previousBoundNames.Contains(desc.Name))
                continue;

            if (desc.Value is Delegate or CancellationToken or Task or IAsyncDisposable)
                continue;

            try
            {
                // Numeric and other primitive values must be injected via let bindings
                // with F#'s native types. AddBoundValue binds them as their .NET types
                // (e.g. System.Int32 instead of int, System.Double instead of float),
                // which F#'s type checker treats differently (e.g. printfn "%d" fails).
                if (desc.Value is string s)
                {
                    var escaped = s.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    session.EvalSilent($"let {desc.Name} = \"{escaped}\"");
                }
                else if (desc.Value is int i)
                {
                    session.EvalSilent($"let {desc.Name} = {i}");
                }
                else if (desc.Value is long l)
                {
                    session.EvalSilent($"let {desc.Name} = {l}L");
                }
                else if (desc.Value is double d)
                {
                    session.EvalSilent($"let {desc.Name} = {d.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                }
                else if (desc.Value is float f)
                {
                    session.EvalSilent($"let {desc.Name} = {f.ToString(System.Globalization.CultureInfo.InvariantCulture)}f");
                }
                else if (desc.Value is bool b)
                {
                    session.EvalSilent($"let {desc.Name} = {(b ? "true" : "false")}");
                }
                else if (desc.Value is decimal m)
                {
                    session.EvalSilent($"let {desc.Name} = {m.ToString(System.Globalization.CultureInfo.InvariantCulture)}M");
                }
                else
                {
                    session.AddBoundValue(desc.Name, desc.Value);
                }
            }
            catch
            {
                // Some values may not be representable in FSI; skip silently
            }
        }
    }

    /// <summary>
    /// Captures a snapshot of the variable store before cell execution so that
    /// <see cref="PublishVariables"/> can detect values that user code explicitly
    /// set via <c>Variables.Set</c> and avoid overwriting them with stale FSI bindings.
    /// </summary>
    public void SnapshotStore(IVariableStore store)
    {
        _preExecutionSnapshot = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var desc in store.GetAll())
        {
            if (desc.Value is not null)
                _preExecutionSnapshot[desc.Name] = desc.Value;
        }
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

            // Only publish new or changed bindings, but don't overwrite values
            // that user code explicitly set via Variables.Set during execution.
            if (!_previousBoundNames.Contains(name) || HasValueChanged(name, value, store))
            {
                if (!WasSetDuringExecution(name, store))
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
        _preExecutionSnapshot = null;
    }

    /// <summary>
    /// Returns <c>true</c> if the store's current value for <paramref name="name"/> differs
    /// from the pre-execution snapshot, indicating that user code called <c>Variables.Set</c>.
    /// </summary>
    private bool WasSetDuringExecution(string name, IVariableStore store)
    {
        if (_preExecutionSnapshot is null)
            return false;

        if (!store.TryGet<object>(name, out var currentValue) || currentValue is null)
            return false;

        // Name was absent from the snapshot but now exists -- user code added it
        if (!_preExecutionSnapshot.TryGetValue(name, out var snapshotValue))
            return true;

        // Value changed from the snapshot -- user code updated it
        return !ReferenceEquals(snapshotValue, currentValue) && !snapshotValue.Equals(currentValue);
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
