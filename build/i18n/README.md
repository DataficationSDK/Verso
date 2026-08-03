# Translations

Verso's interface is written in English and translated into German, Spanish, Japanese, and
Simplified Chinese. The translations are committed files, so building Verso and checking
the translations need no API key and no network.

Strings live in two places. .NET code reads `.resx` files under `src/<Project>/Resources`,
one set per assembly, which the build turns into a satellite assembly per language. The
editor extension reads `vscode/package.nls.json` for anything named in its manifest and
`vscode/l10n/bundle.l10n.json` for the strings its own code shows.

One set per assembly is not a choice: a satellite assembly carries one assembly's resources, so a
kernel that ships as its own package needs its own. Each kernel therefore has a `Resources` folder
and a block in its `.csproj` that generates the accessor. `Plural` lives in `Verso.Abstractions`,
which every one of them already references.

## Which file a string belongs in

Two languages are in play, and a reader may set them differently. The editor writes its own
menus, commands, and settings in whatever display language its workbench is set to, and no
Verso setting can override that. The notebook interface, the host, and the CLI answer in the
language `verso.language` or `--language` asks for. So the same words can be needed in both
places, and that is not duplication to remove: the Compare command in the palette and the
Compare panel inside a notebook can legitimately be showing two different languages at once.

The rule that decides it: if the string is drawn by the editor, it belongs in one of the two
JSON bundles; if it is drawn inside a notebook, it belongs in a `.resx`. Where the editor
sends something a notebook will draw, it sends an identifier and the notebook chooses the
words, which is what `DiffSources` does for the comparison baselines.

Four kinds of string stay in English wherever they appear, and each is marked with a comment
in the code saying so:

- **Anything read by a model rather than a person.** The chat participant's system prompt,
  the tool descriptions in the manifest, and everything the tools hand back. Translating
  them changes how well tools are chosen without changing anything a reader sees.
- **Anything read by a script rather than a person.** The `[stderr]` and `[error]` tags a run
  writes, and the status values in the document `--output json` produces. A pipeline that reads
  those would break the moment the machine running it was set to another language.
- **Text that only a fault produces.** Log lines and guards against programmer error stay
  searchable, so a stack trace and an issue report still match.
- **The shape of the protocol.** The host answers the editor over a small JSON-RPC surface, and a
  request missing a field it declares is a fault in the caller rather than something the reader
  did. A log from one machine has to match a search made on another.

The line between the last two and everything else is who can act on the message. A package that
would not download, a value that does not fit the type its parameter declares, a connection that
has since closed: the reader can do something about each of those, so each is translated. A cell
id that does not exist, or a request without the field it said it had, is a fault in code.

## Adding a string

Add it to the neutral file, in English, with a note saying where it appears and what
constrains it. In a `.resx` that is the `<comment>` element. In `package.nls.json` it is the
`{ "message": ..., "comment": [...] }` form. In extension code it is the object form of
`vscode.l10n.t`, and the note travels into the bundle when it is exported:

```ts
vscode.l10n.t({ message: "tag", comment: ["A name pinned to one point in a project's history."] })
```

That note is the only context a translator gets, and "keep short, it sits next to an icon"
is the difference between a button that fits and one that does not. It matters most for
single words: `Type`, `Value`, and `No` all mean more than one thing on their own.

After editing extension code, re-export the bundle so the new strings reach a translator:

```
cd vscode && npx @vscode/l10n-dev export --outDir ./l10n ./src
```

Then translate it, as below, and regenerate the pseudo-locale so the coverage sweep keeps
working.

## Translating

Two routes fill in a language, and they write the same files. Both ask only for the keys a
language does not already have, so adding a handful of English strings costs a handful of
translations rather than a retranslation of the interface.

Read `glossary.md` before either. It is what keeps `kernel` from becoming three different
words in three files, and it lists what must not be translated at all.

### Handing the strings to a translator

`export.py` writes out what a language is missing, each string with its English and whatever
note the developer left beside it. Nothing about that file is particular to Verso, so it can
go to a person, to a translation service, or to an assistant in a session.

```
python3 build/i18n/export.py de --limit 100
python3 build/i18n/export.py de --set Verso.Ado/Strings
```

The answer comes back through `merge.py`, in the shape its docstring gives:

```
python3 build/i18n/merge.py build/i18n/pending/de.answer.json
```

A translation that dropped a `{0}`, or came back empty, or answers a key English does not
have, is refused and named rather than written. Everything sound in the same run still lands,
so a rerun only has to cover what was named.

Export, translate, merge, export again. The second export asks for what is still outstanding
and nothing else, which is what makes a language safe to do over several sittings without
anyone keeping track of where it got to. `pending/` is working files and is not committed.

### Against the API

`translate.py` does the whole of that in one command, which is the better route for somebody
outside the project who would rather spend an API key than an afternoon.

