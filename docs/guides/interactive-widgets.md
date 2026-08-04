# Interactive Widgets

A widget is a control that draws in the browser and has a kernel behind it. Move the slider and the Python that made it hears about it, runs whatever the author wired to it, and can send something back. Libraries built on `ipywidgets` all work this way, and so does anything built with `anywidget`.

This guide covers what makes a widget live, what a saved file holds, what happens with no network, and how to share a widget's value with the other languages in the notebook.

## A first widget

```python
#!pip ipywidgets
```

```python
import ipywidgets as widgets

slider = widgets.IntSlider(value=20, min=0, max=100, description="Threshold")
seen = []

def remember(change):
    seen.append(change["new"])

slider.observe(remember, names="value")
slider
```

Drag it, then run a later cell:

```python
print(slider.value, seen)
```

The value is what you left it at, and `seen` holds every value it passed through. Nothing was re-run to make that true.

## Live and static

A widget is in one of two states, and the difference is whether there is a kernel listening.

| | Live | Static |
|---|---|---|
| What it is | A control with the interpreter that made it still behind it | A picture of the widget as it was, that still moves |
| Where you get it | The editor, whether served by `verso serve` or opened in VS Code, once the cell has been run | `verso run`, `verso export`, a file just reopened, a kernel that has gone away |
| Dragging it | Reaches Python, fires the author's callbacks, and can change other cells | Redraws in the browser and stops there |

Both states draw the same widget from the same state, so a static one is not a broken one. A three-dimensional plot still rotates and a map still pans, because that happens entirely in the browser.

A static widget says so. It is drawn a little quieter than a live one and carries a line beneath it reading "Showing saved state. Run the cell to make this widget live." Running the cell replaces it with a live one.

## What a saved file holds

A saved notebook holds the widget's state and nothing else. No connection, no session, no claim to be live. A `.verso` file with widgets in it opens and draws on a machine with no Python installed at all.

The state it holds is the state the reader last saw, not the state the cell drew. Save after dragging a slider to 98 and the file says 98. This is the useful behaviour and it is worth knowing about, because it means saving a notebook you have been poking at records the poking.

Widget state makes a file bigger. A five thousand point scatter adds roughly 90 KB each time the cell is run and saved. Each widget also carries the state of every other one still alive in the session, so two plots cost roughly twice what one does; giving a single plot several objects avoids paying twice for the same data. A widget carrying more data than a notebook should reasonably hold, currently 4 MB for one widget's page, is refused with a message rather than written to disk.

## The network

The state travels in the notebook, but the JavaScript that draws it comes from a public CDN when the widget is shown. A machine with no network draws an empty frame, and the rest of the notebook is unaffected.

This surprises people most with `anywidget`, because an `anywidget`'s own JavaScript travels inline in the widget state. The code is right there in the file and still does not run, because the loader that runs it is the part that was not fetched.

## anywidget

`anywidget` widgets work the same as any other, live and static, and need nothing configured:

```python
#!pip anywidget
```

```python
import anywidget, traitlets

class Counter(anywidget.AnyWidget):
    _esm = """
    export default {
      render({ model, el }) {
        const button = document.createElement("button");
        const draw = () => { button.textContent = `count is ${model.get("value")}`; };
        button.addEventListener("click", () => {
          model.set("value", model.get("value") + 1);
          model.save_changes();
        });
        model.on("change:value", draw);
        draw();
        el.appendChild(button);
      }
    };
    """
    value = traitlets.Int(0).tag(sync=True)

counter = Counter()
counter
```

Click it three times and `counter.value` is 3 in the next cell.

`anywidget.experimental.command` works too. It is built on the same messages the rest of a widget uses, so a front end calling into Python and getting an answer back needs nothing beyond a live widget.

## Sharing a trait with other kernels

A widget's value can become a notebook variable, which is what puts a control in front of a computation written in another language.

```
#!bind <expression>.<trait> [as <name>]
#!bind --list
#!bind --remove <name>
```

Given the slider from the top of this guide:

