# Localization

Verso's interface answers in the language the reader asked for: German, Spanish, Japanese, and Simplified Chinese ship beside English, and the [Interface Language](../guides/interface-language.md) guide covers how a reader picks one. An extension can follow the same language with the tools .NET already gives it. This guide covers what the host guarantees, how to keep your strings in a resource file, how an isolated layout gets its strings into its frame, and how to test the result.

The five [showcase extensions](../showcase/overview.md) do everything described here, so each is a worked example.

## What the host guarantees

Every host sets the language it is answering in as the thread's current UI culture, `CultureInfo.CurrentUICulture`, before it calls into an extension. The command line and the editor's host process set it once at startup; the server sets it per request and per circuit, so one process can serve readers in different languages at once. A generated resource class reads that culture on every access, which is why a first-party layout needs no wiring at all to appear in German.

The same value is exposed on the context as `IVersoContext.UICulture`. Read it when something takes a culture explicitly: building a table of strings for a renderer, choosing a resource by hand, or formatting a message that cannot go through a resource property. It is fixed for the life of a session. A reader who changes the interface language reopens the notebook, and the extension is asked again.

The formatting culture, `CultureInfo.CurrentCulture`, is deliberately left alone. Choosing a language translates words; it does not change how numbers and dates print, because that would change a notebook's results rather than its interface.

## Keeping strings in a resource file

Add a `Resources/Strings.resx` to the project and let MSBuild generate the accessor. This is the block every first-party project carries:

```xml
<ItemGroup>
  <EmbeddedResource Update="Resources\Strings.resx">
    <Generator>MSBuild:Compile</Generator>
    <StronglyTypedFileName>$(IntermediateOutputPath)Strings.Designer.cs</StronglyTypedFileName>
    <StronglyTypedLanguage>CSharp</StronglyTypedLanguage>
    <StronglyTypedNamespace>My.Extension.Resources</StronglyTypedNamespace>
    <StronglyTypedClassName>Strings</StronglyTypedClassName>
  </EmbeddedResource>
</ItemGroup>
```

Each translation is a sibling file named for its language, `Strings.de.resx`, `Strings.ja.resx`, and so on. The build turns each one into a satellite assembly under a folder named for the language, `de/My.Extension.resources.dll`, beside the main assembly. That layout is what the runtime looks for, and it is preserved through `dotnet pack` and through a marketplace install, so nothing else is needed for the translations to travel with the package.

Then use the generated class where you used to write the literal:

```csharp
public string DisplayName => Strings.Layout_DisplayName;
```

Three habits keep this correct on a server that draws for several readers:

- **Resolve on every access.** A property that reads `Strings.X` each time answers each reader in their own language. A field assigned once in a constructor answers everyone in the first reader's language, and nothing in a single-language test run will show it.
- **Encode before you append.** A layout builds HTML by hand, so anything that lands in an attribute or a text node goes through `System.Net.WebUtility.HtmlEncode`, the way the built-in layouts do. Translated text is no more trustworthy to a parser than English.
- **Compose with placeholders.** Write `Reads {0} from cell {1}` and fill it with `string.Format`, not `"Reads " + name + " from cell " + n`. Word order differs between languages, and a translator can only move a placeholder.
- **Store the key, not the text.** A string your layout writes into notebook metadata outlives the reader who saved it. Resolve it and you have stamped their language onto the file, and the next person opens a document in a language they may not read, with no way back. Store the resource key and resolve it when you draw.

For anything that counts, `Plural.Of(count, Strings.Slides_One, Strings.Slides_Other)` picks between two forms. Two forms cover every language Verso ships in; a language with more would need a real plural selector, and the remarks on `Plural` say why the helper stops there.

Give every entry a `<comment>` saying where the string is drawn and what any placeholder holds. That note is the only context a translator gets.

## Isolated layouts

An isolated layout draws its interface inside a frame, from a script you wrote, and that script has no way to ask a .NET resource for a string. The extension can, though. On mount, build a table of every string for the language the host is answering in and hand it to the frame with the rest of the initial state:

```csharp
public Task<IReadOnlyDictionary<string, object>?> OnRendererMountedAsync(LayoutRendererMountContext context)
{
    var seed = new Dictionary<string, object>
    {
        ["strings"] = StringTable.From(Strings.ResourceManager, context.Verso.UICulture),
        ["document"] = _doc,
    };
    return Task.FromResult<IReadOnlyDictionary<string, object>?>(seed);
}
```

`StringTable.From` resolves each key the way a generated property would, through the parent culture to English, so a string no translator has reached arrives in English rather than going missing. The table reaches the frame on `verso/init` under `payload.extension.strings`.

Because the table arrives with `verso/init`, build the chrome when that message arrives rather than at module load. The showcases keep a small helper at the top of their scripts and a flag that says whether the chrome exists yet:

