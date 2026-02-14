namespace Verso.Host.Dto;

// --- Extension Management ---

public sealed class ExtensionListResult
{
    public List<ExtensionInfoDto> Extensions { get; set; } = new();
}

public sealed class ExtensionInfoDto
{
    public string ExtensionId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "";
    public List<string> Capabilities { get; set; } = new();
}

public sealed class ExtensionToggleParams
{
    public string ExtensionId { get; set; } = "";
}
