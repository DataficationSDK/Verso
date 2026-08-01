using System.Text.Json;
using Verso.Abstractions;

namespace Verso.Blazor.Shared.Models;

/// <summary>Describes a cell type available for creation or switching.</summary>
public sealed record CellTypeInfo(string Id, string DisplayName, bool IsEditable = true);

/// <summary>Describes a kernel language available for code cells.</summary>
public sealed record KernelLanguageInfo(string Id, string DisplayName, bool SupportsCancellation = true);

/// <summary>Describes a toolbar action with its metadata.</summary>
public sealed record ToolbarActionInfo(
    string ActionId,
    string DisplayName,
    string? Icon,
    ToolbarPlacement Placement,
    int Order,
    bool IconOnly = false,
    bool IsPrimary = false,
    string? ConfirmationPrompt = null,
    string? Description = null);

/// <summary>
/// Describes one entry in the notebook's panel list, whether it is drawn by the host
/// itself or contributed by an extension through <see cref="INotebookPanel"/>.
/// </summary>
/// <param name="PanelId">Identifier, unique within the owning extension.</param>
/// <param name="ExtensionId">Owning extension, or empty for a host panel.</param>
/// <param name="DisplayName">Title and accessible name.</param>
/// <param name="IconName">Icon-set name the host maps to a glyph, or <c>null</c>.</param>
/// <param name="IconMarkup">Host-specific icon markup used when present and understood.</param>
/// <param name="Order">Sort order among the panel controls, lower first.</param>
/// <param name="IsHostPanel">
/// <c>true</c> when the host draws this panel with its own component rather than
/// rendering content produced by an extension.
/// </param>
/// <param name="Description">
/// Optional sentence shown beneath the name in the toggle's tooltip, or <c>null</c>
/// to show the name alone.
/// </param>
public sealed record NotebookPanelInfo(
    string PanelId,
    string ExtensionId,
    string DisplayName,
    string? IconName,
    string? IconMarkup,
    int Order,
    bool IsHostPanel,
    string? Description = null)
{
    /// <summary>
    /// Stable key for a panel across both kinds, since a host panel's id and an
    /// extension panel's id share a namespace only when the extension id is included.
    /// </summary>
    public string Key => IsHostPanel ? PanelId : $"{ExtensionId}::{PanelId}";
}

/// <summary>
/// Payload of <c>INotebookService.OnPanelUpdated</c>, raised when a panel's content
/// has changed and the host should ask for it again.
/// </summary>
public sealed record PanelUpdatedEventArgs(string ExtensionId, string PanelId);

/// <summary>
/// Health of the kernel connection as observed by the host. <see cref="Faulted"/> means the
/// kernel infrastructure failed (e.g. a restart threw); <see cref="Disconnected"/> means the
/// host process or transport backing the kernel is gone. Both are recoverable by restarting.
/// </summary>
public enum KernelHealth
{
    Ok,
    Faulted,
    Disconnected,
}

/// <summary>
/// Payload of <c>INotebookService.OnKernelHealthChanged</c>. <paramref name="Detail"/> carries
/// an optional human-readable explanation (exception message, process exit description) that
/// the UI can surface as a tooltip.
/// </summary>
public sealed record KernelHealthChangedEventArgs(KernelHealth Health, string? Detail = null);

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
    string? ContentSecurityPolicy,
    string? RendererProtocolVersion = null);

/// <summary>
/// One CSS (or other media-type) asset declared by a layout, resolved to a URL the host
/// component can drop into a <c>&lt;link rel="stylesheet"&gt;</c> tag. The Server host
/// resolves to a static-files endpoint URL; the WASM host resolves to a <c>blob:</c> URL
/// produced by JS interop. The Razor component is agnostic to the resolution strategy.
/// </summary>
public sealed record LayoutStaticAssetDescriptor(
    string AssetId,
    string ContentType,
    string Url)
{
    /// <summary>
    /// Load-order and timing hints for script assets. Null applies host defaults
    /// (classic script, defer load mode, after-layout placement).
    /// </summary>
    public LayoutStaticAssetLoadHints? LoadHints { get; init; }

    /// <summary>
    /// Optional Content Security Policy hint carried verbatim from the layout engine.
    /// Current hosts do not enforce a strict CSP and ignore this field at runtime.
    /// </summary>
    public string? ContentSecurityPolicy { get; init; }
}

/// <summary>Describes a theme available for the notebook.</summary>
public sealed record ThemeInfo(
    string ThemeId,
    string DisplayName,
    ThemeKind ThemeKind);

