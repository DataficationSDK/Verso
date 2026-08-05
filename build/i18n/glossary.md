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
  is a chat command, `HEAD` and `main` are version control names, `.save` and `.exit` are
  REPL commands, `all` and `none` are values a command accepts, `true/false`, `yes/no` and
  `1/0` are the words a parameter of that type takes
- Keywords, methods, and header names that a specification defines: `SELECT`, `GROUP BY`,
  `WHERE`, `LIMIT`, `GET`, `POST`, `Content-Type`, `Accept-Language`. Several entries open
  with one and then explain it; explain it in the target language, and leave the word itself
- Command-line switches exactly as spelled: `--name`, `--connection-string`, `--show-output`,
  `--list`. The words after them are prose and are translated
- Environment variable names: `VERSO_PYTHON`, `VERSO_LANGUAGE`, `PATH`, `NODE_PATH`
- Package, module, and library names: `typescript`, `pandas`, `numpy`, `npm`, `pip`, `uv`,
  `FSharp.Compiler.Service`, `System.Management.Automation`, `Microsoft.Data.SqlClient`
- Argument names as the help text spells them: `<path>`, `<notebook>`, `<n>..<m>`,
  `name=value`. They describe what to type, so they are read alongside what was typed
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
| meta-command | A dot-prefixed command the REPL answers itself, such as `.save` |
| magic command | A `#!`-prefixed directive a cell can carry, such as `#!pip` |
| session | One run of the REPL, and the notebook it is building |
| package | Something installed from an index: a NuGet package, a Python distribution, an npm module |
| dependency | A package that came along with one that was asked for |
| interpreter | The Python installation a notebook's cells run in |
| connection | A named, open link to a database that SQL cells run against |
| result set | The rows one query came back with |
| schema | What a database holds: its tables, views, and columns. Also the grouping a table belongs to, which is the column heading `Schema` |

### Naming the things in the Extensions panel

Every loaded part of Verso is listed there by name, and those names are translated: a reader
should not meet a list half in their language. Three shapes recur, and each keeps its identifier
exactly as it is typed while translating the words around it.

- A magic command is named for the word it answers to, which is not translated: `Import Magic
  Command` becomes `Import-Magic-Command`, `Comando mágico Import`, `Import マジックコマンド`,
  `Import 魔法命令`.
- A part of the product is named for what it does: `SQL Renderer`, `Result Set Formatter`,
  `Jupyter Serializer`. Language names, format names, and file extensions inside them stay as
  written, and only the role word is translated.
- A consent reason says why the dialog is asking, in lower case, because it is drawn in brackets
  after the package name. `import cv2` is the exception: it quotes a line of Python back to the
  reader, so `import` is a keyword rather than a word.

## Terms already chosen

Where a house term above has been settled in a language, it is recorded here so the next batch
matches the last one rather than deciding again. A reviewer who disagrees should change the term
everywhere and update this table, not just the entry in front of them.

### German

| English | German | Why |
|---|---|---|
| notebook | Notebook | What German practitioners say. Not Notizbuch, which is the paper kind |
| cell | Zelle | |
| output | Ausgabe | |
| kernel | Kernel | Plural is also Kernel |
| extension | Erweiterung | Matches the editor's own German |
| panel | Panel | Matches the editor's own German |
| theme | Design | The editor calls it Design, so a reader meets the same word twice |
| layout | Layout | |
| baseline | Basis | Also Basisversion where a version rather than a file is meant |
| package | Paket | |
| dependency | Abhängigkeit | |
| variable | Variable | |
| parameter | Parameter | |
| magic command | Magic Command | Untranslated, as Jupyter users say it |
| meta-command | Meta-Befehl | |
| result set | Ergebnismenge | |
| schema | Schema | |
| member (of a type) | Member | Singular and plural alike, as Microsoft's German docs have it |
| renderer, handler | Renderer, Handler | Kept, as German developers say them |
| serializer | Serialisierer | |
| formatter | Formatierer | |
| widget | Widget | Kept, as the editor's own German has it |

One departure from a note: `Serve_PressCtrlC` says `Ctrl+C` is the same in every language, but a
German keyboard prints **Strg**, so the German reads `Strg+C`. Key names follow the keyboard the
reader has, which is the rule the `Key_*` entries already state.

### Spanish

| English | Spanish | Why |
|---|---|---|
| notebook | cuaderno | What the editor itself says in Spanish, so a reader meets the same word twice |
| cell | celda | |
| output | salida | |
| kernel | kernel | Plural is kernels |
| extension | extensión | |
| panel | panel | |
| theme | tema | |
| layout | diseño | The editor's own word. It does not collide with **tema**, so both stay plain |
| dashboard | Dashboard | Kept. The obvious translation is *panel*, which already means the docked region |
| baseline | línea base | |
| package | paquete | |
| dependency | dependencia | |
| variable | variable | |
| parameter | parámetro | |
| magic command | comando mágico | |
| meta-command | metacomando | |
| result set | conjunto de resultados | |
| schema | esquema | |
| member (of a type) | miembro | |
| handler | controlador | Microsoft's Spanish term, which is what the editor uses |
| renderer, render | renderizador, renderizar | What Spanish developers say, over Microsoft's *representador* |
| serializer | serializador | |
| formatter | formateador | |
| commit (version control) | confirmación | |
| required | obligatorio | |
| default | predeterminado | |
| widget | widget | Kept. What Spanish developers say, and what the editor itself uses |

