using System.Collections;
using Verso.Abstractions;

namespace Verso.PowerShell.Kernel;

internal sealed class VariableBridge
{
    private readonly RunspaceManager _runspace;
    private readonly PowerShellKernelOptions _options;
    private HashSet<string> _previousNames = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, object>? _preExecutionSnapshot;

    public VariableBridge(RunspaceManager runspace, PowerShellKernelOptions options)
    {
        _runspace = runspace;
        _options = options;
    }

    public void InjectFromStore(IVariableStore store)
    {
        // Inject the store itself as $VersoVariables
        _runspace.SetVariable("VersoVariables", store);

        // Push each store entry as a PS variable
        foreach (var descriptor in store.GetAll())
        {
            if (PowerShellKernelOptions.AutomaticVariableExclusions.Contains(descriptor.Name))
                continue;

            if (store.TryGet<object>(descriptor.Name, out var value))
            {
                try
                {
                    _runspace.SetVariable(descriptor.Name, value);
                }
                catch
                {
                    // Variable may be read-only or constant in PS — skip silently
                }
            }
        }
    }

    /// <summary>
    /// Captures the variable store immediately before a cell runs. <see cref="PublishToStore"/>
    /// compares against this so it can tell a value the cell itself produced from one an external
    /// actor (for example a layout interaction handler) wrote to the store while the cell was
    /// executing. A store entry that changed during execution is left as-is rather than being
    /// overwritten with the session's pre-execution copy.
    /// </summary>
    public void SnapshotStore(IVariableStore store)
    {
        _preExecutionSnapshot = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in store.GetAll())
        {
            if (descriptor.Value is not null)
                _preExecutionSnapshot[descriptor.Name] = descriptor.Value;
        }
    }

    public void PublishToStore(IVariableStore store)
    {
        var currentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var variables = _runspace.GetSessionVariables();

        foreach (var (name, value) in variables)
        {
            if (ShouldExclude(name, value))
                continue;

            currentNames.Add(name);

            var unwrapped = UnwrapValue(value);

            // Only publish when the session value differs from the store, and never overwrite a
            // store entry that some external actor changed while this cell was running.
            if (HasValueChanged(name, unwrapped, store) && !WasSetDuringExecution(name, store))
            {
                store.Set(name, unwrapped!);
            }
        }

        // Remove stale variables that no longer exist in the PS session
        foreach (var previousName in _previousNames)
        {
            if (!currentNames.Contains(previousName))
            {
                store.Remove(previousName);
            }
        }

        _previousNames = currentNames;
    }

    public void Reset()
    {
        _previousNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _preExecutionSnapshot = null;
    }

    /// <summary>
    /// Returns <c>true</c> when the store's current value for <paramref name="name"/> differs from
    /// the pre-execution snapshot, meaning code outside this cell wrote it during execution. The
    /// session's stale copy must not overwrite that newer value.
    /// </summary>
    private bool WasSetDuringExecution(string name, IVariableStore store)
    {
        if (_preExecutionSnapshot is null)
            return false;

        if (!store.TryGet<object>(name, out var currentValue) || currentValue is null)
            return false;

        // Absent from the snapshot but present now -- added by something other than this cell.
        if (!_preExecutionSnapshot.TryGetValue(name, out var snapshotValue))
            return true;

        // Changed from the snapshot -- updated by something other than this cell.
        return !ReferenceEquals(snapshotValue, currentValue) && !snapshotValue.Equals(currentValue);
    }

    private bool ShouldExclude(string name, object? value)
    {
        if (PowerShellKernelOptions.AutomaticVariableExclusions.Contains(name))
            return true;

        if (!_options.PublishUnderscorePrefixed && name.StartsWith("_"))
            return true;

        if (value is null)
            return true;

        // Exclude ScriptBlock values (functions/closures)
        if (value.GetType().FullName == "System.Management.Automation.ScriptBlock")
            return true;

        // Exclude PSModuleInfo values
        if (value.GetType().FullName == "System.Management.Automation.PSModuleInfo")
            return true;

        return false;
    }

    private static object? UnwrapValue(object? value)
    {
        if (value is null) return null;

        // Convert Hashtable to Dictionary<string, object>
        if (value is Hashtable ht)
        {
            var dict = new Dictionary<string, object>(ht.Count);
            foreach (DictionaryEntry entry in ht)
            {
                dict[entry.Key.ToString()!] = entry.Value ?? string.Empty;
            }
            return dict;
        }

        return value;
    }

    private static bool HasValueChanged(string name, object? newValue, IVariableStore store)
    {
        if (newValue is null) return false;

        if (!store.TryGet<object>(name, out var existing) || existing is null)
            return true;

        return !ReferenceEquals(existing, newValue) && !existing.Equals(newValue);
    }
}