/// <summary>Full theme data for rendering CSS variables.</summary>
/// <remarks>
/// <paramref name="Elevation"/> is optional so a host that predates the elevation scale
/// still produces valid theme data; consumers coalesce a null to the defaults.
/// </remarks>
public sealed record ThemeData(
    ThemeColorTokens Colors,
    ThemeTypography Typography,
    ThemeSpacing Spacing,
    ThemeElevation? Elevation = null);

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
        var s = data.Spacing;
        var e = data.Elevation ?? new ThemeElevation();
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["bg.default"]       = c.BgDefault,
            ["bg.elevated"]      = c.BgElevated,
            ["bg.sunken"]        = c.BgSunken,
            ["fg.default"]       = c.FgDefault,
            ["fg.muted"]         = c.FgMuted,
            ["fg.subtle"]        = c.FgSubtle,
            ["border.default"]   = c.BorderDefault,
            ["accent"]           = c.Accent,
            ["accent.foreground"] = c.AccentForeground,
            ["font.family.mono"] = t.FontFamilyMono,
            ["font.family.sans"] = t.FontFamilySans,
            ["font.size.base"]   = FormatFontSize(t.FontSizeBase),
            // Shape and elevation travel with the palette so an isolated renderer can
            // match the host's corners and depth instead of guessing at them.
            ["shape.small"]      = FormatPx(s.ShapeSmall),
            ["shape.medium"]     = FormatPx(s.ShapeMedium),
            ["shape.large"]      = FormatPx(s.ShapeLarge),
            ["shape.full"]       = FormatPx(s.ShapeFull),
            ["elevation.0"]      = e.Level0,
            ["elevation.1"]      = e.Level1,
            ["elevation.2"]      = e.Level2,
            ["elevation.3"]      = e.Level3,
        };
        return new LayoutThemeBundle(KindToWire(kind), tokens);
    }

    public static string KindToWire(ThemeKind? k) => k switch
    {
        ThemeKind.Dark => "dark",
        ThemeKind.HighContrast => "high-contrast",
        _ => "light",
    };

    private static string FormatFontSize(double px) => FormatPx(px);

    private static string FormatPx(double px) =>
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

/// <summary>
/// A package returned by an extension marketplace search. <see cref="IsInstalled"/> reflects
/// whether the package is already recorded in the open notebook's required extensions.
/// </summary>
public sealed record PackageSearchResultDto(
    string Id,
    string? Version,
    string? Description,
    string? Authors,
    long? DownloadCount,
    string? IconUrl,
    string? ProjectUrl,
    bool IsInstalled);

/// <summary>The outcome of installing an extension package.</summary>
public sealed record PackageInstallResultDto(
    bool Success,
    string? ResolvedVersion,
    string? ErrorMessage,
    int ExtensionsRegistered);

/// <summary>
/// A package this notebook requires (recorded in its required-extensions list), surfaced so the
/// extension panel can list and uninstall it independently of NuGet search. <paramref name="IsLocal"/>
/// is true for files sideloaded from disk, which never appear in NuGet search results.
/// <paramref name="UnavailableReason"/> is non-null when the package was required but failed to load
/// (not on disk, source unreachable, consent denied), carrying a short explanation for the UI.
/// <paramref name="Capabilities"/> is what the package contributed once loaded, which cannot be known
/// from a package feed and so is only ever available for an installed package.
/// <paramref name="IconDataUri"/> is the package's own icon, read from the copy on disk rather than
/// fetched, so it costs no network request and works for sideloaded packages no feed knows about.
/// </summary>
public sealed record InstalledExtensionDto(
    string Id,
    string? Version,
    bool IsLocal,
    string? UnavailableReason = null,
    IReadOnlyList<string>? Capabilities = null,
    string? IconDataUri = null)
{
    /// <summary>
    /// True when this required extension did not load and the panel should flag it. See
    /// <see cref="UnavailableReason"/> for why.
    /// </summary>
    public bool IsUnavailable => UnavailableReason is not null;

    /// <summary>
    /// True when the package loaded and registered no extension point at all, so installing it
    /// changed nothing. Distinct from <see cref="Capabilities"/> being null, which only means
    /// the package has not loaded in this session and nothing is known yet.
    /// </summary>
    public bool ContributesNothing => Capabilities is { Count: 0 };
}

/// <summary>
/// How a host lets the user pick a local extension file (a <c>.dll</c> or <c>.nupkg</c>) to
/// sideload. The extension panel uses this to decide whether to show its "load from file"
/// affordance and which acquisition path to drive.
/// </summary>
public enum LocalExtensionPickMode
{
    /// <summary>Sideloading a local file is not available in this host.</summary>
    None,

    /// <summary>The user picks a file in the browser; its bytes are uploaded to the server (Blazor Server).</summary>
    Upload,

    /// <summary>The host shows a native open dialog and reads the chosen file from disk (VS Code).</summary>
    NativeBrowse,
}

/// <summary>
/// A baseline a notebook can be compared against in the diff view. Well-known ids are
/// "lastSaved", "gitHead", "gitRef", and "file"; <paramref name="Kind"/> groups them as
/// "lastSaved", "git", or "file" for hosts that render sources by category.
/// </summary>
public sealed record DiffSourceInfo(
    string Id,
    string Label,
    string Kind,
    bool Available,
    string? Description = null);
