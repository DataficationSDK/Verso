namespace Verso.Abstractions;

/// <summary>
/// Load-order and timing hints applied to a script <see cref="LayoutStaticAsset"/> by the
/// host when injecting the corresponding <c>&lt;script&gt;</c> tag. Hints are advisory:
/// hosts that do not recognize a value fall back to defaults.
/// </summary>
/// <param name="ModuleKind">
/// Whether the script is an ES module (<c>type="module"</c>) or a classic script. Module
/// scripts cannot read <c>document.currentScript</c>; module authors that need bridge
/// access should call <c>window.verso.layout.bind({...})</c> with explicit identifiers.
/// </param>
/// <param name="LoadMode">
/// Whether the script should be deferred, loaded asynchronously, or block parsing. Defaults
/// to <see cref="LayoutScriptLoadMode.Defer"/> which lets the layout HTML render first and
/// preserves source-order execution.
/// </param>
/// <param name="Placement">
/// Whether the <c>&lt;script&gt;</c> tag is rendered before or after the layout root in DOM
/// order. Defaults to <see cref="LayoutScriptPlacement.AfterLayoutHtml"/>.
/// </param>
public sealed record LayoutStaticAssetLoadHints(
    LayoutScriptModuleKind ModuleKind = LayoutScriptModuleKind.Classic,
    LayoutScriptLoadMode LoadMode = LayoutScriptLoadMode.Defer,
    LayoutScriptPlacement Placement = LayoutScriptPlacement.AfterLayoutHtml);

/// <summary>
/// Discriminates between classic scripts and ES modules.
/// </summary>
public enum LayoutScriptModuleKind
{
    /// <summary>Classic script. Default.</summary>
    Classic,

    /// <summary>ES module (<c>type="module"</c>).</summary>
    Module,
}

/// <summary>
/// Controls how the browser loads the script relative to HTML parsing.
/// </summary>
public enum LayoutScriptLoadMode
{
    /// <summary>Deferred load: fetched in parallel, executed after parsing in source order. Default.</summary>
    Defer,

    /// <summary>Async load: fetched in parallel, executed as soon as available; no ordering guarantees.</summary>
    Async,

    /// <summary>Blocking load: parsing pauses until the script is fetched and executed.</summary>
    Blocking,
}

/// <summary>
/// Controls where the <c>&lt;script&gt;</c> tag is rendered relative to the layout root.
/// </summary>
public enum LayoutScriptPlacement
{
    /// <summary>Render before the layout root, so the script can observe layout-mount events.</summary>
    BeforeLayoutHtml,

    /// <summary>Render after the layout root, so the script can immediately query the rendered DOM. Default.</summary>
    AfterLayoutHtml,
}