```
#!bind slider.value as threshold
```

```
'threshold' now follows slider.value. Any kernel can read it, and writing it moves the widget.
```

From then on `threshold` is an ordinary shared variable. A C# cell reads it the way it reads any other:

```csharp
var cutoff = Variables.Get<long>("threshold");
readings.Where(r => r > cutoff)
```

Dragging the slider changes what that cell computes on its next run. Writing the variable moves the slider:

```csharp
Variables.Set("threshold", 70L);
```

The widget moves on the page, the author's `observe` callbacks fire, and every other kernel sees 70. A value settles after one round trip in whichever direction it started, so nothing bounces between the two sides.

`<expression>` is anything that names the object in the Python interpreter, so `controls[0].value` and `readings["gas"].value` both work. The variable name defaults to the trait's own name, so `#!bind slider.value` shares it as `value`.

### What to know

**The object has to exist already.** A magic command runs before the rest of its own cell, so `#!bind` names a widget an earlier cell built. Binding in the same cell that creates the widget finds nothing, and says so.

**It is not only for widgets.** What the command asks of an object is whether it has a trait by that name and whether it will call back when the trait changes. Any `traitlets.HasTraits` object answers both, so a plain observable object binds the same way a widget does.

**A trait holding another widget is refused.** `layout` and `style` are the two most likely to be tried, and both hold widgets. A widget is a live object rather than data, so bind one of its traits instead:

```
#!bind slider.layout.width as sliderWidth
```

**So is a value too large to keep re-sending.** A projected value crosses on every change, which for a dragged control is many times a second, so the ceiling is lower than the one a cell's whole scope is published under. A large array is refused with a reason naming what it was; a small one crosses as the numbers in it.

**A projection lasts as long as the interpreter.** Restarting Python leaves the shared name holding the value it last had rather than taking it away, because the cells reading it are not written to cope with a name that disappears. Re-running the `#!bind` line reconnects it.

**Listing and removing.**

```
#!bind --list
```

```
Widget traits shared as variables:
  threshold  <-  slider.value
```

```
#!bind --remove threshold
```

```
'threshold' no longer follows a widget trait. It keeps the value it last held.
```

Removing stops the two from following each other. It does not delete the variable, so a cell that was reading it keeps working on the last value.

Binding the same trait a second time replaces the first rather than watching it twice, whether or not you rename it.

### What crosses

A projected trait goes through the same path as any Python variable, so what survives is what survives for variables generally.

| Trait holds | Other kernels see |
|---|---|
| `Int`, `Float`, `Bool`, `Unicode` | `long`, `double`, `bool`, `string` |
| `List`, `Tuple` | a list |
| `Dict` | a dictionary keyed by string |
| `Bytes` | a byte array |
| `Datetime` | a date and time |
| A widget | Refused, with the reason |
| Anything over the size a projected value may occupy | Refused, with the reason |

See [Language Kernels](language-kernels.md) for the full picture of what a value looks like as it crosses between languages.

## Limits

| Limit | Value | What happens |
|---|---|---|
| One widget's saved page | 4 MB | The cell shows a message instead of the widget |
| One message between a widget and its kernel | 8 MB | The message is refused with a diagnostic naming the widget; the session continues |
| One projected value | 1 MB | The bind is refused, or a later change keeps the value that crossed before it |

A widget sending more than the transport will carry is refused at the point it is produced, so an oversized update is something the author can act on rather than a kernel that stopped answering.

## What is not supported

`ipywidgets.Output` captures what is displayed inside a `with` block by talking to an IPython shell, and there is no shell here, so the block captures nothing. Libraries that use the pattern and then display the empty area alongside the real figure get the empty one left out rather than drawn as a blank box.

## Exporting

Exporting a notebook to HTML keeps its widgets, drawn from their saved state and static, under the same network condition as everything else on this page.

## See also

- [Language Kernels](language-kernels.md) and [Python Packages](python-packages.md)
- [Python Interpreters](python-interpreters.md)
- [CLI Reference](cli-reference.md)
