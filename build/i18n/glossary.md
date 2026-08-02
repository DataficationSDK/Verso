# Translation glossary

Rules for translating Verso's interface. `translate.py` passes this file to the model with
every batch, and a human reviewer should hold a translation to the same rules.

## Never translate

These are names, not words. They appear as written in every language.

- **Verso**, **Verso Notebooks**, **Datafication**
- Kernel and language names: **C#**, **F#**, **Python**, **PowerShell**, **JavaScript**,
  **SQL**, **HTTP**, **Markdown**, **Mermaid**, **Blazor**, **Monaco**, **NuGet**,
  **Jupyter**, **Git**
- File extensions, exactly as spelled: `.verso`, `.ipynb`, `.dib`, `.md`, `.csx`, `.fsx`
- Magic commands and anything else typed at a keyboard: `#!import`, `#!pip`, `#!restart`,
  `#!extension`, `#!connect`
- Identifiers of any kind: extension ids, setting names such as `verso.python.useUv`,
  environment variables such as `VERSO_LANGUAGE`, command-line options such as
  `--language`, MIME types, HTTP method names
- Anything typed to make something happen: `@verso` addresses the chat assistant, `/props`
  is a chat command, `HEAD` and `main` are version control names
- The names languages call themselves. A language picker lists **English**, **Deutsch**,
  **Español**, **日本語**, **简体中文**, and those read the same whichever language the
  picker is in. Only the entry meaning "take it from the editor" is a word to translate.

## Placeholders

A placeholder is filled in at runtime. Reproduce every one exactly, spelling and case
included.

- `{0}`, `{1}` are positional. Move them wherever the sentence needs them, but keep all of
  them and add none.
- `{name}` is named. Same rule, and never translate the name inside the braces.
- `{{` and `}}` are an escaped literal brace. Leave them doubled.

## House terms

Translate these consistently. Where a target language has an established computing term,
prefer it over a coinage; where the English word is what practitioners actually say in that
language, keep the English word.

| English | Meaning in Verso |
|---|---|
| notebook | The document: cells, outputs, and metadata in one file |
| cell | One unit of the notebook, holding code or prose |
| output | What a cell produced when it ran |
| kernel | The process that runs a cell's code |
| run / execute | Both mean starting a cell. Use one word consistently |
| extension | An add-on that contributes a kernel, formatter, or panel |
| panel | A dockable region of the interface |
| layout | The arrangement a notebook is rendered with |
| parameter | A named value a notebook declares and a caller supplies |
| variable | A value produced by running a cell |
| trust | The user's decision to let something run |

## Tone and shape

- Address the reader the way the target language's own software does. German uses **Sie**;
  Japanese uses **です・ます**.
- Match the source's register: buttons and menu entries are short and imperative, messages
  are complete sentences with a full stop.
- Keep it about as long as the English. A button label that doubles in length is clipped.
- Reproduce the source's punctuation and capitalisation conventions for the target
  language, not for English. German capitalises nouns; Japanese and Chinese use their own
  full stop and need no space before it.
- Never add quotation marks, brackets, or a trailing full stop the English does not have.
- Translate nothing that is already a code sample or a literal value.
