# Layout Authoring Guide

This guide explains how to create custom layout engines for Verso notebooks. Layouts control how cells are arranged, displayed, and interacted with in the notebook UI.

## Introduction

A Verso layout engine implements the `ILayoutEngine` interface and defines how notebook cells are spatially arranged. The platform ships two built-in layouts (`NotebookLayout` for linear top-to-bottom, `DashboardLayout` for grid-based dashboards) and supports third-party layouts loaded via the extension system.

A layout renderer runs in one of two isolation modes. **Inline** layouts (the default) supply HTML that the host injects into its own page and style against shared CSS variables; everything up to [Complete Examples](#complete-examples) describes this mode. **Isolated** layouts ship a renderer module that the host runs inside a sandboxed iframe with its own DOM, scripts, and styles, bridged to the host over a message contract. See [Isolated (iframe) layouts](#isolated-iframe-layouts) for that mode.

Layouts handle:
- Cell arrangement and positioning
- Visual rendering of the layout container
- Cell lifecycle events (add, remove, move)
- Metadata persistence for saving/restoring layout state

## Quick Start

1. Create a new extension project:

```bash
dotnet new verso-extension -n MyLayout --extensionId com.mycompany.mylayout
```

2. Add a class implementing `ILayoutEngine` with the `[VersoExtension]` attribute:

```csharp
using Verso.Abstractions;

[VersoExtension]
public sealed class KanbanLayout : ILayoutEngine
{
    public string ExtensionId => "com.mycompany.kanban";
    public string Name => "Kanban Layout";
    public string Version => "1.0.0";
    public string? Author => "Your Name";
    public string? Description => "Kanban board layout for notebook cells.";

    public string LayoutId => "kanban";
    public string DisplayName => "Kanban";
    public string? Icon => null;
    public bool RequiresCustomRenderer => true;

    public LayoutCapabilities Capabilities =>
        LayoutCapabilities.CellInsert |
        LayoutCapabilities.CellDelete |
        LayoutCapabilities.CellReorder |
        LayoutCapabilities.CellEdit |
        LayoutCapabilities.CellExecute;

    // ... implement all ILayoutEngine methods
}
```

3. Reference only `Verso.Abstractions` in your project:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/Verso.Abstractions.csproj" />
</ItemGroup>
```

## Capability Flags

`LayoutCapabilities` is a `[Flags]` enum that declares what operations your layout supports. The front-end uses these flags to enable or disable UI controls.

| Flag | Value | Description |
|------|-------|-------------|
| `None` | 0 | No capabilities (read-only layout) |
| `CellInsert` | 1 | Users can add new cells |
| `CellDelete` | 2 | Users can delete cells |
| `CellReorder` | 4 | Users can drag/move cells |
| `CellEdit` | 8 | Users can edit cell content |
| `CellResize` | 16 | Users can resize cells within the layout |
| `CellExecute` | 32 | Users can execute cells |
| `MultiSelect` | 64 | Multiple cells can be selected simultaneously |

Combine flags with bitwise OR:

```csharp
public LayoutCapabilities Capabilities =>
    LayoutCapabilities.CellInsert | LayoutCapabilities.CellDelete |
    LayoutCapabilities.CellEdit | LayoutCapabilities.CellExecute;
```

### Dynamic Capabilities

Capabilities can change at runtime. For example, `DashboardLayout` adds insert/delete/reorder only in edit mode:

```csharp
public LayoutCapabilities Capabilities
{
    get
    {
        var caps = LayoutCapabilities.CellResize | LayoutCapabilities.CellExecute;
        if (_isEditMode)
            caps |= LayoutCapabilities.CellInsert | LayoutCapabilities.CellDelete;
        return caps;
    }
}
```

## RequiresCustomRenderer

| Value | Behavior |
|-------|----------|
| `false` | The front-end renders cells individually using the standard cell-by-cell pipeline. Your layout only provides positioning via `GetCellContainerAsync`. |
| `true` | Your layout provides a complete HTML rendering via `RenderLayoutAsync`. The front-end injects this HTML into a webview panel. |

Use `RequiresCustomRenderer = false` for simple layouts where standard cell rendering suffices (like `NotebookLayout`). Use `true` when you need complete control over the visual output (like `DashboardLayout` or `PresentationLayout`).

## Cell Container Positioning

`GetCellContainerAsync` returns a `CellContainerInfo` record describing a cell's position and size:

```csharp
public sealed record CellContainerInfo(
    Guid CellId,
    double X,       // Horizontal offset in DIPs
    double Y,       // Vertical offset in DIPs
    double Width,   // Container width in DIPs
    double Height,  // Container height in DIPs
    bool IsVisible  // Whether the cell is rendered
);
```

The coordinate system is layout-dependent:
- **NotebookLayout**: X=0, Y=sequential offset, Width=800, Height=120
- **DashboardLayout**: X=grid column, Y=grid row, Width/Height in grid units
- **PresentationLayout**: X=0, Y=0, Width=1024, Height=768, IsVisible based on current slide

The `IsVisible` property controls whether the front-end renders the cell at all. This is useful for layouts like presentations where only one slide's cells should be visible.

## RenderLayoutAsync

When `RequiresCustomRenderer` is `true`, implement `RenderLayoutAsync` to return the complete layout HTML:

```csharp
public Task<RenderResult> RenderLayoutAsync(
    IReadOnlyList<CellModel> cells,
    IVersoContext context)
```

The host wraps your returned HTML in a `<div class="verso-layout-root">` automatically and mounts it into the active notebook view. Two complementary mechanisms then connect your layout to the host:

- **Cell slots** let you arrange existing cell components without re-rendering them yourself. The host mounts a real `<Cell>` component into every `[data-cell-slot]` placeholder you emit.
- **Data-attribute routing** lets buttons, inputs, and selects inside your HTML send interaction events back to your extension with no JavaScript on your side.

The sections below cover identity, slots, routing, the interaction handler, the re-render protocol, and how to style against the host theme. Always HTML-encode user-supplied content with `WebUtility.HtmlEncode()` to prevent XSS.

### HTML Conventions

Prefix CSS classes with `verso-` followed by your layout name to avoid clashing with host or other-extension styles:

```html
<div class="verso-dashboard-grid">
    <div class="verso-dashboard-cell">
    <div class="verso-dashboard-resize-handle">
```

Use the standard output pattern when rendering cell outputs:

```csharp
if (output.IsError)
    sb.Append($"<div class=\"verso-output verso-output--error\">{escaped}</div>");
else if (output.MimeType == "text/html")
    sb.Append($"<div class=\"verso-output verso-output--html\">{output.Content}</div>");
else
    sb.Append($"<div class=\"verso-output verso-output--text\"><pre>{escaped}</pre></div>");
```

## Layout Identity

Every layout has a two-part identity: `(ExtensionId, LayoutId)`. `LayoutId` is required to be unique within an extension but not globally. Two third-party extensions may both ship a `kanban` layout without conflict. The registry, notebook metadata, JSON-RPC surface, and DOM data attributes all carry both halves.

The single source of truth for the layout's `ExtensionId` is the `IExtension.ExtensionId` of the class implementing `ILayoutEngine`. Interaction handlers do not declare a target extension; their own `ExtensionId` (inherited from `IExtension`) defines the scope, and a handler may only target a layout owned by the same extension.

Registration is validated when an assembly loads. The following diagnostics surface load failures:

| Diagnostic | Trigger |
|---|---|
| `LAYOUT_ID_DUPLICATE_IN_EXTENSION` | Two `ILayoutEngine` classes in the same extension declare the same `LayoutId`. |
| `LAYOUT_INTERACTION_DUPLICATE` | Two `ILayoutInteractionHandler` classes in the same extension declare the same `LayoutId`. |
| `LAYOUT_HANDLER_ORPHANED` | An interaction handler declares a `LayoutId` for which the same extension does not also register an `ILayoutEngine`. |

Registry lookups always require both halves; there is no fallback search by bare `LayoutId`.

## Embedding Cells with `data-cell-slot`

A custom layout does not render cell internals itself. Instead it emits placeholder slot elements, and the host mounts the real `<Cell>` Blazor components into them from a hidden cell pool:

```html
<div class="my-layout-grid">
  <div class="my-layout-cell-slot"
       data-cell-slot="00000000-0000-0000-0000-000000000001">
    <!-- Cell component is mounted here by the host -->
  </div>
</div>
```

When the host encounters a `[data-cell-slot]` element it locates the matching cell in the pool by its GUID and mounts the `<Cell>` component into the slot. The cell continues to participate in execution, parameter editing, output streaming, and `cell/interact` exactly as it would in the default notebook layout.

The host wraps your `RenderLayoutAsync` output in `<div class="verso-layout-root" data-extension-id="..." data-layout-id="...">` automatically, so you typically do not need to add those attributes on the outermost element. You may add them on inner scopes if a portion of your layout should act as an interaction root with different identity.

## Event Routing

A global event delegator intercepts clicks, changes, and keydowns on any element with a `[data-action]` attribute inside your rendered layout, then routes the event back to your extension.

### Data attributes

| Attribute | Role |
|---|---|
| `data-action` | Names the interaction type (e.g. `set-mode`, `parameter-update`). Required on any interactive element. |
| `data-cell-id` | Scopes the event to a specific cell. Required to route to `cell/interact`. |
| `data-extension-id` | Identifies the owning extension. Must be present on a `[data-cell-id]` or `[data-layout-id]` ancestor for the event to route. |
| `data-layout-id` | Marks an element as a layout interaction root. Must co-locate with `data-extension-id` on the same ancestor. |
| `data-payload` | Optional string sent as the interaction's `Payload`. Inputs, selects, and checkboxes fall back to the element's `value` (or `checked` state) when `data-payload` is absent. |
| `data-target-id` | Optional. Echoed back unchanged in `LayoutInteractionContext.TargetId`, typically the id of a specific sub-control. |

### Routing rules

Given an event whose target carries `[data-action]`:

| Target ancestry | Routes to |
|---|---|
| Target has a `[data-cell-id]` ancestor inside a `[data-extension-id]` ancestor | `cell/interact` for that cell |
| Target has a `[data-layout-id]` ancestor (with no `[data-cell-id]` between them) | `layout/interact` for that layout |
| Target has neither | Silently ignored |

If `data-layout-id` and `data-extension-id` are on different ancestors of the same target, the router logs a warning and drops the event. The two must be co-located on the same element.

### Worked example

A workbench layout that toggles all cells between two execution modes:

```csharp
[VersoExtension]
public sealed class DotNetWorkbenchLayout : ILayoutEngine, ILayoutInteractionHandler
{
    private string _mode = "csharp";

    public string ExtensionId => "com.mycompany.dotnet-workbench";
    public string Version => "1.0.0";
    public string LayoutId => "dotnet-workbench";
    public string DisplayName => ".NET Workbench";
    public bool RequiresCustomRenderer => true;

    public LayoutCapabilities Capabilities =>
        LayoutCapabilities.CellExecute | LayoutCapabilities.CellEdit;

    public Task<RenderResult> RenderLayoutAsync(
        IReadOnlyList<CellModel> cells, IVersoContext ctx)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"workbench-toolbar\">");
        sb.Append($"<button data-action=\"set-mode\" data-payload=\"csharp\" " +
                  $"class=\"{(_mode == "csharp" ? "active" : "")}\">C#</button>");
        sb.Append($"<button data-action=\"set-mode\" data-payload=\"fsharp\" " +
                  $"class=\"{(_mode == "fsharp" ? "active" : "")}\">F#</button>");
        sb.Append("</div>");
        sb.Append("<div class=\"workbench-cells\">");
        foreach (var cell in cells)
            sb.Append($"<div data-cell-slot=\"{cell.Id}\"></div>");
        sb.Append("</div>");
        return Task.FromResult(new RenderResult("text/html", sb.ToString()));
    }

    public Task OnLayoutInteractionAsync(LayoutInteractionContext context)
    {
        if (context.InteractionType == "set-mode")
        {
            _mode = context.Payload;
            context.RequestRender();
        }
        return Task.CompletedTask;
    }

    public Dictionary<string, object> GetLayoutMetadata() =>
        new() { ["mode"] = _mode };

    public Task ApplyLayoutMetadata(Dictionary<string, object> m, IVersoContext _)
    {
        if (m.TryGetValue("mode", out var v) && v is string s) _mode = s;
        return Task.CompletedTask;
    }

    // Other ILayoutEngine members elided.
}
```

The toolbar buttons emit `data-action="set-mode"` with `data-payload="csharp"` or `"fsharp"`. Because the buttons sit inside the host-injected `[data-layout-id]` wrapper but outside any `[data-cell-id]` ancestor, the router sends them to `layout/interact`, which lands in `OnLayoutInteractionAsync`. The handler updates `_mode` and asks the host to re-render. No JavaScript, no inline scripts, no iframe.

## ILayoutInteractionHandler

`ILayoutInteractionHandler` is a sibling capability interface to `ILayoutEngine`. It receives `layout/interact` events for a layout owned by the same extension:

```csharp
public interface ILayoutInteractionHandler : IExtension
{
    string LayoutId { get; }
    Task OnLayoutInteractionAsync(LayoutInteractionContext context);
}
```

The handler may live in the same class as the layout engine (as in the worked example above) or in a separate class with the same `LayoutId`. Either way, the handler's owning extension must also register the matching `ILayoutEngine`. Cross-extension handler attachment is not supported.

`LayoutInteractionContext` carries:

| Field | Purpose |
|---|---|
| `ExtensionId`, `LayoutId` | Identity of the target layout. |
| `FrameInstanceId` | Host-allocated opaque id for the live renderer mount. Stable for the lifetime of that mount. |
| `InteractionType` | The string from the originating element's `data-action`. |
| `Payload` | The string from `data-payload`, or the element's `value` for inputs and selects. |
| `TargetId` | The optional `data-target-id`, echoed back unchanged. |
| `RequestRender` | Invoke to ask the host to re-fetch `layout/render` and replace the HTML. |
| `RequestCellRefresh(cellId)` | Invoke to refresh a single cell container's position. |
| Notebook accessors | Standard `IVersoContext` access to cells, variables, and the extension host. |

## Re-render Protocol

After your interaction handler returns, the host does not refresh the UI by default. Call one of the context methods to request an update:

- `context.RequestRender()` — the host re-fetches `layout/render` and replaces the HTML in place. Use this for any change that affects the layout's visible structure.
- `context.RequestCellRefresh(cellId)` — the host re-fetches `layout/getCellContainer` for that cell and updates only that container's position and size. Other DOM is untouched.

Both translate to a `layout/updated` notification with `scope: "full" | "cell" | "metadata"`. Clients receive the notification and act accordingly.

The full-render path diffs the new HTML against the existing DOM via Blazor's renderer. DOM identity is not preserved across renders, so do not rely on element references from one render in the next. Client-side state lives in attributes and form values that your extension serializes into each render's output.

## Theming

Layouts inherit the host page's CSS automatically. The host publishes a documented palette of CSS custom properties on `:root` that you should style against rather than hardcoding colors or fonts:

| Variable | Purpose |
|---|---|
| `--verso-bg-default` | Default page background. |
| `--verso-bg-elevated` | Elevated surface background (toolbars, cards). |
| `--verso-fg-default` | Default foreground color. |
| `--verso-fg-muted` | Muted / secondary foreground. |
| `--verso-border-default` | Default border color. |
| `--verso-accent` | Accent / brand color. |
| `--verso-font-family-mono` | Monospace font stack. |
| `--verso-font-family-sans` | Sans-serif font stack. |
| `--verso-font-size-base` | Base font size in pixels. |

When the user switches themes, the host updates these variables on `:root` and your HTML re-styles automatically. No interaction handler call is required. Style your layout inline or in a single `<style>` block:

```html
<style>
  .my-layout-toolbar {
    background: var(--verso-bg-elevated);
    color: var(--verso-fg-default);
    border-bottom: 1px solid var(--verso-border-default);
    font-family: var(--verso-font-family-sans);
  }
  .my-layout-toolbar button.active {
    color: var(--verso-accent);
  }
