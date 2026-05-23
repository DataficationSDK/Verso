# Layout Authoring Guide

This guide explains how to create custom layout engines for Verso notebooks. Layouts control how cells are arranged, displayed, and interacted with in the notebook UI.

## Introduction

A Verso layout engine implements the `ILayoutEngine` interface and defines how notebook cells are spatially arranged. The platform ships two built-in layouts (`NotebookLayout` for linear top-to-bottom, `DashboardLayout` for grid-based dashboards) and supports third-party layouts loaded via the extension system.

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

## See Also

- [Extension Interfaces](extension-interfaces.md): full `ILayoutEngine` API reference
- [Context Reference](context-reference.md): `IVersoContext` details
- [Testing Extensions](testing-extensions.md): test stubs and patterns
- [Best Practices](best-practices.md): state management, thread safety
- [Getting Started](getting-started.md): project scaffolding
