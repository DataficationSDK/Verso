using System.Text.Json;
using Verso.Abstractions;
using Verso.Host.Dto;

namespace Verso.Host.Handlers;

public static class SettingsHandler
{
    public static SettingsGetDefinitionsResult HandleGetDefinitions(NotebookSession ns)
    {
        var manager = ns.Scaffold.SettingsManager;
        if (manager is null)
            return new SettingsGetDefinitionsResult();

        var allDefs = manager.GetAllDefinitions();
        var result = new SettingsGetDefinitionsResult();

        foreach (var (extensionId, definitions) in allDefs)
        {
            var extInfo = ns.ExtensionHost.GetExtensionInfos()
                .FirstOrDefault(e => string.Equals(e.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase));

            var settable = ns.ExtensionHost.GetSettableExtensions()
                .FirstOrDefault(s => s is IExtension ext &&
                    string.Equals(ext.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase));

            var dto = new ExtensionSettingsDto
            {
                ExtensionId = extensionId,
                ExtensionName = extInfo?.Name ?? extensionId,
                Definitions = definitions.Select(d => new SettingDefinitionDto
                {
                    Name = d.Name,
                    DisplayName = d.DisplayName,
                    Description = d.Description,
                    SettingType = d.SettingType.ToString(),
                    DefaultValue = d.DefaultValue,
                    Category = d.Category,
                    Constraints = d.Constraints is not null ? new SettingConstraintsDto
                    {
                        MinValue = d.Constraints.MinValue,
                        MaxValue = d.Constraints.MaxValue,
                        Pattern = d.Constraints.Pattern,
                        Choices = d.Constraints.Choices?.ToList(),
                        MaxLength = d.Constraints.MaxLength,
                        MaxItems = d.Constraints.MaxItems
                    } : null,
                    Order = d.Order
                }).ToList(),
                CurrentValues = settable is not null
                    ? new Dictionary<string, object?>(settable.GetSettingValues())
                    : new Dictionary<string, object?>()
            };

            result.Extensions.Add(dto);
        }

        return result;
    }

    public static SettingsGetResult HandleGet(NotebookSession ns, JsonElement? @params)
    {
        var extensionId = @params?.GetProperty("extensionId").GetString()
            ?? throw new JsonException("Missing extensionId");

        var settable = ns.ExtensionHost.GetSettableExtensions()
            .FirstOrDefault(s => s is IExtension ext &&
                string.Equals(ext.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Extension '{extensionId}' does not implement IExtensionSettings.");

        return new SettingsGetResult
        {
            ExtensionId = extensionId,
            Values = new Dictionary<string, object?>(settable.GetSettingValues())
        };
    }

    public static async Task<SettingsGetResult> HandleUpdateAsync(NotebookSession ns, JsonElement? @params)
    {
        var extensionId = @params?.GetProperty("extensionId").GetString()
            ?? throw new JsonException("Missing extensionId");
        var name = @params?.GetProperty("name").GetString()
            ?? throw new JsonException("Missing name");

        object? value = null;
        if (@params?.TryGetProperty("value", out var valueElement) == true)
            value = ReadSettingValue(valueElement);

        await ns.Scaffold.SettingsManager!.UpdateSettingAsync(extensionId, name, value);

        return HandleGet(ns, @params);
    }

    /// <summary>
    /// Turn a setting value from the wire into what the extension expects. Every setting type is
    /// represented, so nothing arrives as raw JSON text that the extension then has to parse.
    /// </summary>
    internal static object? ReadSettingValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        // The cast matters: without it both branches unify as double and an integer setting
        // arrives as a floating-point value.
        JsonValueKind.Number => value.TryGetInt64(out var whole) ? (object)whole : value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,

        // A list setting travels as an array. Handing the raw JSON text through instead would
        // reach the extension as one string containing brackets and quotes.
        JsonValueKind.Array => value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : item.GetRawText())
            .Where(item => item is not null)
            .ToList(),

        _ => value.GetRawText(),
    };

    public static async Task<SettingsGetResult> HandleResetAsync(NotebookSession ns, JsonElement? @params)
    {
        var extensionId = @params?.GetProperty("extensionId").GetString()
            ?? throw new JsonException("Missing extensionId");
        var name = @params?.GetProperty("name").GetString()
            ?? throw new JsonException("Missing name");

        await ns.Scaffold.SettingsManager!.ResetSettingAsync(extensionId, name);

        return HandleGet(ns, @params);
    }
}
