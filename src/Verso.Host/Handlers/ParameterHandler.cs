using System.Text.Json;
using Verso.Abstractions;
using Verso.Host.Dto;
using Verso.Host.Protocol;
using Verso.Parameters;

namespace Verso.Host.Handlers;

public static class ParameterHandler
{
    public static ParameterListResult HandleList(NotebookSession ns)
    {
        var parameters = ns.Scaffold.Notebook.Parameters;
        if (parameters is null || parameters.Count == 0)
            return new ParameterListResult();

        var result = new Dictionary<string, ParameterDefDto>();
        foreach (var (name, def) in parameters)
        {
            result[name] = MapDefinition(def);
        }

        return new ParameterListResult { Parameters = result };
    }

    public static object HandleAdd(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<ParameterAddParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for parameter/add");

        if (string.IsNullOrEmpty(p.Name))
            throw new JsonException("Parameter name is required.");

        var notebook = ns.Scaffold.Notebook;
        notebook.Parameters ??= new Dictionary<string, NotebookParameterDefinition>();

        if (notebook.Parameters.ContainsKey(p.Name))
            throw new InvalidOperationException($"Parameter '{p.Name}' already exists.");

        var typeId = p.Type ?? "string";
        object? defaultValue = null;
        if (!string.IsNullOrEmpty(p.DefaultValue))
        {
            if (!ParameterValueParser.TryParse(typeId, p.DefaultValue, out var parsed, out var error))
                throw new InvalidOperationException($"Invalid default value: {error}");
            defaultValue = parsed;
        }

        var def = new NotebookParameterDefinition
        {
            Type = typeId,
            Description = p.Description,
            Required = p.Required ?? false,
            Default = defaultValue,
        };

        notebook.Parameters[p.Name] = def;

        if (defaultValue is not null)
            ns.Scaffold.Variables.Set(p.Name, defaultValue);

        // Auto-insert a parameters cell at index 0 if none exists
        if (!notebook.Cells.Any(c => string.Equals(c.Type, "parameters", StringComparison.OrdinalIgnoreCase)))
        {
            ns.Scaffold.InsertCell(0, "parameters");
        }

        InvalidateParametersCell(ns);

        return new { name = p.Name, parameter = MapDefinition(def) };
    }

    public static object HandleUpdate(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<ParameterUpdateParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for parameter/update");

        if (string.IsNullOrEmpty(p.Name))
            throw new JsonException("Parameter name is required.");

        var parameters = ns.Scaffold.Notebook.Parameters;
        if (parameters is null || !parameters.TryGetValue(p.Name, out var def))
            throw new InvalidOperationException($"Parameter '{p.Name}' not found.");

        if (p.Type is not null)
            def.Type = p.Type;

        if (p.Description is not null)
            def.Description = p.Description;

        if (p.Required is not null)
            def.Required = p.Required.Value;

        if (p.DefaultValue is not null)
        {
            if (!ParameterValueParser.TryParse(def.Type, p.DefaultValue, out var parsed, out var error))
                throw new InvalidOperationException($"Invalid default value: {error}");

            def.Default = parsed;
            ns.Scaffold.Variables.Set(p.Name, parsed!);
        }

        InvalidateParametersCell(ns);

        return new { name = p.Name, parameter = MapDefinition(def) };
    }

    public static object HandleRemove(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<ParameterRemoveParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for parameter/remove");

        if (string.IsNullOrEmpty(p.Name))
            throw new JsonException("Parameter name is required.");

        var parameters = ns.Scaffold.Notebook.Parameters;
        if (parameters is null || !parameters.Remove(p.Name))
            throw new InvalidOperationException($"Parameter '{p.Name}' not found.");

        ns.Scaffold.Variables.Remove(p.Name);
        InvalidateParametersCell(ns);

        return new { success = true };
    }

    /// <summary>
    /// Clears the outputs of the parameters cell so the WASM auto-execute guard
    /// re-renders it with the updated parameter definitions.
    /// </summary>
    private static void InvalidateParametersCell(NotebookSession ns)
    {
        var cell = ns.Scaffold.Notebook.Cells
            .FirstOrDefault(c => string.Equals(c.Type, "parameters", StringComparison.OrdinalIgnoreCase));
        cell?.Outputs.Clear();
    }

    private static ParameterDefDto MapDefinition(NotebookParameterDefinition def)
    {
        return new ParameterDefDto
        {
            Type = def.Type,
            Description = def.Description,
            Default = def.Default,
            Required = def.Required,
            Order = def.Order,
        };
    }
}
