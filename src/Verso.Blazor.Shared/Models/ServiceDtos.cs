using Verso.Abstractions;

namespace Verso.Blazor.Shared.Models;

/// <summary>Describes a cell type available for creation or switching.</summary>
public sealed record CellTypeInfo(string Id, string DisplayName);

/// <summary>Describes a toolbar action with its metadata.</summary>
public sealed record ToolbarActionInfo(
    string ActionId,
    string DisplayName,
    string? Icon,
    ToolbarPlacement Placement,
    int Order);

/// <summary>Describes a layout engine available for the notebook.</summary>
public sealed record LayoutInfo(
    string LayoutId,
    string DisplayName,
    bool RequiresCustomRenderer);

/// <summary>Describes a theme available for the notebook.</summary>
public sealed record ThemeInfo(
    string ThemeId,
    string DisplayName,
    ThemeKind ThemeKind);

/// <summary>Full theme data for rendering CSS variables.</summary>
public sealed record ThemeData(
    ThemeColorTokens Colors,
    ThemeTypography Typography,
    ThemeSpacing Spacing);

/// <summary>Result of a hover info request.</summary>
public sealed record HoverResultDto(string Content, HoverRangeDto? Range);

/// <summary>Range within editor text.</summary>
public sealed record HoverRangeDto(int StartLine, int StartColumn, int EndLine, int EndColumn);

/// <summary>Result of a completions request.</summary>
public sealed record CompletionsResultDto(IReadOnlyList<CompletionItemDto> Items);

/// <summary>A single completion item.</summary>
public sealed record CompletionItemDto(
    string DisplayText,
    string InsertText,
    string? Kind,
    string? Description,
    string? SortText);

/// <summary>Result of a cell execution.</summary>
public sealed record ExecutionResultDto(
    Guid CellId,
    string Status,
    TimeSpan Elapsed);

/// <summary>Variable entry for the variable explorer.</summary>
public sealed record VariableEntryDto(
    string Name,
    string TypeName,
    string ValuePreview,
    bool IsExpandable);

/// <summary>Result of inspecting a variable.</summary>
public sealed record VariableInspectResultDto(
    string Name,
    string TypeName,
    string MimeType,
    string Content);

/// <summary>Setting definition grouped by extension.</summary>
public sealed record ExtensionSettingsGroup(
    string ExtensionId,
    IReadOnlyList<SettingDefinition> Definitions);
