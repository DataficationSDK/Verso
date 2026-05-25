using System.Text.Json;
using Verso.Abstractions;

namespace Verso.Blazor.Shared.Models;

/// <summary>Describes a cell type available for creation or switching.</summary>
public sealed record CellTypeInfo(string Id, string DisplayName);

/// <summary>Describes a kernel language available for code cells.</summary>
public sealed record KernelLanguageInfo(string Id, string DisplayName, bool SupportsCancellation = true);

/// <summary>Describes a toolbar action with its metadata.</summary>
public sealed record ToolbarActionInfo(
    string ActionId,
    string DisplayName,
    string? Icon,
    ToolbarPlacement Placement,
    int Order);

/// <summary>
/// Payload of a <c>layout/updated</c> host-to-client notification. Carries the
/// identity pair, the originating renderer instance, and a scope describing what
/// the client should re-fetch.
/// </summary>
public sealed record LayoutUpdatedEventArgs(
    string ExtensionId,
    string LayoutId,
    string FrameInstanceId,
    string Scope,
    Guid? CellId);

/// <summary>
/// Payload of a <c>layout/frameMessage</c> host-to-client notification, used to
/// forward extension push messages from <c>ILayoutFrameChannel.PostMessageAsync</c>
/// into the matching isolated-layout iframe. The <c>Type</c> field already carries
/// the <c>ext/</c> prefix applied by the host channel.
/// </summary>
public sealed record LayoutFrameMessageEventArgs(
    string FrameInstanceId,
    string Type,
    JsonElement? Payload);

/// <summary>
/// Payload of a per-cell <c>output/update</c> notification. Carries the cell id
/// and the raw outputs array element so subscribers can re-broadcast without
/// having to re-deserialize the cell list.
/// </summary>
public sealed record CellOutputUpdatedEventArgs(
    Guid CellId,
    JsonElement Outputs);

/// <summary>Describes a layout engine available for the notebook.</summary>
public sealed record LayoutInfo(
    string LayoutId,
    string DisplayName,
    bool RequiresCustomRenderer,
    LayoutCapabilities Capabilities = LayoutCapabilities.CellInsert | LayoutCapabilities.CellDelete
        | LayoutCapabilities.CellReorder | LayoutCapabilities.CellEdit | LayoutCapabilities.CellResize
        | LayoutCapabilities.CellExecute | LayoutCapabilities.MultiSelect,
    bool SupportsPropertiesPanel = false,
    string ExtensionId = "",
    string RendererIsolation = "inline");

/// <summary>
/// Client-facing form of a layout renderer package. Bytes are already base64-decoded.
/// Returned by <c>RemoteNotebookService.GetLayoutRendererPackageAsync</c>; <c>null</c> when
/// the layout is inline or no package is available.
/// </summary>
public sealed record LayoutRendererPackageDto(
    string EntryPoint,
    IReadOnlyDictionary<string, byte[]> Files,
    string? ContentSecurityPolicy);

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

/// <summary>Resolved theme bundle sent to iframe-isolated layout renderers.</summary>
public sealed record LayoutThemeBundle(string Kind, IReadOnlyDictionary<string, string> Tokens);

/// <summary>
/// Builds a <see cref="LayoutThemeBundle"/> from the active <see cref="ThemeData"/>.
/// The bundle's token keys use the documented dotted-name palette
/// (<c>bg.default</c>, <c>fg.default</c>, etc.); the iframe bridge maps each key
/// to a <c>--verso-{key-with-dashes}</c> CSS custom property on the iframe root.
/// </summary>
public static class LayoutThemeBundleBuilder
{
    public static LayoutThemeBundle? Build(ThemeKind? kind, ThemeData? data)
    {
        if (data is null) return null;
        var c = data.Colors;
        var t = data.Typography;
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["bg.default"]       = c.BgDefault,
            ["bg.elevated"]      = c.BgElevated,
            ["fg.default"]       = c.FgDefault,
            ["fg.muted"]         = c.FgMuted,
            ["border.default"]   = c.BorderDefault,
            ["accent"]           = c.Accent,
            ["font.family.mono"] = t.FontFamilyMono,
            ["font.family.sans"] = t.FontFamilySans,
            ["font.size.base"]   = FormatFontSize(t.FontSizeBase),
        };
        return new LayoutThemeBundle(KindToWire(kind), tokens);
    }

    public static string KindToWire(ThemeKind? k) => k switch
    {
        ThemeKind.Dark => "dark",
        ThemeKind.HighContrast => "high-contrast",
        _ => "light",
    };

    private static string FormatFontSize(double px) =>
        px.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "px";
}

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
    int ExecutionCount,
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

/// <summary>Pairs a property provider's extension ID with the section it returned.</summary>
public sealed record PropertySectionResult(
    string ProviderExtensionId,
    PropertySection Section);