</style>
```

For authors of theme extensions (which set the values these variables resolve to), see [Theme Authoring](theme-authoring.md).

## Cell Lifecycle Notifications

The layout engine receives notifications when cells are added, removed, or moved. Use these to maintain internal state:

### OnCellAddedAsync

Called when a new cell is inserted at the given index. Assign a default position:

```csharp
public Task OnCellAddedAsync(Guid cellId, int index, IVersoContext context)
{
    _positions[cellId] = FindDefaultPosition();
    return Task.CompletedTask;
}
```

### OnCellRemovedAsync

Called when a cell is deleted. Clean up the associated state:

```csharp
public Task OnCellRemovedAsync(Guid cellId, IVersoContext context)
{
    _positions.Remove(cellId);
    return Task.CompletedTask;
}
```

### OnCellMovedAsync

Called when a cell is reordered to a new index. Update internal ordering if your layout uses cell indices:

```csharp
public Task OnCellMovedAsync(Guid cellId, int newIndex, IVersoContext context)
{
    // Only needed if your layout uses cell order for positioning
    return Task.CompletedTask;
}
```

## Metadata Persistence

Layouts persist their state through `GetLayoutMetadata()` and `ApplyLayoutMetadata()`. This allows layout state to survive save/load cycles.

### GetLayoutMetadata

Return a dictionary of serializable values describing the current layout state:

```csharp
public Dictionary<string, object> GetLayoutMetadata()
{
    if (_positions.Count == 0)
        return new Dictionary<string, object>();

    var cells = new Dictionary<string, object>();
    foreach (var (id, pos) in _positions)
    {
        cells[id.ToString()] = new Dictionary<string, object>
        {
            ["x"] = pos.X,
            ["y"] = pos.Y,
            ["width"] = pos.Width
        };
    }

    return new Dictionary<string, object>
    {
        ["version"] = 1,
        ["cells"] = cells
    };
}
```

### ApplyLayoutMetadata with JsonElement Handling

When metadata is deserialized from JSON, values may arrive as `JsonElement` instead of CLR types. Always handle both:

```csharp
public Task ApplyLayoutMetadata(Dictionary<string, object> metadata, IVersoContext context)
{
    if (!metadata.TryGetValue("cells", out var cellsObj))
        return Task.CompletedTask;

    Dictionary<string, object>? cellsDict = null;

    if (cellsObj is Dictionary<string, object> dict)
        cellsDict = dict;
    else if (cellsObj is JsonElement je && je.ValueKind == JsonValueKind.Object)
    {
        cellsDict = new Dictionary<string, object>();
        foreach (var prop in je.EnumerateObject())
            cellsDict[prop.Name] = prop.Value;
    }

    if (cellsDict is null) return Task.CompletedTask;

    foreach (var (key, value) in cellsDict)
    {
        if (!Guid.TryParse(key, out var cellId)) continue;

        // Handle both Dictionary<string, object> and JsonElement
        if (value is Dictionary<string, object> posDict)
        {
            // Direct dictionary access
        }
        else if (value is JsonElement posEl && posEl.ValueKind == JsonValueKind.Object)
        {
            // JsonElement property access
        }
    }

    return Task.CompletedTask;
}
```

This dual-path handling is critical. See `DashboardLayout.cs` and `PresentationLayout.cs` for complete examples.

## Visibility and Cell Properties

### SupportedVisibilityStates

Declare which `CellVisibilityState` values your layout handles. The built-in `CellVisibilityPropertyProvider` uses this to render per-layout visibility dropdowns in the properties panel. Only layouts that support more than `{ Visible }` will appear.

```csharp
public IReadOnlySet<CellVisibilityState> SupportedVisibilityStates
    => new HashSet<CellVisibilityState>
    {
        CellVisibilityState.Visible,
        CellVisibilityState.Hidden,
        CellVisibilityState.OutputOnly
    };
