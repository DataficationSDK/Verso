using Verso.Abstractions;

namespace Verso.Cli.Parameters;

/// <summary>
/// Resolves notebook parameters by merging CLI overrides with notebook-defined defaults,
/// validating types and required constraints.
/// </summary>
public sealed class ParameterResolver
{
    private readonly Dictionary<string, NotebookParameterDefinition>? _definitions;
    private readonly Dictionary<string, string> _cliParams;
    private readonly bool _isVersoFormat;
    private readonly bool _interactive;
    private readonly bool _isInputRedirected;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public ParameterResolver(
        Dictionary<string, NotebookParameterDefinition>? definitions,
        Dictionary<string, string> cliParams,
        bool isVersoFormat,
        bool interactive = false,
        TextReader? input = null,
        TextWriter? output = null,
        TextWriter? error = null,
        bool? isInputRedirected = null)
    {
        _definitions = definitions;
        _cliParams = cliParams;
        _isVersoFormat = isVersoFormat;
        _interactive = interactive;
        _input = input ?? Console.In;
        _output = output ?? Console.Out;
        _error = error ?? Console.Error;
        _isInputRedirected = isInputRedirected ?? Console.IsInputRedirected;
    }

    /// <summary>
    /// Resolves all parameters and returns the result.
    /// </summary>
    public ParameterResolutionResult Resolve()
    {
        // For non-.verso formats, inject all CLI params as untyped strings
        if (!_isVersoFormat)
        {
            return ResolveUntyped();
        }

        // No definitions and no CLI params means nothing to do
        if (_definitions is null or { Count: 0 } && _cliParams.Count == 0)
        {
            return ParameterResolutionResult.Success(new Dictionary<string, object>());
        }

        var resolved = new Dictionary<string, object>();
        var errors = new List<string>();
        var definitions = _definitions ?? new Dictionary<string, NotebookParameterDefinition>();

        // 1. Apply CLI overrides with type parsing
        foreach (var (name, rawValue) in _cliParams)
        {
            if (definitions.TryGetValue(name, out var def))
            {
                if (ParameterTypeParser.TryParse(def.Type, rawValue, out var typed, out var parseError))
                {
                    resolved[name] = typed!;
                }
                else
                {
                    errors.Add($"  {name} ({def.Type}): {parseError}");
                }
            }
            else
            {
                // Unknown parameter: inject as string with warning
                _error.WriteLine($"Warning: Unknown parameter '{name}' not defined in notebook metadata. Injecting as string.");
                resolved[name] = rawValue;
            }
        }

        // If there were parse errors, fail early
        if (errors.Count > 0)
        {
            return ParameterResolutionResult.Failure(
                "Error: Invalid parameter values:\n" + string.Join("\n", errors));
        }

        // 2. Apply defaults for unspecified parameters
        foreach (var (name, def) in definitions)
        {
            if (!resolved.ContainsKey(name) && def.Default is not null)
            {
                resolved[name] = CoerceDefault(def.Default, def.Type);
            }
        }

        // 3. Handle interactive prompting for missing parameters
        if (_interactive && !_isInputRedirected)
        {
            PromptForParameters(definitions, resolved);
        }

        // 4. Validate required parameters
        var missing = new List<(string Name, NotebookParameterDefinition Def)>();
        foreach (var (name, def) in definitions)
        {
            if (def.Required && !resolved.ContainsKey(name))
            {
                missing.Add((name, def));
            }
        }

        if (missing.Count > 0)
        {
            var lines = missing
                .Select(m =>
                {
                    var desc = m.Def.Description is not null ? $"  {m.Def.Description}" : "";
                    return $"  {m.Name} ({m.Def.Type}){desc}";
                });

            return ParameterResolutionResult.Failure(
                "Error: Missing required notebook parameters:\n\n" +
                string.Join("\n", lines) +
                "\n\nSupply values with --param or use --interactive to be prompted.");
        }

        // 5. Sort by Order then alphabetically
        var sorted = SortParameters(resolved, definitions);
        return ParameterResolutionResult.Success(sorted);
    }

    private ParameterResolutionResult ResolveUntyped()
    {
        if (_cliParams.Count == 0)
        {
            return ParameterResolutionResult.Success(new Dictionary<string, object>());
        }

        _error.WriteLine("Warning: Parameter definitions are only supported for .verso files. Injecting all --param values as untyped strings.");

        var resolved = new Dictionary<string, object>();
        foreach (var (name, value) in _cliParams)
        {
            resolved[name] = value;
        }
        return ParameterResolutionResult.Success(resolved);
    }

    private void PromptForParameters(
        Dictionary<string, NotebookParameterDefinition> definitions,
        Dictionary<string, object> resolved)
    {
        var sortedDefs = definitions
            .OrderBy(kvp => kvp.Value.Order ?? int.MaxValue)
            .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _output.WriteLine();
        _output.WriteLine("Notebook parameters:");
        _output.WriteLine();

        foreach (var (name, def) in sortedDefs)
        {
            // Skip parameters already resolved via CLI
            if (resolved.ContainsKey(name)) continue;

            var typeLabel = def.Required ? $"{def.Type}, required" : def.Type;
            var defaultLabel = def.Default is not null ? $", default: {def.Default}" : "";
            var desc = def.Description is not null ? $" {def.Description}" : "";

            _output.WriteLine($"  {name} ({typeLabel}{defaultLabel}){desc}");

            while (true)
            {
                _output.Write("  > ");
                _output.Flush();
                var line = _input.ReadLine();

                // Accept default on empty input
                if (string.IsNullOrEmpty(line))
                {
                    if (def.Default is not null)
                    {
                        resolved[name] = CoerceDefault(def.Default, def.Type);
                        break;
                    }
                    if (!def.Required)
                    {
                        break; // Optional with no default, skip
                    }
                    _output.WriteLine("    Value is required.");
                    continue;
                }

                if (ParameterTypeParser.TryParse(def.Type, line, out var typed, out var parseError))
                {
                    resolved[name] = typed!;
                    break;
                }
                _output.WriteLine($"    {parseError}");
            }
        }

        _output.WriteLine();
    }

    private static object CoerceDefault(object defaultValue, string typeId)
    {
        // Defaults from deserialization may already be the correct CLR type.
        // Strings from JSON need parsing for date/datetime types.
        if (defaultValue is string strVal && typeId is "date" or "datetime")
        {
            if (ParameterTypeParser.TryParse(typeId, strVal, out var parsed, out _))
                return parsed!;
        }

        return defaultValue;
    }

    private static Dictionary<string, object> SortParameters(
        Dictionary<string, object> resolved,
        Dictionary<string, NotebookParameterDefinition> definitions)
    {
        var sorted = new Dictionary<string, object>();

        var ordered = resolved.Keys
            .OrderBy(name =>
            {
                if (definitions.TryGetValue(name, out var def) && def.Order.HasValue)
                    return def.Order.Value;
                return int.MaxValue;
            })
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var name in ordered)
        {
            sorted[name] = resolved[name];
        }

        return sorted;
    }
}

/// <summary>
/// Result of parameter resolution, containing either resolved parameters or an error message.
/// </summary>
public sealed class ParameterResolutionResult
{
    public bool IsSuccess { get; private init; }
    public Dictionary<string, object> Parameters { get; private init; } = new();
    public string? ErrorMessage { get; private init; }

    public static ParameterResolutionResult Success(Dictionary<string, object> parameters) =>
        new() { IsSuccess = true, Parameters = parameters };

    public static ParameterResolutionResult Failure(string errorMessage) =>
        new() { IsSuccess = false, ErrorMessage = errorMessage };
}