Two collisions worth knowing about, because the English words are distinct and the obvious
Spanish is not. `Table_Kind` (light or dark) is **Clase** so that `Table_Type` can stay **Tipo**,
and `Table_DisplayName` is **Nombre visible** so that `Table_Name` can stay **Nombre**.

Where a count and an adjective have to agree, the count is moved rather than guessed at. The npm
audit severities read `altas: {0}` rather than `{0} altas`, because `{0}` is 1 as often as not and
Spanish would need `alta` there.

### Japanese

| English | Japanese | Why |
|---|---|---|
| notebook | ノートブック | What the editor itself says in Japanese |
| cell | セル | |
| output | 出力 | |
| kernel | カーネル | |
| extension | 拡張機能 | Matches the editor's own Japanese |
| panel | パネル | |
| theme | テーマ | |
| layout | レイアウト | |
| dashboard | ダッシュボード | No collision with **パネル**, so both stay plain |
| baseline | ベースライン | |
| package | パッケージ | |
| dependency | 依存関係 | |
| variable | 変数 | |
| parameter | パラメーター | With the long vowel mark, as Microsoft's Japanese has it |
| magic command | マジックコマンド | |
| meta-command | メタコマンド | |
| result set | 結果セット | |
| schema | スキーマ | |
| member (of a type) | メンバー | |
| renderer, handler, formatter, serializer | レンダラー、ハンドラー、フォーマッター、シリアライザー | Transliterated, as Japanese developers say them |
| commit (version control) | コミット | |
| required | 必須 | |
| default | 既定 | Microsoft's Japanese, over デフォルト |
| run / execute | 実行 | One word for both, as the source asks |
| widget | ウィジェット | |

Japanese has one form where English has two, so every `_One` and `_Other` pair is translated
identically. That is expected here and not a copy-and-paste slip.

Counts read as **{0} 件** or **{0} 個** rather than being placed like an English adjective. The
npm audit severities are **高 {0} 件**, not **{0} 高**, because Japanese puts the counter after the
number and the classifier before it.

Two headings need distinct words the obvious translation would collapse. `Table_Kind` (light or
dark) is **種類** so that `Table_Type` can stay **型**, and `Magic_Schema_ColumnNullable` is
**NULL 可** rather than a phrase, because the column is narrow and NULL is written the same in
every language.

One string is worth a reviewer's eye: `configuration.showOpenInVersoMenu.description` quotes the
editor's own **Reopen Editor With...** command, rendered here as 「エディターを再度開く...」. If
VS Code's Japanese words that command differently, match the editor rather than this file.

### Simplified Chinese

| English | Simplified Chinese | Why |
|---|---|---|
| notebook | 笔记本 | What the editor itself says in Chinese |
| cell | 单元格 | Matches the editor's own Chinese |
| output | 输出 | |
| kernel | 内核 | Matches the editor's own Chinese |
| extension | 扩展 | Matches the editor's own Chinese |
| panel | 面板 | |
| theme | 主题 | |
| layout | 布局 | |
| dashboard | 仪表板 | No collision with **面板**, so both stay plain |
| baseline | 基线 | |
| package | 包 | |
| dependency | 依赖项 | |
| variable | 变量 | |
| parameter | 参数 | |
| magic command | 魔法命令 | |
| meta-command | 元命令 | |
| result set | 结果集 | |
| schema (of a database) | 架构 | Microsoft's Chinese for the database sense, not 模式 |
| repository (version control) | 存储库 | Microsoft's Chinese, which the editor also uses |
| tag (version control) | 标记 | |
| tag (on a cell) | 标签 | Deliberately not 标记, so a cell tag never reads as a git tag |
| member (of a type) | 成员 | |
| renderer, render | 渲染器、渲染 | What Chinese developers say, over Microsoft's 呈现 |
| handler | 处理程序 | |
| provider | 提供程序 | |
| formatter | 格式化程序 | |
| serializer | 序列化程序 | |
| commit (version control) | 提交 | |
| required | 必需 | |
| default | 默认 | |
| run / execute | 运行 | One word for both, as the source asks |
| widget | 小组件 | |

Chinese has one form where English has two, so every `_One` and `_Other` pair is translated
identically. That is expected here and not a copy-and-paste slip.

Counts read as **{0} 个** or **{0} 项** rather than being placed like an English adjective, and a
count that heads a clause moves behind its verb: `Compare_SummaryAdded` is **已添加 {0} 个**, not
**{0} 已添加**. The npm audit severities are **高 {0} 个**, because Chinese puts the measure word
after the number and the severity before it.

Three headings need distinct words the obvious translation would collapse. `Table_Kind` (light or
dark) is **种类** so that `Table_Type` can stay **类型**; `Table_Extensions` (the file extensions a
format is read from) is **扩展名** so that the add-ons can stay **扩展**; and `Common_Dismiss` is
**忽略** so that `Common_Close` can stay **关闭**, since both would otherwise be 关闭.

Punctuation follows Chinese convention: full-width **，。：（）** with no space before them, and
**、** between items in a list. The three-dot ellipsis of the source is kept as typed, because
that is what the editor's own Chinese does.

One string is worth a reviewer's eye, the same one Japanese flagged:
`configuration.showOpenInVersoMenu.description` quotes the editor's own **Reopen Editor With...**
command, rendered here as “重新打开文件方式...”. If VS Code's Chinese words that command
differently, match the editor rather than this file.

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