```

Available states:

| State | Description |
|-------|-------------|
| `Visible` | Show the full cell (input and output). |
| `Hidden` | Hide the cell entirely. |
| `OutputOnly` | Show only the cell's output area. |
| `Collapsed` | Show the cell in a collapsed/summary state. |

### SupportsPropertiesPanel

Set this to `true` to enable the cell properties sidebar when your layout is active:

```csharp
public bool SupportsPropertiesPanel => true;
```

The front-end checks this flag to conditionally show or hide the properties panel. This defaults to `false`, so layouts that do not opt in will not display the panel.

### Using CellVisibilityResolver

When rendering cells, use `CellVisibilityResolver` to resolve per-cell visibility from metadata and cell type defaults:

```csharp
foreach (var cell in cells)
{
    var renderer = context.ExtensionHost.GetRenderers()
        .FirstOrDefault(r => r.CellTypeId == cell.Type);
    if (renderer is null) continue;

    var state = CellVisibilityResolver.Resolve(
        cell, renderer, LayoutId, SupportedVisibilityStates);

    switch (state)
    {
        case CellVisibilityState.Hidden:
            continue; // skip
        case CellVisibilityState.OutputOnly:
            RenderOutputOnly(cell);
            break;
        default:
            RenderFull(cell);
            break;
    }
}
```

The resolver checks `CellModel.Metadata["verso:ui.layoutVisibility"]` for a per-layout user override first, then falls back to `ICellRenderer.DefaultVisibility`, constraining the result to your `SupportedVisibilityStates`.

## Front-End Considerations

### Blazor Server / WASM

The Blazor front-end renders layouts in a `<div>` container. When `RequiresCustomRenderer` is `true`, the raw HTML from `RenderLayoutAsync` is inserted via `@((MarkupString)html)`. Interactive elements (buttons with `data-action`) are wired up through JavaScript interop.

### VS Code Webview

In the VS Code extension, custom layouts are rendered inside a webview panel. The same HTML conventions apply, but scripts run in a sandboxed iframe.

### Key Implications

- Avoid inline `<script>` tags; use `data-action` attributes instead
- All styles should be inline or in `<style>` blocks (no external CSS)
- Keep HTML self-contained; external resource references will not resolve

## State Management and Thread Safety

Layout engines may be called from multiple threads (e.g., UI thread for rendering, background thread for cell execution). If your layout maintains mutable state:

- Use `lock` around shared state if the layout is used from multiple threads
- Keep state modifications in the lifecycle callbacks (`OnCellAdded`, `OnCellRemoved`)
- `RenderLayoutAsync` should be a pure read of current state when possible

For simple layouts, thread safety is often not a concern because the front-end serializes calls. But if your layout supports background execution or concurrent operations, add appropriate synchronization.

## Testing Layouts

Use `StubVersoContext` from `Verso.Testing` for unit tests. Key scenarios to cover:

### 1. Extension Metadata

```csharp
[TestMethod]
public void ExtensionId_IsCorrect()
    => Assert.AreEqual("com.mycompany.kanban", _layout.ExtensionId);
