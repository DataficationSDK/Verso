namespace Verso.Host.Dto;

// --- Notebook ---

public sealed class NotebookOpenParams
{
    public string Content { get; set; } = "";
    public string? FilePath { get; set; }
    public string? WorkingDir { get; set; }

    /// <summary>Single extensions directory (retained for back-compat with older clients).</summary>
    public string? ExtensionsDirectory { get; set; }

    /// <summary>Multiple extensions directories to scan, in order.</summary>
    public List<string>? ExtensionsDirectories { get; set; }
}

public sealed class NotebookOpenResult
{
    public string NotebookId { get; set; } = "";
    public string? Title { get; set; }
    public List<CellDto> Cells { get; set; } = new();
    public string? DefaultKernel { get; set; }
}

public sealed class NotebookCloseParams
{
    public string NotebookId { get; set; } = "";
}

public sealed class NotebookSetFilePathParams
{
    public string? FilePath { get; set; }
}

public sealed class NotebookSetDefaultKernelParams
{
    public string KernelId { get; set; } = "";
}

public sealed class NotebookSaveResult
{
    public string Content { get; set; } = "";
}

public sealed class NotebookDiffParams
{
    /// <summary>Full text of the baseline notebook file (.verso, .ipynb, or .dib).</summary>
    public string BaselineContent { get; set; } = "";

    /// <summary>Baseline file path, used to pick the right serializer by extension.</summary>
    public string? BaselineFilePath { get; set; }

    /// <summary>Display label for the baseline (e.g. "Git: HEAD").</summary>
    public string BaselineLabel { get; set; } = "Baseline";
}

public sealed class CellTypesResult
{
    public List<CellTypeDto> CellTypes { get; set; } = new();
}

public sealed class CellTypeDto
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsEditable { get; set; } = true;
}

public sealed class LanguagesResult
{
    public List<LanguageDto> Languages { get; set; } = new();
}

public sealed class LanguageDto
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool SupportsCancellation { get; set; } = true;
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
    public bool IconOnly { get; set; }
    public bool IsPrimary { get; set; }
    public string? ConfirmationPrompt { get; set; }
}

// --- Cell ---

public sealed class CellDto
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "code";
    public string? Language { get; set; }
    public string Source { get; set; } = "";
    public List<CellOutputDto> Outputs { get; set; } = new();
    public Dictionary<string, object>? Metadata { get; set; }
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

public sealed class CellChangeLanguageParams
{
    public string CellId { get; set; } = "";
    public string Language { get; set; } = "";
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

    // Whether this execution changed the saved document. False for cell types whose outputs are
    // transient (re-rendered on open), so auto-rendering them on open does not mark the file edited.
    public bool Dirty { get; set; } = true;
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
    /// <summary>The layout's <see cref="Verso.Abstractions.ILayoutEngine.LayoutId"/>.</summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// The <see cref="Verso.Abstractions.IExtension.ExtensionId"/> of the extension that owns
    /// this layout. Together with <see cref="Id"/> forms the qualified identity pair every
    /// JSON-RPC consumer should use from v1.0 onward.
    /// </summary>
    public string ExtensionId { get; set; } = "";

    public string DisplayName { get; set; } = "";
    public string? Icon { get; set; }
    public bool RequiresCustomRenderer { get; set; }

    /// <summary>
    /// Rendering isolation model declared by the layout: <c>"inline"</c> (default) or
    /// <c>"isolated"</c>. Isolated layouts are served via <c>layout/getRendererPackage</c>;
    /// inline layouts produce their content through <c>layout/render</c>.
    /// </summary>
    public string RendererIsolation { get; set; } = "inline";

    public bool IsActive { get; set; }
    public int Capabilities { get; set; }
    public bool SupportsPropertiesPanel { get; set; }
}

public sealed class LayoutSwitchParams
{
    public string LayoutId { get; set; } = "";

    /// <summary>
    /// The extension id that owns the target layout. Required from v1.0 onward; absent
    /// callers fall through the legacy resolution path with a deprecation warning logged
    /// to the diagnostic channel. Removed entirely in v2.0.
    /// </summary>
    public string? ExtensionId { get; set; }
}

public sealed class LayoutRenderParams
{
    /// <summary>
    /// Cell ids (as strings) of heading cells whose sections are collapsed, so a custom layout can
    /// fold the cells beneath them. Null or empty when nothing is collapsed.
    /// </summary>
    public List<string>? CollapsedSections { get; set; }
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

public sealed class LayoutGetRendererPackageParams
{
    public string NotebookId { get; set; } = "";
    public string ExtensionId { get; set; } = "";
    public string LayoutId { get; set; } = "";
}

public sealed class LayoutGetRendererPackageResult
{
    /// <summary>Relative path of the entry module within <see cref="Files"/>.</summary>
    public string EntryPoint { get; set; } = "";

