# Panels

A panel sits alongside the notebook body and is opened from a toggle in the toolbar. Verso ships five of its own (Metadata, Extensions, Variables, Settings, Cell Properties) and an extension can add more by implementing `INotebookPanel`.

Reach for a panel when the thing you want to show belongs *next to* the notebook rather than inside a cell: a summary of the whole document, a list you act on, a view that outlives any single cell's output.

## A panel describes content, it does not draw it

This is the one idea the rest of the API follows from.

In some hosts your extension does not run in the same process as the UI. In the VS Code extension, for example, your code runs in an out-of-process host while the interface is a WebAssembly app in the webview. Everything a panel contributes crosses a process boundary, so it has to be data. There is no arrangement in which you hand the host a live component.

So a panel returns **representations**: the same content expressed one or more ways, ordered richest first. Each host renders the first one whose media type it understands.

```csharp
public Task<IReadOnlyList<RenderResult>> RenderAsync(IPanelContext context)
{
    IReadOnlyList<RenderResult> representations = new[]
    {
        new RenderResult("text/html", BuildHtml(context)),
        new RenderResult("text/plain", BuildText(context))
    };
    return Task.FromResult(representations);
}
```

**No host is obliged to understand `text/html`.** A panel that offers only markup is simply unavailable anywhere that does not draw markup. A plain-text alternative usually costs a few lines and is the difference between a panel that works in one place and one that works everywhere.

## A minimal panel

```csharp
[VersoExtension]
public sealed class FindingsPanel : INotebookPanel
{
    public string ExtensionId => "com.example.findings";
    public string Name => "Findings";
    public string Version => "1.0.0";
    public string? Author => "Example";
    public string? Description => "Review findings for the current notebook.";

    public string PanelId => "findings";
    public string DisplayName => "Findings";
    public string? IconName => "flag";
    public int Order => 600;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<bool> IsAvailableAsync(IPanelContext context)
        => Task.FromResult(true);

    public Task<IReadOnlyList<RenderResult>> RenderAsync(IPanelContext context)
    {
        IReadOnlyList<RenderResult> representations = new[]
        {
            new RenderResult("text/plain", $"{context.NotebookCells.Count} cells")
        };
        return Task.FromResult(representations);
    }
}
```

### Identity

A panel is identified by the `(ExtensionId, PanelId)` pair. Two extensions may both use `PanelId` `"findings"` without colliding. One extension declaring `"findings"` twice fails to load with `PANEL_ID_DUPLICATE_IN_EXTENSION`.

### Ordering

`Order` places the toggle among the host's panel controls, lower first. The built-in panels occupy 100 through 500 in steps of 100, so a value above 500 puts your panel after them and a value in between slots it among them.

### Availability

`IsAvailableAsync` decides whether the panel is offered at all. Returning `false` removes its toggle and closes it if it was open, which is how a panel disappears when it has nothing to say.

The host calls it when it builds the panel list: on notebook load, and whenever the layout or the set of loaded extensions changes. It is **not** called on every cell selection. A panel whose content depends on the selection should stay available and read `context.SelectedCellId` inside `RenderAsync`, which the host does call when the selection changes.

## Icons

`IconName` names an icon rather than supplying one, so hosts that do not draw SVG can still show something. Hosts are expected to recognize:

`document` · `list` · `puzzle` · `braces` · `gear` · `search` · `layout` · `compare` · `info` · `warning` · `flag` · `check` · `clock` · `tag` · `chart` · `table` · `folder` · `link`

Resolution order is `IconName`, then `IconMarkup`, then the first letter of `DisplayName`. A name a host does not recognize falls back rather than failing, so naming an icon is always safe.

`IconMarkup` is the escape hatch for a custom icon. Hosts that understand the markup use it; others fall back. In the web host it is inline SVG, sized to a 16 by 16 viewBox.

```csharp
public string? IconName => "flag";
public string? IconMarkup =>
    "<svg viewBox=\"0 0 16 16\" width=\"15\" height=\"15\" fill=\"currentColor\">...</svg>";
```

### Say what the panel contains

A toggle is an icon at rest, so an icon and a one-word name are all a reader has before deciding whether to open the panel. Hosts show `Description` beneath `DisplayName` in the toggle's tooltip, which is the one place to fix that.

```csharp
public string DisplayName => "Findings";
public string? Description => "Rule violations found in the current notebook.";
```

Say what the panel shows. A description that restates the name spends the reader's attention without repaying it.

## Responding to actions

Implement `IPanelInteractionHandler` to receive actions the user triggers in your panel. The usual arrangement is one class implementing both interfaces.

```csharp
public Task OnPanelInteractionAsync(PanelInteractionContext context)
{
    if (context.InteractionType == "accept" && context.TargetId is { } id)
    {
        Accept(id);
        context.RequestRefresh();
    }
    return Task.CompletedTask;
}
```