```

### 2. Capabilities

```csharp
[TestMethod]
public void Capabilities_HasExpectedFlags()
{
    Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellInsert));
    Assert.IsFalse(_layout.Capabilities.HasFlag(LayoutCapabilities.CellResize));
}
```

### 3. HTML Rendering

```csharp
[TestMethod]
public async Task RenderLayoutAsync_ProducesValidHtml()
{
    var cells = new List<CellModel> { new() { Source = "test" } };
    var result = await _layout.RenderLayoutAsync(cells, _context);

    Assert.AreEqual("text/html", result.MimeType);
    Assert.IsTrue(result.Content.Contains("data-cell-id"));
}
```

### 4. Cell Lifecycle

```csharp
[TestMethod]
public async Task OnCellAdded_TracksCell()
{
    var id = Guid.NewGuid();
    await _layout.OnCellAddedAsync(id, 0, _context);
    var container = await _layout.GetCellContainerAsync(id, _context);
    Assert.IsTrue(container.IsVisible);
}
```

### 5. Metadata Round-Trip

```csharp
[TestMethod]
public async Task MetadataRoundTrip_PreservesState()
{
    // Set up state
    await _layout.OnCellAddedAsync(cellId, 0, _context);

    // Serialize
    var metadata = _layout.GetLayoutMetadata();

    // Restore to new instance
    var restored = new MyLayout();
    await restored.ApplyLayoutMetadata(metadata, _context);

    // Verify state matches
    var container = await restored.GetCellContainerAsync(cellId, _context);
    Assert.IsTrue(container.IsVisible);
}
```

## JSON-RPC Surface

Layouts communicate with clients over a small set of JSON-RPC methods. Most extensions never call these directly; they are invoked by host components in response to user gestures and your `RequestRender` calls. The names are summarized here for diagnostics, custom clients, and integration tests. Canonical wire-name constants live in `MethodNames.cs` in the host protocol.

| Method | Direction | Purpose |
|---|---|---|
| `layout/getLayouts` | Client → Host | List registered layouts, each keyed by `(extensionId, layoutId)`. |
| `layout/switch` | Client → Host | Activate a layout for a notebook by qualified reference. |
| `layout/render` | Client → Host | Fetch the layout's HTML for the current cell list. |
| `layout/getCellContainer` | Client → Host | Fetch a cell's container position and visibility within the layout. |
| `layout/interact` | Client → Host | Dispatch an event to the layout's `ILayoutInteractionHandler`. |
| `layout/updated` | Host → Client | Push notification triggered by `RequestRender` / `RequestCellRefresh`. Carries `scope` of `full`, `cell`, or `metadata`. |

Two earlier methods, `layout/updateCell` and `layout/setEditMode`, remain in the protocol as deprecated forwarders. New code should use `layout/interact` with the corresponding `InteractionType` instead; the forwarders will be removed in a future major version.

## Migration from Legacy Notebooks

Notebook metadata represents the active layout as a qualified reference:

```json
{
  "metadata": {
    "activeLayout": {
      "extensionId": "verso.layout.dashboard",
      "layoutId": "dashboard"
    }
  },
  "layouts": {
    "verso.layout.dashboard:dashboard": { /* per-layout state */ },
    "com.mycompany.kanban:kanban":      { /* per-layout state */ }
  }
}
```

Older notebooks may carry a bare-string form (`"activeLayout": "dashboard"`) with per-layout state keyed by bare `LayoutId`. These continue to load:

1. The host resolves the bare string against built-in layouts first by their well-known `ExtensionId`s.
2. If exactly one loaded extension provides a layout with that `LayoutId`, the host promotes the reference and writes the qualified form on the next save.
3. If multiple loaded extensions match, the host shows a `LayoutMissing` banner and falls back to the default layout. The user resolves the ambiguity by editing the notebook metadata.
4. If no match, the existing `LayoutMissing` banner fires.

Promotion is one-way and silent on save; on-disk notebooks gradually move to qualified form without explicit migration. No action is required from extension authors to support legacy notebooks.

## Complete Examples

### NotebookLayout (Simple, No Custom Renderer)

The simplest built-in layout. Linear top-to-bottom cell arrangement with no custom rendering.

- **Source**: `src/Verso/Extensions/Layouts/NotebookLayout.cs`
- `RequiresCustomRenderer = false`
- All capabilities enabled
- No position tracking (fixed 800x120 per cell)
- Minimal metadata

### DashboardLayout (Grid, Custom Renderer)

Grid-based dashboard with drag handles, resize handles, and bin-packing position assignment.

- **Source**: `src/Verso/Extensions/Layouts/DashboardLayout.cs`
- **Tests**: `tests/Verso.Tests/Extensions/DashboardLayoutTests.cs`
- `RequiresCustomRenderer = true`
- Dynamic capabilities (edit mode toggle)
- 12-column CSS Grid rendering
- `GridPosition` record for cell placement
- Full metadata persistence with JsonElement handling

### PresentationLayout (Slides, Custom Renderer)

Slide-based presentation layout that maps cells to numbered slides with navigation.

- **Source**: `samples/SampleLayout/Verso.Sample.Slides/PresentationLayout.cs`
- **Tests**: `samples/SampleLayout/Verso.Sample.Slides.Tests/PresentationLayoutTests.cs`
- `RequiresCustomRenderer = true`
- `IsVisible` based on current slide number
- `SlideAssignment` record for cell-to-slide mapping
- Navigation controls and slide counter in rendered HTML
- Metadata round-trip with `currentSlide` and per-cell slide assignments

### Verso.Sample.Sparkline (Isolated, iframe renderer)

A minimal isolated layout that draws a sparkline on a `<canvas>` from a numeric kernel variable. It is the worked example for the [Isolated (iframe) layouts](#isolated-iframe-layouts) section below.

- **Source**: `samples/SampleLayout/Verso.Sample.Sparkline/SparklineLayout.cs`
- **Renderer**: `samples/SampleLayout/Verso.Sample.Sparkline/assets/main.js`
- **Tests**: `samples/SampleLayout/Verso.Sample.Sparkline.Tests/SparklineLayoutTests.cs`
- `RendererIsolation = Isolated`; single self-contained `main.js` (no external script, so no extra CSP)
- `ILayoutLifecycleHandler` subscribes to the `series` variable and pushes updates into the live frame
- `ILayoutInteractionHandler` handles a `select-point` interaction and writes the chosen index to `selectedPoint`
- Reads `--verso-*` theme tokens so the chart tracks the active theme

## Isolated (iframe) layouts

Everything above describes **inline** layouts: your `RenderLayoutAsync` HTML is injected into the host's own page, shares its DOM and CSS, and routes events through host-provided delegation. That is the right default for toolbars, grids, and mode toggles.

An **isolated** layout instead ships a renderer module that the host runs inside a sandboxed iframe with its own DOM, its own scripts, and its own styles. Reach for it when you need arbitrary JavaScript, a third-party visualization library, a private DOM, or CSS that must not leak into (or inherit unexpectedly from) the host page. Examples: a charting dashboard with brushing, a custom code surface, a 3D scene.

### Inline vs isolated

| Property | Inline | Isolated |
|---|---|---|
| Extension code | C# only | C# + a JS renderer bundle |
| Script isolation | None (shares the host page realm) | Full (sandboxed iframe) |
| Style isolation | None (inherits host CSS) | Full |
| Reuse host `<Cell>` components | Yes, via `data-cell-slot` | No; the frame renders its own content |
| Theme propagation | Automatic via `:root` CSS variables | Host sends tokens; you apply them |
| Network access | Host page's policy | None inside the frame (fetch kernel-side) |
| Suitable for | Toolbars, grids, mode toggles | Visualizations, custom editors, rich UIs |

A layout picks one mode and stays on it. Both modes share the same `ILayoutInteractionHandler` contract; only the transport differs, so your interaction handler does not know or care which mode delivered the event.

### Opting into isolation

Set `RendererIsolation` to `Isolated` and implement `GetRendererPackageAsync` to return the renderer bundle. `RenderLayoutAsync` is never consulted in this mode, so return an empty result.

```csharp
[VersoExtension]
public sealed class SparklineLayout
    : ILayoutEngine, ILayoutLifecycleHandler, ILayoutInteractionHandler
{
    public LayoutRendererIsolation RendererIsolation => LayoutRendererIsolation.Isolated;

    public Task<LayoutRendererPackage?> GetRendererPackageAsync(IVersoContext context)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["main.js"] = Encoding.UTF8.GetBytes(MainJs),
        };

        return Task.FromResult<LayoutRendererPackage?>(
            new LayoutRendererPackage(EntryPoint: "main.js", Files: files, ContentSecurityPolicy: null));
    }

    // Never called for an isolated renderer, but required by the interface.
    public Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context)
        => Task.FromResult(new RenderResult("text/html", string.Empty));

    // ... lifecycle and interaction members below ...
}
```

`LayoutRendererPackage` carries three things:

| Field | Meaning |
|---|---|
| `EntryPoint` | Relative path of the entry module within `Files` (for example `"main.js"`). |
| `Files` | The complete bundle, keyed by relative path, as `byte[]`. Must contain the entry point. |
| `ContentSecurityPolicy` | Optional CSP additions appended to the host's base policy. `null` keeps the base policy as-is. |

**Ship a self-contained entry module.** The host materializes each file as a separate blob and loads the entry point as an ES module (`import(entryBlobUrl)`). The other files' URLs are not exposed to the entry module, so a relative `import "./chart.js"` will not resolve. Bundle your renderer into a single entry file (or inline its dependencies). Embedding the script as an assembly resource, as the Sparkline sample does, keeps the extension a single deployable `.dll`.

### The renderer bridge

Before your entry module runs, the host installs a `window.verso` bridge in the frame. Your module uses it to talk to the host; it is the only channel out of the sandbox.

| Call | Purpose |
|---|---|
| `verso.ready()` | Signal that the frame finished initializing. The host then runs your lifecycle handler and replies with `verso/init`. Call it once, after you have registered `onMessage`. |
| `verso.onMessage((type, payload) => …)` | Receive host-to-frame messages. Register before calling `ready()` so you do not miss `verso/init`. |
| `verso.interact(interactionType, payload, targetId?)` | Raise a layout interaction. Routes to your `ILayoutInteractionHandler`. The host stamps the layout identity; the frame cannot spoof another layout. |
| `verso.cellInteract(cellId, interactionType, payload, options?)` | Raise a cell interaction from inside the frame. |
| `verso.executeCell(cellId)` | Request execution of a cell. |
| `verso.send(type, payload)` | Send a custom message to the host. The `verso/` prefix is reserved and rejected. |
| `verso.log(level, message)` | Write to the host's log channel. |

### Pushing data into a live frame

`ILayoutLifecycleHandler` gives you a per-mount handle on the frame. Implement it to supply authoritative initial state and to stream updates while the frame is alive.

```csharp
public Task<IDictionary<string, object>?> OnRendererMountedAsync(LayoutRendererMountContext context)
{
    var frame = context.Frame;
    var variables = context.Verso.Variables;

    // The variable-store change event does not name the changed variable, so
    // re-read the series each time and push it to the live frame.
    void PushSeries()
    {
        if (!frame.IsAlive) return;
        _ = frame.PostMessageAsync("data", new { values = ReadSeries(variables) }, context.CancellationToken);
    }

    variables.OnVariablesChanged += PushSeries;
    _unsubscribers[context.FrameInstanceId] = () => variables.OnVariablesChanged -= PushSeries;

    // Returned dictionary is delivered to the frame on `verso/init` under `extension`.
    return Task.FromResult<IDictionary<string, object>?>(new Dictionary<string, object>
    {
        ["variable"] = "series",
        ["values"] = ReadSeries(variables),
    });
}