    /// <summary>
    /// Bundle files keyed by relative path. Values are base64-encoded byte arrays so the
    /// JSON-RPC transport can carry binary payloads without modification.
    /// </summary>
    public Dictionary<string, string> Files { get; set; } = new();

    /// <summary>Optional Content Security Policy hint.</summary>
    public string? ContentSecurityPolicy { get; set; }

    /// <summary>
    /// Optional host-renderer protocol version the bundle was built against. <c>null</c>
    /// when the renderer does not declare one.
    /// </summary>
    public string? RendererProtocolVersion { get; set; }
}

public sealed class LayoutGetStaticAssetsParams
{
    public string NotebookId { get; set; } = "";
    public string ExtensionId { get; set; } = "";
    public string LayoutId { get; set; } = "";

    /// <summary>
    /// IANA media types the host can load as layout-scoped static assets
    /// (e.g. <c>"text/css"</c>). The engine is expected to return only assets whose
    /// content type appears here; the host filters defensively on receipt.
    /// </summary>
    public List<string> SupportedAssetContentTypes { get; set; } = new();

    /// <summary>
    /// IANA media types the host can render from the engine (e.g. <c>"text/html"</c>).
    /// Reserved for a future render-format negotiation.
    /// </summary>
    public List<string> SupportedRenderFormats { get; set; } = new();
}

public sealed class LayoutStaticAssetDto
{
    /// <summary>Stable, layout-relative asset key (e.g. <c>"dashboard.css"</c>).</summary>
    public string AssetId { get; set; } = "";

    /// <summary>IANA media type of the asset.</summary>
    public string ContentType { get; set; } = "";

    /// <summary>
    /// Asset bytes, base64-encoded so the JSON-RPC transport can carry them without
    /// modification.
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>
    /// Optional load-order and timing hints. Consulted only for script content types.
    /// </summary>
    public LayoutStaticAssetLoadHintsDto? LoadHints { get; set; }

    /// <summary>
    /// Optional Content Security Policy hint. Carried verbatim through the contract;
    /// current Blazor hosts do not enforce a strict CSP and ignore this field at runtime.
    /// </summary>
    public string? ContentSecurityPolicy { get; set; }
}

public sealed class LayoutStaticAssetLoadHintsDto
{
    /// <summary>"classic" or "module". Defaults to "classic" when omitted.</summary>
    public string? ModuleKind { get; set; }

    /// <summary>"defer", "async", or "blocking". Defaults to "defer" when omitted.</summary>
    public string? LoadMode { get; set; }

    /// <summary>"beforeLayoutHtml" or "afterLayoutHtml". Defaults to "afterLayoutHtml".</summary>
    public string? Placement { get; set; }
}

public sealed class LayoutGetStaticAssetsResult
{
    public List<LayoutStaticAssetDto> Assets { get; set; } = new();
}

public sealed class LayoutAllocateFrameInstanceParams
{
    public string NotebookId { get; set; } = "";
    public string ExtensionId { get; set; } = "";
    public string LayoutId { get; set; } = "";
}

public sealed class LayoutAllocateFrameInstanceResult
{
    public string FrameInstanceId { get; set; } = "";
}

public sealed class LayoutRendererMountedParams
{
    public string NotebookId { get; set; } = "";
    public string ExtensionId { get; set; } = "";
    public string LayoutId { get; set; } = "";
    public string FrameInstanceId { get; set; } = "";
}

public sealed class LayoutRendererMountedResult
{
    /// <summary>
    /// Dictionary returned by <c>ILayoutLifecycleHandler.OnRendererMountedAsync</c>,
    /// to be merged into the <c>verso/init</c> payload as the <c>extension</c> field.
    /// Null when no lifecycle handler is registered for the layout, or when the
    /// handler returned null.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Extension { get; set; }
}

public sealed class LayoutRendererUnmountedParams
{
    public string NotebookId { get; set; } = "";
    public string ExtensionId { get; set; } = "";
    public string LayoutId { get; set; } = "";
    public string FrameInstanceId { get; set; } = "";
}

public sealed class LogExtensionParams
{
    public string NotebookId { get; set; } = "";
    public string ExtensionId { get; set; } = "";
    public string LayoutId { get; set; } = "";
    public string FrameInstanceId { get; set; } = "";
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
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