`RequestRefresh()` tells the host to discard what the panel is showing and ask for it again. Without it the panel keeps displaying whatever it last rendered, which is a common first bug.

`PanelInteractionContext` names no rendering technology. It tells you *what* was triggered (`InteractionType`), *which item* it applies to (`TargetId`), and any accompanying data (`Payload`). How a host noticed is its own business.

Interaction types beginning `verso/` are reserved by the host and never reach your handler.

A handler whose extension registers no panel with a matching `PanelId` fails to load with `PANEL_HANDLER_ORPHANED`.

## Writing panel markup for the web host

Everything below applies only to hosts that render `text/html`. It is host guidance, not part of the abstraction.

### Wiring actions

The web host delegates from the panel root. Any element carrying `data-panel-action` reports that action when clicked, and `data-target-id` says which item it applies to:

```html
<button class="verso-panel-action"
        data-panel-action="accept"
        data-target-id="f-17">Accept</button>
```

Both arrive on `PanelInteractionContext`. For anything more than a name and a target, put JSON in `data-panel-payload` and read `context.Payload`.

A `change` event on a value-bearing control (`<input>`, `<select>`) reports its value as `{"value": ...}` in the payload, so a checkbox or dropdown needs no extra wiring:

```html
<input type="checkbox" data-panel-action="toggle-filter" data-target-id="errors" />
```

Mark a subtree `data-panel-passive` to stop it triggering an action it sits inside, which is what you want for a link or a block of selectable text inside an actionable row.

### Class vocabulary

These classes are built entirely on the theme tokens, so markup that uses them follows the active theme without knowing which one it is. You are free to write your own CSS instead; you then keep up with theme changes yourself.

| Class | Use |
|---|---|
| `verso-panel-section` | A grouped block. Carries the surface and elevation. |
| `verso-panel-section-title` | Small uppercase heading inside a section. |
| `verso-panel-row` | One item. Add `verso-panel-row--interactive` when the whole row is clickable. |
| `verso-panel-secondary` | De-emphasized text, for metadata and attribution. |
| `verso-panel-chip` | Small status pill. Tones: `--info`, `--success`, `--warning`, `--error`. |
| `verso-panel-action` | Button. Add `--primary` for the filled accent style. |
| `verso-panel-actions` | Right-aligned container for one or more buttons in a row. |
| `verso-panel-empty` | The nothing-to-show state. |

Putting them together:

```html
<div class="verso-panel-section">
  <div class="verso-panel-section-title">Open findings</div>
  <div class="verso-panel-row">
    <span>
      <b>The disposable is never cleared.</b>
      <span class="verso-panel-secondary">Raised 2 days ago.</span>
    </span>
    <span class="verso-panel-chip verso-panel-chip--warning">open</span>
    <span class="verso-panel-actions">
      <button class="verso-panel-action verso-panel-action--primary"
              data-panel-action="accept" data-target-id="f-17">Accept</button>
      <button class="verso-panel-action"
              data-panel-action="flag" data-target-id="f-17">Flag</button>
    </span>
  </div>
</div>
```

### Escaping

Panel markup goes into the host's document as written. Anything derived from notebook content, variable values, or file paths must be escaped on the way out. The host does not sanitize it for you.

```csharp
private static string Escape(string value)
    => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
```

### What panels do not get

Panels cannot ship their own stylesheets or scripts. Style with the classes above and the `--verso-*` custom properties; if you need behavior, route it through `IPanelInteractionHandler` and re-render.

This is a deliberate limit, not an oversight. The static-asset pipeline that layouts use carries content types, script module kinds, and a content security policy, all of which assume a browser. Keeping it out of panels is what lets a panel mean something on a host that is not one.

## Testing

`IsAvailableAsync` and `RenderAsync` take an `IPanelContext`, which `Verso.Testing` can stand in for. Assert on the representations rather than on rendered output:

```csharp
var representations = await panel.RenderAsync(context);

Assert.AreEqual("text/html", representations[0].MimeType);
StringAssert.Contains(representations[0].Content, "data-panel-action=\"accept\"");
```

For the interaction half, build a `PanelInteractionContext` directly and pass a `RequestRefresh` that records whether it fired:

```csharp
var refreshed = false;
await panel.OnPanelInteractionAsync(new PanelInteractionContext
{
    ExtensionId = panel.ExtensionId,
    PanelId = panel.PanelId,
    InteractionType = "accept",
    TargetId = "f-17",
    Verso = context,
    RequestRefresh = () => refreshed = true
});

Assert.IsTrue(refreshed);
```

## See Also

- **[Extension Interfaces](extension-interfaces.md)** — the member-level reference for `INotebookPanel` and `IPanelInteractionHandler`.
- **[Layouts](layouts.md)** — for changing how the notebook body itself is arranged, rather than adding something beside it.
- **[Theme Authoring](theme-authoring.md)** — the tokens the panel classes are built on.
