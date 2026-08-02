# Translations

Verso's interface is written in English and translated into German, Spanish, Japanese, and
Simplified Chinese. The translations are committed files, so building Verso and checking
the translations need no API key and no network. Only regenerating them does.

Strings live in two places. .NET code reads `.resx` files under `src/<Project>/Resources`,
one set per assembly, which the build turns into a satellite assembly per language. The
editor extension reads `vscode/package.nls.json` for anything named in its manifest and
`vscode/l10n/bundle.l10n.json` for the strings its own code shows.

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

Two kinds of string stay in English wherever they appear, and both are marked with a comment
in the code saying so:

- **Anything read by a model rather than a person.** The chat participant's system prompt,
  the tool descriptions in the manifest, and everything the tools hand back. Translating
  them changes how well tools are chosen without changing anything a reader sees.
- **Text that only a fault produces.** Log lines and guards against programmer error stay
  searchable, so a stack trace and an issue report still match.

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

Then, from the repository root:

```
python3 build/i18n/translate.py    # fills in the four languages
python3 build/i18n/pseudo.py       # regenerates the pseudo-locale
python3 build/i18n/check.py        # confirms the four agree with the English
```

`translate.py` only asks for keys a language does not already have, so this is cheap for a
handful of strings. It needs `pip install anthropic` and `ANTHROPIC_API_KEY`.

A machine translation is a draft. Have somebody who reads the language look over anything
user-facing before it ships.

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

## Adding a language

1. Add the tag to `VersoCultures.Supported` in `src/Verso/Localization/VersoCultures.cs`.
2. Add it to `LOCALES` and `LANGUAGE_NAMES` in `resources.py`, and to `VSCODE_IDS` if the
   editor spells it differently, as it does for Chinese.
3. Add it to the `verso.language` setting's `enum` and `enumItemLabels` in
   `vscode/package.json`, and to `SHIPPED` in `vscode/src/localization.ts`.
4. Run `translate.py`, then `check.py`.

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
