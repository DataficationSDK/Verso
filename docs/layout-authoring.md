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

### HTML Conventions

Follow these conventions for front-end integration:

1. **`data-cell-id` attributes**: Every cell container must include a `data-cell-id` attribute with the cell's GUID for the front-end to identify interactive elements:

```html
<div class="my-layout-cell" data-cell-id="a1b2c3d4-...">
    <!-- cell content -->
</div>
```

2. **`data-action` attributes**: Use `data-action` on interactive elements for the front-end to handle clicks:

```html
<button data-action="run" data-cell-id="...">Run</button>
<button data-action="prev-slide">Previous</button>
```

3. **CSS class naming**: Prefix all CSS classes with `verso-` followed by your layout name:

```html
<div class="verso-dashboard-grid">
    <div class="verso-dashboard-cell">
    <div class="verso-dashboard-resize-handle">
```

4. **Output rendering**: Use the standard output pattern for cell outputs:

```csharp
if (output.IsError)
    sb.Append($"<div class=\"verso-output verso-output--error\">{escaped}</div>");
else if (output.MimeType == "text/html")
    sb.Append($"<div class=\"verso-output verso-output--html\">{output.Content}</div>");
else
    sb.Append($"<div class=\"verso-output verso-output--text\"><pre>{escaped}</pre></div>");
```

Always HTML-encode user content with `WebUtility.HtmlEncode()` to prevent XSS.

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