public Task OnRendererUnmountedAsync(LayoutRendererUnmountContext context)
{
    if (_unsubscribers.TryRemove(context.FrameInstanceId, out var unsubscribe))
        unsubscribe();   // cancel the subscription so a torn-down frame is never pushed to
    return Task.CompletedTask;
}
```

Two contract details worth calling out:

- **Watch variables through the change event.** `IVariableStore` exposes `event Action OnVariablesChanged` plus `TryGet<T>` / `Get<T>`; there is no per-variable watch. Subscribe to the event and re-read the variable you care about inside the handler.
- **`PostMessageAsync` types are namespaced for you.** The host prefixes the type you pass with `ext/` before delivery, so `PostMessageAsync("data", …)` arrives in the frame as `ext/data`. Passing a `verso/`-prefixed type throws.

On the renderer side, the entry module consumes both the init payload and the live pushes:

```js
const verso = window.verso;

verso.onMessage((type, payload) => {
  switch (type) {
    case "verso/init":
      // Initial state from OnRendererMountedAsync arrives under `extension`.
      if (payload?.extension?.values) setValues(payload.extension.values);
      break;
    case "ext/data":
      // Live push from PostMessageAsync("data", { values }).
      if (payload?.values) setValues(payload.values);
      break;
    case "verso/themeChanged":
      draw();   // tokens already applied to :root; just repaint
      break;
  }
});