```js
let strings = {};
let chromeBuilt = false;

// For a text node or a property assignment, where the browser never parses the result.
function t(key, ...args) {
  const text = Object.prototype.hasOwnProperty.call(strings, key) ? strings[key] : key;
  return text.replace(/\{(\d+)\}/g, (m, i) => (i < args.length ? String(args[i]) : m));
}

// For anything that becomes markup. The arguments are left alone so a caller can pass a
// fragment it built and escaped itself.
function tHtml(key, ...args) {
  return escapeHtml(Object.prototype.hasOwnProperty.call(strings, key) ? strings[key] : key)
    .replace(/\{(\d+)\}/g, (m, i) => (i < args.length ? String(args[i]) : m));
}

function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, (c) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

verso.onMessage((type, payload) => {
  if (type === "verso/init") {
    if (!chromeBuilt) {
      strings = (payload && payload.extension && payload.extension.strings) || {};
      buildChrome();
    }
    // ...apply the rest of the seed
  }
});
verso.ready();
```

A missing key falls back to the key itself, which keeps a typo visible instead of blank.

Use `tHtml` wherever the result is assigned to `innerHTML` or interpolated into a template literal that will be, including inside an attribute value such as `title="${tHtml("Toolbar_Run_Tip")}"`. Use plain `t` for `textContent`, for `setAttribute`, and for a property such as `el.title`. This is the same rule as the server side, where a layout runs every string through `WebUtility.HtmlEncode` before appending it: a translation is text, and text becomes markup only when something encodes it first. No shipped translation contains a quote or an angle bracket today, which is exactly why the habit has to be in place before one does.

The host also tells the frame which language it is drawing in. The `verso/init` payload carries `uiCulture`, a tag such as `de` or `zh-Hans`, and the frame's own document is written with `<html lang="...">` set to the same value, so a script that needs the tag can read either. A host that never resolved a language reports `en`, the language the strings are written in, so the field is always a usable tag and never blank. Do not name your own field `language`: in the same message, the per-cell `language` is the programming language.

For an inline layout the picture is simpler. Its HTML is rendered on the server, so every string goes through the resource class before it reaches the page, and the host writes the language onto the layout root as a `lang` attribute for any script that wants it.

## Testing

`StubVersoContext.UICulture` follows the thread's current UI culture unless a test sets it, so a test that wants one language for one context can have it without touching the thread.

The test worth writing is the one that proves a string resolves on every access. Switch the thread's UI culture part-way through and ask again:

```csharp
[TestMethod]
public void DisplayName_FollowsTheCurrentLanguage()
{
    var layout = new MyLayout();
    var english = layout.DisplayName;

    var original = CultureInfo.CurrentUICulture;
    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
    try
    {
        Assert.AreNotEqual(english, layout.DisplayName);
    }
    finally
    {
        CultureInfo.CurrentUICulture = original;
    }
}
```

Verso's own tests use the `qps-Ploc` pseudo-locale for this, in which every string comes back accented and bracketed. The `pseudo.py` script under `build/i18n` in the Verso repository generates one from an English resource file; `build/i18n/README.md` covers the tooling. Running `verso serve --language qps-Ploc` with your extension loaded shows at a glance which strings never reached a resource file.

## Names a reader chose

The rule above has an edge to it: a name someone typed is theirs, and translating it out from under them is as wrong as freezing a built-in name in one language. Image Studio and Form Studio both keep the two apart the same way. A layer or widget carries either a `nameKey` naming a resource entry, or a literal name a person typed, never both; renaming clears the key for good. What reaches the notebook is that stored shape, and what reaches the renderer is a projection with the keys resolved, so one file opens correctly for everybody.

Resolving on the way out and stripping on the way back in belongs on the host side rather than in the renderer. The renderer sees a resolved name and hands the whole document back when anything changes, so if the host trusted what it received, a renderer that forgot to strip the resolved text would quietly bake a language into the file.

Form Studio's chart titles show the one wrinkle worth planning for. The default title composes two entries, `{0} chart` over a chart-kind word, so the argument is stored as a key as well. Store a composed string and a later translation moves one half and leaves the other in English.

## What stays in English

Names stay as written: your extension's name if it is a name rather than a description, the languages and formats it mentions, identifiers a reader types, and values a notebook's own code compares. Text a script reads rather than a person, and text only a fault produces, stay English too, so a log from one machine matches a search made on another. The glossary under `build/i18n` in the Verso repository lists the rules the first-party translations follow.

## See Also

- [Interface Language](../guides/interface-language.md), the reader's side of the same feature.
- [Context Reference](context-reference.md) for `IVersoContext.UICulture`.
- [Layouts](layouts.md) for the `verso/init` contract an isolated renderer receives.
- [Showcase Extensions](../showcase/overview.md), five extensions that follow this guide.
