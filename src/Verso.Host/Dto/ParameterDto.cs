namespace Verso.Host.Dto;

// --- Parameter operations ---

public sealed class ParameterAddParams
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "string";
    public string? Description { get; set; }
    public string? DefaultValue { get; set; }
    public bool? Required { get; set; }
}

public sealed class ParameterUpdateParams
{
    public string Name { get; set; } = "";
    public string? Type { get; set; }
    public string? Description { get; set; }
    public string? DefaultValue { get; set; }
    public bool? Required { get; set; }
}

public sealed class ParameterRemoveParams
{
    public string Name { get; set; } = "";
}

public sealed class ParameterListResult
{
    public Dictionary<string, ParameterDefDto> Parameters { get; set; } = new();
}

public sealed class ParameterDefDto
{
    public string Type { get; set; } = "string";
    public string? Description { get; set; }
    public object? Default { get; set; }
    public bool Required { get; set; }
    public int? Order { get; set; }
}
