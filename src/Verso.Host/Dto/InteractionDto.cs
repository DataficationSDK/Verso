namespace Verso.Host.Dto;

// --- Cell Interaction ---

public sealed class CellInteractParams
{
    public string CellId { get; set; } = "";
    public string ExtensionId { get; set; } = "";
    public string InteractionType { get; set; } = "";
    public string Payload { get; set; } = "";
    public string? OutputBlockId { get; set; }
    public string Region { get; set; } = "";
}

public sealed class CellInteractResult
{
    public string? Response { get; set; }

    // Whether the interaction changed persisted notebook state (e.g. a parameter definition edit),
    // so the client and the VS Code host can mark the notebook as edited.
    public bool Dirty { get; set; }
}
