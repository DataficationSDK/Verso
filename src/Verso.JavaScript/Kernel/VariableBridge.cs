using System.Text.Json;
using Verso.Abstractions;

namespace Verso.JavaScript.Kernel;

/// <summary>
/// Translates between <see cref="IVariableStore"/> (CLR objects) and the JSON wire format
/// used by <see cref="IJavaScriptRunner"/>.
/// </summary>
internal sealed class VariableBridge
{
    private readonly JavaScriptKernelOptions _options;
    private HashSet<string> _lastPublished = [];

    public VariableBridge(JavaScriptKernelOptions options) => _options = options;

    /// <summary>
    /// Injects variables from the store into the JS runner as JSON.
    /// </summary>
    public async Task InjectFromStore(IVariableStore store, IJavaScriptRunner runner, CancellationToken ct)
    {
        var descriptors = store.GetAll();
        var toInject = new Dictionary<string, string>();

        foreach (var desc in descriptors)
        {
            if (desc.Value is null) continue;
            if (ShouldSkip(desc.Name, desc.Value)) continue;

            try
            {
                var json = JsonSerializer.Serialize(desc.Value);
                toInject[desc.Name] = json;
            }
            catch
            {
                // Non-serializable value, skip
            }
        }

        if (toInject.Count > 0)
            await runner.SetVariablesAsync(toInject, ct);
    }

    /// <summary>
    /// Publishes user-defined JS globals back to the variable store.
    /// </summary>
    public async Task PublishToStore(
        IVariableStore store,
        IJavaScriptRunner runner,
        IReadOnlyList<string>? userGlobals,
        CancellationToken ct)
    {
        if (userGlobals is null || userGlobals.Count == 0)
        {
            // Remove previously published variables that no longer exist
            foreach (var name in _lastPublished)
                store.Remove(name);
            _lastPublished = [];
            return;
        }

        var values = await runner.GetVariablesAsync(userGlobals, ct);
        var currentNames = new HashSet<string>();

        foreach (var (name, json) in values)
        {
            if (json is null) continue;

            try
            {
                var clr = JsonElementToClr(JsonDocument.Parse(json).RootElement);
                if (clr is not null)
                {
                    store.Set(name, clr);
                    currentNames.Add(name);
                }
            }
            catch
            {
                // Non-parseable, store as string
                store.Set(name, json);
                currentNames.Add(name);
            }
        }

        // Remove variables that were published before but are gone now
        foreach (var name in _lastPublished)
        {
            if (!currentNames.Contains(name))
                store.Remove(name);
        }

        _lastPublished = currentNames;
    }

    private static bool ShouldSkip(string name, object value)
    {
        if (name.StartsWith("__verso_")) return true;

        return value is Delegate
            or CancellationToken
            or Task
            or IAsyncDisposable;
    }

    /// <summary>
    /// Recursively converts a <see cref="JsonElement"/> to a CLR type.
    /// </summary>
    internal static object? JsonElementToClr(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(JsonElementToClr)
                .Where(v => v is not null)
                .ToList(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => JsonElementToClr(p.Value)!),
            _ => null,
        };
    }
}
