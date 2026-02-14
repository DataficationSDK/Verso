namespace Verso.Host.Dto;

// --- Variable Explorer ---

public sealed class VariableListResult
{
    public List<VariableEntryDto> Variables { get; set; } = new();
}

public sealed class VariableEntryDto
{
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string ValuePreview { get; set; } = "";
    public bool IsExpandable { get; set; }
}

public sealed class VariableInspectParams
{
    public string Name { get; set; } = "";
}

public sealed class VariableInspectResult
{
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string MimeType { get; set; } = "text/plain";
    public string Content { get; set; } = "";
}
