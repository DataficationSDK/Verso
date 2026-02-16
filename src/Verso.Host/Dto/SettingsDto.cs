namespace Verso.Host.Dto;

// --- Settings Management ---

public sealed class SettingsGetDefinitionsResult
{
    public List<ExtensionSettingsDto> Extensions { get; set; } = new();
}

public sealed class ExtensionSettingsDto
{
    public string ExtensionId { get; set; } = "";
    public string ExtensionName { get; set; } = "";
    public List<SettingDefinitionDto> Definitions { get; set; } = new();
    public Dictionary<string, object?> CurrentValues { get; set; } = new();
}

public sealed class SettingDefinitionDto
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string SettingType { get; set; } = "";
    public object? DefaultValue { get; set; }
    public string? Category { get; set; }
    public SettingConstraintsDto? Constraints { get; set; }
    public int Order { get; set; }
}

public sealed class SettingConstraintsDto
{
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public string? Pattern { get; set; }
    public List<string>? Choices { get; set; }
    public int? MaxLength { get; set; }
    public int? MaxItems { get; set; }
}

public sealed class SettingsGetResult
{
    public string ExtensionId { get; set; } = "";
    public Dictionary<string, object?> Values { get; set; } = new();
}

public sealed class SettingsUpdateParams
{
    public string ExtensionId { get; set; } = "";
    public string Name { get; set; } = "";
    public object? Value { get; set; }
}

public sealed class SettingsResetParams
{
    public string ExtensionId { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class SettingsGetParams
{
    public string ExtensionId { get; set; } = "";
}
