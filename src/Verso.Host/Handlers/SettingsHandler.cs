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
        {
            value = valueElement.ValueKind switch
            {
                JsonValueKind.String => valueElement.GetString(),
                JsonValueKind.Number => valueElement.TryGetInt64(out var l) ? (object)l : valueElement.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => valueElement.GetRawText()
            };
        }

        await ns.Scaffold.SettingsManager!.UpdateSettingAsync(extensionId, name, value);

        return HandleGet(ns, @params);
    }

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
