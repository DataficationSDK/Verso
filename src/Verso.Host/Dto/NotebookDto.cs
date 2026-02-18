namespace Verso.Host.Dto;

// --- Notebook ---

public sealed class NotebookOpenParams
{
    public string Content { get; set; } = "";
    public string? FilePath { get; set; }
}

public sealed class NotebookOpenResult
{
    public string? Title { get; set; }
    public List<CellDto> Cells { get; set; } = new();
    public string? DefaultKernel { get; set; }
}

public sealed class NotebookSetFilePathParams
{
    public string? FilePath { get; set; }
}

public sealed class NotebookSaveResult
{
    public string Content { get; set; } = "";
}

public sealed class CellTypesResult
{
    public List<CellTypeDto> CellTypes { get; set; } = new();
}

public sealed class CellTypeDto
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public sealed class LanguagesResult
{
    public List<LanguageDto> Languages { get; set; } = new();
}

public sealed class LanguageDto
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public sealed class ToolbarActionsResult
{
    public List<ToolbarActionDto> Actions { get; set; } = new();
}

public sealed class ToolbarActionDto
{
    public string ActionId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Icon { get; set; }
    public string Placement { get; set; } = "";
    public int Order { get; set; }
}

// --- Cell ---

public sealed class CellDto
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "code";
    public string? Language { get; set; }
    public string Source { get; set; } = "";
    public List<CellOutputDto> Outputs { get; set; } = new();
}

public sealed class CellOutputDto
{
    public string MimeType { get; set; } = "";
    public string Content { get; set; } = "";
    public bool IsError { get; set; }
    public string? ErrorName { get; set; }
    public string? ErrorStackTrace { get; set; }
}

public sealed class CellAddParams
{
    public string Type { get; set; } = "code";
    public string? Language { get; set; }
    public string Source { get; set; } = "";
}

public sealed class CellInsertParams
{
    public int Index { get; set; }
    public string Type { get; set; } = "code";
    public string? Language { get; set; }
    public string Source { get; set; } = "";
}

public sealed class CellRemoveParams
{
    public string CellId { get; set; } = "";
}

public sealed class CellMoveParams
{
    public int FromIndex { get; set; }
    public int ToIndex { get; set; }
}

public sealed class CellUpdateSourceParams
{
    public string CellId { get; set; } = "";
    public string Source { get; set; } = "";
}

public sealed class CellChangeTypeParams
{
    public string CellId { get; set; } = "";
    public string Type { get; set; } = "code";
}

public sealed class CellGetParams
{
    public string CellId { get; set; } = "";
}

// --- Execution ---

public sealed class ExecutionRunParams
{
    public string CellId { get; set; } = "";
}

public sealed class ExecutionResultDto
{
    public string CellId { get; set; } = "";
    public string Status { get; set; } = "";
    public int ExecutionCount { get; set; }
    public double ElapsedMs { get; set; }
    public List<CellOutputDto> Outputs { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public sealed class ExecutionStateNotification
{
    public string CellId { get; set; } = "";
    public string State { get; set; } = ""; // "running" | "completed" | "failed" | "cancelled"
}

// --- Kernel ---

public sealed class KernelRestartParams
{
    public string? KernelId { get; set; }
}

public sealed class CompletionsParams
{
    public string CellId { get; set; } = "";
    public string Code { get; set; } = "";
    public int CursorPosition { get; set; }
}

public sealed class CompletionsResult
{
    public List<CompletionDto> Items { get; set; } = new();
}

public sealed class CompletionDto
{
    public string DisplayText { get; set; } = "";
    public string InsertText { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? Description { get; set; }
    public string? SortText { get; set; }
}

public sealed class DiagnosticsParams
{
    public string CellId { get; set; } = "";
    public string Code { get; set; } = "";
}

public sealed class DiagnosticsResult
{
    public List<DiagnosticDto> Items { get; set; } = new();
}

public sealed class DiagnosticDto
{
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public string? Code { get; set; }
}

public sealed class HoverParams
{
    public string CellId { get; set; } = "";
    public string Code { get; set; } = "";
    public int CursorPosition { get; set; }
}

public sealed class HoverResult
{
    public string? Content { get; set; }
    public string MimeType { get; set; } = "text/plain";
    public RangeDto? Range { get; set; }
}

public sealed class RangeDto
{
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
}

// --- Layout ---

public sealed class LayoutsResult
{
    public List<LayoutDto> Layouts { get; set; } = new();
}

public sealed class LayoutDto
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Icon { get; set; }
    public bool RequiresCustomRenderer { get; set; }
    public bool IsActive { get; set; }
}

public sealed class LayoutSwitchParams
{
    public string LayoutId { get; set; } = "";
}

public sealed class LayoutRenderResult
{
    public string Html { get; set; } = "";
}

public sealed class LayoutGetCellContainerParams
{
    public string CellId { get; set; } = "";
}

public sealed class LayoutGetCellContainerResult
{
    public int Row { get; set; }
    public int Col { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class LayoutUpdateCellParams
{
    public string CellId { get; set; } = "";
    public int Row { get; set; }
    public int Col { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class LayoutSetEditModeParams
{
    public bool EditMode { get; set; }
}

// --- Theme ---

public sealed class ThemeResult
{
    public string ThemeId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ThemeKind { get; set; } = "";
    public Dictionary<string, string> Colors { get; set; } = new();
    public Dictionary<string, string> SyntaxColors { get; set; } = new();
    public ThemeTypographyDto Typography { get; set; } = new();
    public ThemeSpacingDto Spacing { get; set; } = new();
}

public sealed class ThemeTypographyDto
{
    public FontDto EditorFont { get; set; } = new();
    public FontDto UIFont { get; set; } = new();
    public FontDto ProseFont { get; set; } = new();
    public FontDto CodeOutputFont { get; set; } = new();
}

public sealed class FontDto
{
    public string Family { get; set; } = "";
    public double SizePx { get; set; }
    public int Weight { get; set; } = 400;
    public double LineHeight { get; set; } = 1.4;
}

public sealed class ThemeSpacingDto
{
    public double CellPadding { get; set; }
    public double CellGap { get; set; }
    public double ToolbarHeight { get; set; }
    public double SidebarWidth { get; set; }
    public double ContentMarginHorizontal { get; set; }
    public double ContentMarginVertical { get; set; }
    public double CellBorderRadius { get; set; }
    public double ButtonBorderRadius { get; set; }
    public double OutputPadding { get; set; }
    public double ScrollbarWidth { get; set; }
}

public sealed class ThemesResult
{
    public List<ThemeListItemDto> Themes { get; set; } = new();
}

public sealed class ThemeListItemDto
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ThemeKind { get; set; } = "";
    public bool IsActive { get; set; }
}

// --- Toolbar ---

public sealed class ToolbarGetEnabledStatesParams
{
    public string Placement { get; set; } = "";
    public List<string> SelectedCellIds { get; set; } = new();
}

public sealed class ToolbarGetEnabledStatesResult
{
    public Dictionary<string, bool> States { get; set; } = new();
}

public sealed class ToolbarExecuteParams
{
    public string ActionId { get; set; } = "";
    public List<string> SelectedCellIds { get; set; } = new();
}
