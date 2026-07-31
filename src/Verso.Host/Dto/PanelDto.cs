namespace Verso.Host.Dto;

// --- Extension-contributed panels ---
//
// Host panels are not carried over this wire. The client knows its own panels;
// only the extension-contributed ones have to cross the process boundary.

// panel/list

public sealed class PanelListParams
{
    /// <summary>Selected cell id, or empty when nothing is selected.</summary>
    public string? SelectedCellId { get; set; }
}

public sealed class PanelListResult
{
    public List<PanelInfoDto> Panels { get; set; } = new();
}

public sealed class PanelInfoDto
{
    public string PanelId { get; set; } = "";
    public string ExtensionId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? IconName { get; set; }
    public string? IconMarkup { get; set; }
    public int Order { get; set; }
}

// panel/render

public sealed class PanelRenderParams
{
    public string ExtensionId { get; set; } = "";
    public string PanelId { get; set; } = "";
    public string? SelectedCellId { get; set; }
}

public sealed class PanelRenderResult
{
    /// <summary>Representations of the panel's content, richest first.</summary>
    public List<PanelRepresentationDto> Representations { get; set; } = new();
}

public sealed class PanelRepresentationDto
{
    public string MimeType { get; set; } = "";
    public string Content { get; set; } = "";
}

// panel/interact

public sealed class PanelInteractParams
{
    public string ExtensionId { get; set; } = "";
    public string PanelId { get; set; } = "";
    public string InteractionType { get; set; } = "";
    public string Payload { get; set; } = "";
    public string? TargetId { get; set; }
    public string? SelectedCellId { get; set; }
}
