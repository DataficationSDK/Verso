# Markdown Notebooks

Verso reads and writes plain Markdown (`.md`) as a notebook format. A file stays an ordinary Markdown document: it renders on GitHub, in a static site generator, and in any editor's preview, while in Verso its fenced code blocks are executable cells.

Nothing marks the file as a notebook. There is no front matter to add and no metadata block to keep in step, so an existing Markdown file is already a notebook and a notebook written this way is still just a document to everything else that reads it.

## Opening one

| Where | How |
|-------|-----|
| VS Code | Right-click the file in the Explorer and choose **Open as Verso Notebook**, or use **Reopen Editor With...** and pick **Verso Notebook** |
| Browser | `verso serve article.md` |
| Command line | `verso run article.md` |

In VS Code the Markdown editor stays the default for `.md` files, so opening one normally still gives you the text editor. Verso is offered alongside it rather than taking over. If you would rather not see the context menu entry at all, turn off `verso.showOpenInVersoMenu`; the **Reopen Editor With...** route still works.

## How a file maps to cells

A top-level fenced code block whose language tag Verso recognizes becomes a cell. Everything else, prose and all, becomes markdown cells.

| Fence tag | Becomes |
|-----------|---------|
| `csharp`, `cs`, `c#` | C# code cell |
| `fsharp`, `fs`, `f#` | F# code cell |
| `python`, `py` | Python code cell |
| `javascript`, `js` | JavaScript code cell |
| `typescript`, `ts` | TypeScript code cell |
| `powershell`, `pwsh` | PowerShell code cell |
| `sql` | SQL cell |
| `html` | HTML cell |
| `mermaid` | Mermaid diagram cell |

Four kinds of fence are deliberately left alone and stay part of the prose:

- **Untagged fences.** A bare ``` block is how you quote expected output, a log excerpt, or a terminal transcript without it becoming something runnable.
- **Unrecognized tags.** A block tagged `yaml`, `json`, or `rust` is a code sample being shown, not a cell to execute.
- **Indented code**, the four-space form, which CommonMark treats as a code block without a language.
- **Fences nested inside quotes, lists, or other containers.** Only the document's direct children are considered, so a fenced example inside a bullet never splits the surrounding prose.

A fence tagged `markdown` is also left inline, since it is a Markdown example being displayed rather than a cell boundary.

## What saving preserves

Saving writes plain Markdown back to the same file. A `.md` notebook does not convert itself to `.verso` on save the way `.ipynb` does by default, because writing a sibling file would defeat the point of keeping the document editable in place.

Your fence style is remembered per cell and reused, so a file that uses `~~~` or four backticks keeps them, tags stay spelled the way you wrote them, and CRLF line endings survive. Blank lines between and inside blocks are left alone. The one normalization is at the file boundary: blank lines before the first block and after the last are dropped, and the file ends with a single newline. Otherwise a file read and written straight back is unchanged, which keeps the format quiet in version control.

**Cell outputs are not persisted.** Running cells produces results in the editor, and saving does not write them into the file. This is the format's one real limitation and its main attraction, depending on what you want: the file stays small and diffable and never carries stale results, but reopening it means running the cells again.

The same applies to anything else the format has nowhere to put: notebook parameters, a chosen layout or theme, extension requirements, and saved layout state. They work in the session and are not written to the file.

## Outgrowing the format

When a notebook needs persistent outputs, parameters, or a custom layout, convert it to the native format. Verso offers a **Verso** entry in the toolbar's Export menu for notebooks opened from a `.md` file, which writes a `.verso` copy carrying the cells and their current outputs. The original file is left untouched.

From the command line:

```bash
verso convert article.md --to verso
```

Going the other way turns a native notebook into a readable document, dropping its outputs:

```bash
verso convert notebook.verso --to md --output article.md
```

## When to reach for it

Markdown notebooks suit material meant to be read as much as run: tutorials, runnable documentation, lab exercises, design notes with a worked example, and articles whose code should be executable rather than merely quoted. The file reviews well in a pull request, renders wherever Markdown renders, and needs no tooling to read.

Reach for `.verso` instead when the results are the artifact: a report whose charts should be there when someone opens it, a parameterized pipeline, or a notebook using a custom layout.

## See also

- [Cell Types](cell-types.md)
- [Comparing Notebooks](comparing-notebooks.md)
- [CLI Reference](cli-reference.md)
- [Migrating from Jupyter](../migration/from-jupyter.md)
