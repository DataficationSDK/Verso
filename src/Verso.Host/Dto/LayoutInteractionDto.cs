namespace Verso.Host.Dto;

// --- Layout Interaction ---

public sealed class LayoutInteractParams
{
    public string NotebookId { get; set; } = "";
    public string ExtensionId { get; set; } = "";
    public string LayoutId { get; set; } = "";
    public string FrameInstanceId { get; set; } = "";
    public string InteractionType { get; set; } = "";
    public string Payload { get; set; } = "";
    public string? TargetId { get; set; }
}