```
python3 build/i18n/translate.py --locale de
```

It needs `pip install anthropic` and `ANTHROPIC_API_KEY`. Nothing in the build or in
continuous integration runs it, so neither building Verso nor checking the translations
needs a key.

### Either way, afterwards

```
python3 build/i18n/pseudo.py       # regenerates the pseudo-locale
python3 build/i18n/check.py        # confirms the languages agree with the English
```

A translation nobody has read is a draft, whichever route produced it. Have somebody who
reads the language look over anything user-facing before it ships.

## Counting things

Neither format has plural rules, so a count is written as two entries and the code picks
between them:

```ts
count === 1 ? vscode.l10n.t("{0} cell", count) : vscode.l10n.t("{0} cells", count)
```

Two forms cover German, Spanish, Japanese, and Simplified Chinese; the last two have one
form and translate both entries the same way. A language with more forms than two, such as
Russian or Polish, would need a real plural selector, and that is worth knowing before
adding one. What must not happen is `cell(s)`, which no other language can copy.

In .NET the pair is chosen through `Plural.Of`. Where the count is dropped into a longer
sentence, write the count out on its own and pass the phrase in as an argument, so the
sentence needs one entry rather than a singular and a plural of the whole thing:

```csharp
string.Format(Strings.Meta_Save_Done, CellCount.Describe(cells.Count), path)
```

## Words and styling

The CLI writes coloured output through Spectre.Console, whose markup is written in square
brackets. None of it reaches a translator: `Messages` in `src/Verso.Cli/Utilities` fills a
translated sentence in and adds the styling around it, and everything substituted in is
escaped, because a file path can contain a bracket too.

Where a sentence names something typed at a keyboard, that part is a placeholder rather than
part of the words, so it survives the sentence being rewritten:

```csharp
Messages.Typed(Strings.Repl_UnsavedHint, ".save", ".load")
```

Colour goes on a whole line rather than on a word inside it. English puts the verb first, so
`Saved` could be picked out where it stood; a language that ends with its verb would leave
the colour on whatever happened to come first instead.

## Sentences built from pieces

A message that reports a count is the usual place a sentence gets assembled out of fragments, and
the usual place translation breaks. `"Installed " + list + " and " + n + " dependencies."` cannot
be translated at all: every join is a decision about word order that only English made.

Write the whole sentence as one entry with numbered placeholders, and write any count out on its
own so it goes in as a single argument:

```csharp
var dependencies = string.Format(
    Plural.Of(rest.Count, Strings.Npm_DependencyCount_One, Strings.Npm_DependencyCount_Other),
    rest.Count);

return string.Format(Strings.Npm_InstalledAnd, Describe(named), dependencies);
```

Where a message continues on the same line or the next one, each part is still a whole entry
rather than a phrase glued on, and the entry carries its own leading space or line break. Where
something wraps an assembled sentence rather than following it, the wrapper takes the sentence as
its placeholder: `"{0} (execution failed)"` rather than `+ " (execution failed)"`.

## Text a browser rewrites

The table a SQL query comes back as repaints its own footer as the reader pages through it, so the
sentence goes into the page as a template with its placeholders intact and the script fills them
in. Assembling it there out of words and numbers would put it beyond a translator's reach, and the
static footer and the moving one would drift apart.

## Things that line up

A column heading, a padded label, and a rule drawn to a fixed width are all measured from the
string itself, never from a count written into the code. A translated heading is not the
length the English one was, and a table whose columns no longer line up reads as a fault
rather than as a translation.

## Adding a language

1. Add the tag to `VersoCultures.Supported` in `src/Verso/Localization/VersoCultures.cs`.
2. Add it to `LOCALES` and `LANGUAGE_NAMES` in `resources.py`, and to `VSCODE_IDS` if the
   editor spells it differently, as it does for Chinese.
3. Add it to the `verso.language` setting's `enum` and `enumItemLabels` in
   `vscode/package.json`, and to `SHIPPED` in `vscode/src/localization.ts`.
4. Translate it by either route above, then run `pseudo.py` and `check.py`.

## Checking the work

`check.py` is the one to run in continuous integration. It reports a key the English has
and a language does not, a key a language still has after English dropped it, a translation
that lost a `{0}` it was meant to fill in, and an empty translation. It also checks the
editor manifest against `package.nls.json` in both directions, because a `%key%` with
nothing behind it is drawn on screen exactly as written. All of those otherwise stay quiet
until the string is finally shown to somebody.

The pseudo-locale is the coverage check. Run the interface in `qps-Ploc` and every
translated string appears accented, bracketed, and padded, so anything still in plain
English is a string nobody moved into a resource file, and anything clipped is a place a
real translation will not fit.

```
verso serve --language qps-Ploc
```

In the editor, set `verso.language` to `qps-Ploc` by hand. It is deliberately absent from
the setting's dropdown, because it is a development aid rather than a language.