verso.ready();
```

### Receiving interactions

Isolated layouts use the same `ILayoutInteractionHandler` as inline layouts. The frame raises an interaction with `verso.interact(...)`; the host routes it to `OnLayoutInteractionAsync`.

```js
canvas.addEventListener("click", (event) => {
  const index = nearestIndex(event.clientX);
  verso.interact("select-point", { index, value: values[index] });
});
```

```csharp
public Task OnLayoutInteractionAsync(LayoutInteractionContext context)
{
    if (context.InteractionType == "select-point" && TryParseIndex(context.Payload, out var index))
    {
        // The frame owns its DOM and highlights the selection itself, so there is no
        // RequestRender here; we only surface the choice as a kernel variable.
        context.Verso.Variables.Set("selectedPoint", index);
    }
    return Task.CompletedTask;
}
```

Unlike inline layouts, an isolated handler usually does **not** call `RequestRender`: the frame manages its own DOM and repaints from the data you push, so a host-driven re-render is neither needed nor possible.

### Mount and unmount sequencing

```
Host mounts the frame (writes the package into a sandboxed iframe, installs the bridge)
  Frame  → Host:  verso/ready
  Host:            OnRendererMountedAsync(context with Frame)   // subscribe, return init extras
  Host   → Frame:  verso/init { …, extension: <your dict> }
  Frame is alive
    Frame → Host:  verso/interact, verso/cellInteract, verso/executeCell, verso/log
    Host  → Frame: verso/cellsChanged, verso/cellOutputs, verso/themeChanged, verso/layoutUpdated, ext/*
  Teardown (notebook close, layout switch, kernel restart, manual remount)
  Host:            OnRendererUnmountedAsync(context)            // cancel subscriptions; Frame.IsAlive → false
  Host   → Frame:  verso/dispose
  Host detaches the iframe
```

`OnRendererMountedAsync` runs once per mount, after the frame's `verso/ready` and before `verso/init`. `OnRendererUnmountedAsync` always runs before `verso/dispose`, so you get a chance to release per-frame resources.

### Message contract

Frame to host (sent through the bridge):

| Type | Payload | Purpose |
|---|---|---|
| `verso/ready` | `{ extensionId, layoutId, frameInstanceId }` | Frame finished initializing. |
| `verso/interact` | `{ interactionType, payload, targetId? }` | Routes to `ILayoutInteractionHandler`. Identity is stamped by the host. |
| `verso/cellInteract` | `{ cellId, interactionType, payload, … }` | A cell interaction originating in the frame. |
| `verso/executeCell` | `{ cellId }` | Request cell execution. |
| `verso/log` | `{ level, message }` | Log to the host channel. |

Host to frame (received in `onMessage`):

| Type | Payload | Purpose |
|---|---|---|
| `verso/init` | `{ extensionId, layoutId, frameInstanceId, cells, capabilities, theme, layoutMetadata, extension? }` | Initial state. `extension` is the dictionary your mount handler returned. |
| `verso/cellsChanged` | `{ cells }` | The notebook's cell list changed. |
| `verso/cellOutputs` | `{ cellId, outputs }` | A cell's outputs changed. |
| `verso/themeChanged` | `{ theme }` | The active theme changed. |
| `verso/layoutUpdated` | `{ scope, cellId? }` | Server-side update notification; the frame decides what to redraw. |
| `verso/dispose` | `{}` | The host is unmounting the frame. |
| `ext/<type>` | extension-defined | A push from `ILayoutFrameChannel.PostMessageAsync(type, …)`, delivered with the `ext/` prefix. |

The frame maintains its own DOM state across these events; the host never reaches into the iframe.

### Sandbox and Content Security Policy

The host mounts the iframe with `sandbox="allow-scripts"` (no `allow-same-origin`, `allow-forms`, or `allow-top-navigation`) and composes this base Content Security Policy, which your package cannot relax:

```
default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data: blob:; connect-src 'none';
```

Implications:

- **No network from inside the frame.** `connect-src 'none'` is the default. If your package adds a `connect-src`, only `'none'`, `blob:`, and `'self'` are accepted; anything else fails the mount. Fetch data kernel-side and push it in via `PostMessageAsync` or `verso/cellOutputs`.
- **Inline scripts and styles are allowed**, which is why a pure-`<canvas>` renderer like Sparkline needs no CSP additions at all and returns `ContentSecurityPolicy: null`.
- **An external library needs a hash.** If your entry module loads a third-party script, add its `script-src` hash through the package CSP; it is appended to the base policy.

### Theming isolated renderers

The host resolves the active theme to a small token bundle and applies it to the frame's `:root` as `--verso-*` custom properties, both on `verso/init` and on every `verso/themeChanged`. The tokens (dotted keys in the message, dash-cased as CSS properties) are:

| Token key | CSS property |
|---|---|
| `bg.default` | `--verso-bg-default` |
| `bg.elevated` | `--verso-bg-elevated` |
| `fg.default` | `--verso-fg-default` |
| `fg.muted` | `--verso-fg-muted` |
| `border.default` | `--verso-border-default` |
| `accent` | `--verso-accent` |
| `font.family.mono` | `--verso-font-family-mono` |
| `font.family.sans` | `--verso-font-family-sans` |
| `font.size.base` | `--verso-font-size-base` |

CSS that uses `var(--verso-accent)` re-colors automatically. A `<canvas>` does not, so read the value with `getComputedStyle(document.documentElement).getPropertyValue('--verso-accent')` and repaint when you receive `verso/themeChanged`.

### Failure modes

- **The frame never signals `verso/ready`.** After a timeout (default 10 seconds) the host treats the mount as failed, skips `OnRendererMountedAsync`, and shows an error banner with a Retry button that remounts the frame from scratch.
- **`OnRendererMountedAsync` throws.** The host logs the error and sends `verso/init` without the `extension` field, so the frame initializes with framework-only state. The unmount callback still runs at teardown.
- **Clean unmount.** On notebook close, layout switch, kernel restart, or manual remount, the host runs `OnRendererUnmountedAsync`, then sends `verso/dispose`, then detaches the iframe. `Frame.IsAlive` flips to `false` before the unmount callback returns, so an in-flight push is a no-op.

### Worked example

`samples/SampleLayout/Verso.Sample.Sparkline` exercises this entire surface in about a hundred lines of C# and a single `main.js`: isolation opt-in, a self-contained canvas renderer, a lifecycle handler that streams a kernel variable, an interaction handler that writes the selection back to the kernel, and theme-token styling. Its README has build and run instructions.

## See Also

- [Extension Interfaces](extension-interfaces.md): full `ILayoutEngine` API reference
- [Context Reference](context-reference.md): `IVersoContext` details
- [Testing Extensions](testing-extensions.md): test stubs and patterns
- [Best Practices](best-practices.md): state management, thread safety
- [Getting Started](getting-started.md): project scaffolding
