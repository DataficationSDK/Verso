// Monaco Editor JS interop for Verso.Blazor
window.versoMonaco = (function () {
    const editors = {};
    const diffEditors = {};         // elementId → { editor, originalModel, modifiedModel }
    const layoutFns = {};           // elementId → height/layout updater
    const visibilityObservers = {}; // elementId → ResizeObserver watching for first visibility
    const dotnetRefs = {};          // model URI → DotNetObjectReference
    const registeredLanguages = new Set();
    let monacoReady = false;
    let readyCallbacks = [];
    let onReadyCallbacks = [];
    let _currentTheme = 'vs';

    // Editor font settings — overridden by VS Code extension when running in a webview
    let _editorSettings = {
        fontSize: 14,
        fontFamily: "'Cascadia Code', 'Fira Code', Consolas, monospace",
        fontLigatures: true,
        // Tallest a cell editor grows, in lines, before it scrolls inside itself; 0 means no
        // limit. A cell grows to fit its content up to here. Kept short by default so a notebook
        // stays easy to scan and a dragged cell has little distance to cover; a cell that
        // reaches it says so and points at where to raise it.
        maxLines: 30
    };

    const minEditorLines = 3;

    // The compare view is opened on purpose to read a change, so it keeps a fixed, taller limit
    // of its own rather than following the cell setting.
    const diffMaxLines = 40;

    // Height for an editor whose rendered content is contentHeight pixels tall: at least three
    // lines so an empty cell is still a comfortable target, at most maxLines lines when that is
    // above 0, plus allowance, room the limit should not count such as a horizontal scrollbar.
    // contentHeight is Monaco's own measure, which counts wrapped lines, view zones such as the
    // deleted lines an inline diff shows, and the horizontal scrollbar, none of which a line
    // count sees.
    function editorHeight(contentHeight, lineHeight, padding, maxLines, allowance) {
        let height = Math.max(contentHeight, minEditorLines * lineHeight);
        if (maxLines > 0) height = Math.min(height, maxLines * lineHeight + (allowance || 0));
        return height + padding;
    }

    function cellMaxLines() {
        const maxLines = Number(_editorSettings.maxLines);
        return maxLines > 0 ? Math.floor(maxLines) : 0;
    }

    // Apply VS Code editor settings if injected by the extension host
    if (window.__versoEditorSettings) {
        Object.assign(_editorSettings, window.__versoEditorSettings);
    }

    const completionKindMap = {
        'Method':    1,  // monaco.languages.CompletionItemKind.Method
        'Function':  1,
        'Property':  9,  // Property
        'Field':     4,  // Field
        'Variable':  5,  // Variable
        'Class':     6,  // Class
        'Interface': 7,  // Interface
        'Module':    8,  // Module
        'Keyword':  17,  // Keyword
        'Snippet':  27,  // Snippet
        'Text':     18,  // Text
        'Value':    12,  // Value
        'Enum':     15,  // Enum
        'EnumMember':16, // EnumMember
        'Struct':    6,  // Class (no distinct struct kind)
        'Event':    10,  // Event
        'Operator': 11,  // Operator
        'Unit':     13,  // Unit
    };

    function registerProviders(language) {
        if (registeredLanguages.has(language)) return;
        registeredLanguages.add(language);

        monaco.languages.registerHoverProvider(language, {
            provideHover: async function (model, position) {
                const uri = model.uri.toString();
                const ref = dotnetRefs[uri];
                if (!ref) return null;
                try {
                    const code = model.getValue();
                    const offset = model.getOffsetAt(position);
                    const result = await ref.invokeMethodAsync('GetHoverInfo', code, offset);
                    if (!result || !result.content) return null;

                    const hover = {
                        contents: [{ value: result.content }]
                    };
                    if (result.range) {
                        hover.range = new monaco.Range(
                            result.range.startLine + 1,
                            result.range.startColumn + 1,
                            result.range.endLine + 1,
                            result.range.endColumn + 1
                        );
                    }
                    return hover;
                } catch (e) {
                    return null;
                }
            }
        });

        monaco.languages.registerCompletionItemProvider(language, {
            triggerCharacters: ['.'],
            provideCompletionItems: async function (model, position) {
                const uri = model.uri.toString();
                const ref = dotnetRefs[uri];
                if (!ref) return { suggestions: [] };
                try {
                    const code = model.getValue();
                    const offset = model.getOffsetAt(position);
                    const result = await ref.invokeMethodAsync('GetCompletions', code, offset);
                    if (!result || !result.items) return { suggestions: [] };

                    const word = model.getWordUntilPosition(position);
                    const range = new monaco.Range(
                        position.lineNumber,
                        word.startColumn,
                        position.lineNumber,
                        word.endColumn
                    );

                    const suggestions = result.items.map(function (item) {
                        return {
                            label: item.displayText,
                            kind: completionKindMap[item.kind] || 18,
                            insertText: item.insertText,
                            detail: item.description || '',
                            sortText: item.sortText || item.displayText,
                            range: range
                        };
                    });
                    return { suggestions: suggestions };
                } catch (e) {
                    return { suggestions: [] };
                }
            }
        });
    }

    // ── Kernel diagnostics ──────────────────────────────────────────────
    // Hover and completion are pulled by Monaco; diagnostics are pushed. Each editor asks its
    // kernel for diagnostics when the user pauses typing, when the editor gains focus, and when
    // .NET asks (after the cell runs), then replaces its markers under one owner. Requests are
    // held while a cell is running, one is in flight per editor at a time, and a reply computed
    // for text the user has since changed is dropped. A focus refresh is skipped when neither the
    // text nor the kernel state has changed since the last answer, since for C# and F# a request
    // is a full compilation, and in VS Code it would queue ahead of a run that follows.
    const DIAGNOSTICS_OWNER = 'verso-kernel';
    const DIAGNOSTICS_DELAY_MS = 500;
    const DIAGNOSTICS_MAX_LINES = 2000;
    // elementId → { timer, inFlight, pending, suspended, generation, staleEpoch, answeredVersion,
    //               answeredEpoch, applyingValue }
    const diagnosticsState = {};
    const markerSeverity = { Error: 8, Warning: 4, Info: 2 }; // monaco.MarkerSeverity; Hidden is never shown

    function diagnosticsStateFor(elementId) {
        let state = diagnosticsState[elementId];
        if (!state) {
            state = {
                timer: null, inFlight: false, pending: false, suspended: false, generation: 0,
                // staleEpoch moves whenever kernel state may have changed under the text (a run
                // ended, a restart, a language change). The last answer records the model version
                // and epoch it was computed for; while both still match, a focus refresh is moot.
                staleEpoch: 0, answeredVersion: null, answeredEpoch: -1,
                applyingValue: false
            };
            diagnosticsState[elementId] = state;
        }
        return state;
    }

    // Kernel positions are 0-based and cell-relative; Monaco's are 1-based. Every coordinate is
    // clamped to the model so a stale or malformed reply cannot throw inside Monaco. An empty span
    // that starts at column 0 is a cell-level or line-level advisory and covers its whole line; an
    // empty span anywhere else is a real point (a missing ';' at the end of a line) and stays put.
    function toMarkers(model, result) {
        const lineCount = model.getLineCount();
        const markers = [];
        (result.items || []).forEach(function (item) {
            const severity = markerSeverity[item.severity];
            if (!severity) return;

            const rawStartLine = Number(item.startLine) || 0;
            const rawStartColumn = Number(item.startColumn) || 0;
            const rawEndLine = Number(item.endLine) || 0;
            const rawEndColumn = Number(item.endColumn) || 0;

            const clampLine = function (line) { return Math.min(Math.max(line, 1), lineCount); };
            const clampColumn = function (line, column) {
                return Math.min(Math.max(column, 1), model.getLineMaxColumn(line));
            };

            const startLineNumber = clampLine(rawStartLine + 1);
            let startColumn = clampColumn(startLineNumber, rawStartColumn + 1);
            let endLineNumber = clampLine(rawEndLine + 1);
            let endColumn = clampColumn(endLineNumber, rawEndColumn + 1);

            const empty = rawEndLine < rawStartLine
                || (rawEndLine === rawStartLine && rawEndColumn <= rawStartColumn);
            if (empty && rawStartColumn === 0) {
                startColumn = 1;
                endLineNumber = startLineNumber;
                endColumn = model.getLineMaxColumn(startLineNumber);
            } else if (endLineNumber < startLineNumber
                || (endLineNumber === startLineNumber && endColumn < startColumn)) {
                endLineNumber = startLineNumber;
                endColumn = startColumn;
            }

            const marker = {
                severity: severity,
                message: item.message || '',
                startLineNumber: startLineNumber,
                startColumn: startColumn,
                endLineNumber: endLineNumber,
                endColumn: endColumn
            };
            if (item.code) marker.code = item.code;
            if (result.source) marker.source = result.source;
            markers.push(marker);
        });
        return markers;
    }

    function setKernelMarkers(model, markers) {
        if (model && !model.isDisposed()) {
            monaco.editor.setModelMarkers(model, DIAGNOSTICS_OWNER, markers);
        }
    }

    function markDiagnosticsStale(state) {
        if (state) state.staleEpoch++;
    }

    // onFocus is true for a refresh triggered by the editor gaining focus, which is skipped when
    // the last answer still holds. Edits, runs and explicit requests always go through.
    function refreshDiagnostics(elementId, onFocus) {
        const state = diagnosticsState[elementId];
        const editor = editors[elementId];
        if (!state || !editor) return;

        if (onFocus && !state.pending && !state.timer) {
            const current = editor.getModel();
            if (current && current.getVersionId() === state.answeredVersion
                && state.answeredEpoch === state.staleEpoch) {
                return;
            }
        }

        if (state.timer) {
            clearTimeout(state.timer);
            state.timer = null;
        }

        // A running cell holds the kernel; a request now would wait for the run and answer
        // against state it is changing. Ask once the run is over instead.
        if (state.suspended || state.inFlight) {
            state.pending = true;
            return;
        }
        state.pending = false;

        const model = editor.getModel();
        if (!model) return;
        const ref = dotnetRefs[model.uri.toString()];
        if (!ref) return;

        // A pathological pasted cell is not analyzed at all, and what it showed before no
        // longer applies.
        if (model.getLineCount() > DIAGNOSTICS_MAX_LINES) {
            setKernelMarkers(model, []);
            return;
        }

        const version = model.getVersionId();
        const generation = state.generation;
        const epoch = state.staleEpoch;
        state.inFlight = true;

        ref.invokeMethodAsync('GetDiagnostics', model.getValue())
            .then(function (result) {
                if (diagnosticsState[elementId] !== state || state.generation !== generation) return;
                if (model.isDisposed() || model.getVersionId() !== version) return;
                // Null means the request failed; a transient failure must not hide a real error.
                if (!result) return;
                setKernelMarkers(model, toMarkers(model, result));
                state.answeredVersion = version;
                state.answeredEpoch = epoch;
            })
            .catch(function () { /* failure is silence: keep what is shown */ })
            .finally(function () {
                state.inFlight = false;
                if (state.pending && !state.suspended && diagnosticsState[elementId] === state) {
                    refreshDiagnostics(elementId);
                }
            });
    }

    function scheduleDiagnostics(elementId) {
        const state = diagnosticsState[elementId];
        if (!state) return;
        if (state.timer) clearTimeout(state.timer);
        state.timer = setTimeout(function () {
            state.timer = null;
            refreshDiagnostics(elementId);
        }, DIAGNOSTICS_DELAY_MS);
    }

    function clearDiagnostics(elementId) {
        const state = diagnosticsState[elementId];
        if (state) {
            if (state.timer) {
                clearTimeout(state.timer);
                state.timer = null;
            }
            state.pending = false;
            // Invalidates any reply still in flight, so it cannot repaint what was just cleared.
            state.generation++;
            markDiagnosticsStale(state);
        }
        const editor = editors[elementId];
        if (editor) setKernelMarkers(editor.getModel(), []);
    }

    // Extend the built-in C# monarch tokenizer to highlight #i "nuget: ..." directives
    // the same way Monaco highlights #r directives (as preprocessor + string).
    // The theme last handed to applyTheme(), kept so it can be defined as soon as Monaco
    // loads: a host usually knows its theme before the editor script has arrived.
    const CUSTOM_THEME = 'verso-active';
    const BASE_THEMES = ['vs', 'vs-dark', 'hc-black', 'hc-light'];
    let _themeSpec = null;

    // Monaco reads theme colors as hex only, and paints anything else bright red rather
    // than ignoring it. Theme tokens and CSS variables can hold rgb()/rgba() as well, so
    // every color is brought to #RRGGBB or #RRGGBBAA here, and dropped if it cannot be.
    function toHexColor(value) {
        if (typeof value !== 'string') return null;
        const v = value.trim();
        let m = /^#([0-9a-f]{3,8})$/i.exec(v);
        if (m) {
            const h = m[1];
            if (h.length === 6 || h.length === 8) return '#' + h;
            if (h.length === 3 || h.length === 4) {
                return '#' + h.split('').map(function (c) { return c + c; }).join('');
            }
            return null;
        }
        m = /^rgba?\(\s*([\d.]+)[\s,]+([\d.]+)[\s,]+([\d.]+)(?:[\s,\/]+([\d.]+%?))?\s*\)$/i.exec(v);
        if (!m) return null;
        const byte = function (n) {
            const b = Math.max(0, Math.min(255, Math.round(n)));
            return (b < 16 ? '0' : '') + b.toString(16);
        };
        let hex = '#' + byte(+m[1]) + byte(+m[2]) + byte(+m[3]);
        if (m[4] !== undefined) {
            const a = m[4].slice(-1) === '%' ? parseFloat(m[4]) / 100 : parseFloat(m[4]);
            if (a < 1) hex += byte(a * 255);
        }
        return hex;
    }

    function defineCustomTheme() {
        const spec = _themeSpec;
        if (!spec) return;

        const colors = {};
        Object.keys(spec.colors || {}).forEach(function (id) {
            const hex = toHexColor(spec.colors[id]);
            if (hex) colors[id] = hex;
        });

        const rules = [];
        (spec.rules || []).forEach(function (r) {
            if (!r || typeof r.token !== 'string') return;
            const rule = { token: r.token };
            // Token colors take six or eight hex digits and no leading '#'.
            const fg = toHexColor(r.foreground);
            if (fg) rule.foreground = fg.slice(1);
            if (typeof r.fontStyle === 'string') rule.fontStyle = r.fontStyle;
            if (rule.foreground || rule.fontStyle !== undefined) rules.push(rule);
        });

        monaco.editor.defineTheme(CUSTOM_THEME, {
            base: BASE_THEMES.indexOf(spec.base) >= 0 ? spec.base : 'vs',
            inherit: spec.inherit !== false,
            rules: rules,
            colors: colors
        });
    }

    function enhanceCSharpTokenizer() {
        const langDef = monaco.languages.getLanguages().find(l => l.id === 'csharp');
        if (!langDef || !langDef.loader) return;

        // Wrap the language loader to inject our custom rules
        const originalLoader = langDef.loader;
        langDef.loader = function () {
            return originalLoader().then(function (mod) {
                const tokenizer = mod.language && mod.language.tokenizer;
                if (tokenizer && tokenizer.root) {
                    // Match #i as a preprocessor keyword, then the quoted string as a string literal.
                    // Uses two rules: one for the directive keyword, one for the string that follows.
                    tokenizer.root.unshift(
                        [/(#i)(\s+)("(?:[^"\\]|\\.)*")/, ['keyword.preprocessor', 'white', 'string']]
                    );
                }
                return mod;
            });
        };
    }

    // Initialize Monaco AMD loader
    function ensureMonaco(callback) {
        if (monacoReady) {
            callback();
            return;
        }
        readyCallbacks.push(callback);
        if (readyCallbacks.length === 1) {
            require.config({ paths: { vs: 'https://cdn.jsdelivr.net/npm/monaco-editor@0.45.0/min/vs' } });
            require(['vs/editor/editor.main'], function () {
                enhanceCSharpTokenizer();

                // The kernels own JavaScript and TypeScript diagnostics. Monaco's bundled worker
                // checks each cell as a standalone file and so flags what a notebook does on
                // purpose (a name declared in an earlier cell, top-level await, require, magic
                // lines). Its validation is turned off; its completion and hover stay.
                const tsLanguages = monaco.languages.typescript;
                if (tsLanguages) {
                    const off = { noSyntaxValidation: true, noSemanticValidation: true };
                    tsLanguages.typescriptDefaults.setDiagnosticsOptions(off);
                    tsLanguages.javascriptDefaults.setDiagnosticsOptions(off);
                }

                // Remove AMD flag so UMD libraries (Plotly, D3, Leaflet, etc.)
                // loaded from CDN skip the AMD path and assign to window directly.
                // Then lock `define` so cell output scripts (e.g. Plotly's AMD
                // workaround: `window.define = undefined`) cannot destroy it.
                // Monaco still needs define() for lazy-loading language grammars.
                if (typeof define === 'function' && define.amd) {
                    delete define.amd;
                    Object.defineProperty(window, 'define', {
                        value: define,
                        writable: false,
                        configurable: false
                    });
                }

                monacoReady = true;
                defineCustomTheme();
                readyCallbacks.forEach(cb => cb());
                readyCallbacks = [];
                onReadyCallbacks.forEach(cb => cb());
                onReadyCallbacks = [];
            });
        }
    }

    // Whether every editor inside a cell has been given its content height. create()
    // sets that height inline once Monaco can measure a line; until then the host is
    // zero-high and the cell measures far shorter than it will end up. A cell with no
    // editor in it, which is what a collapsed or display-only cell looks like, is
    // sized as soon as it is on screen.
    function editorsSized(cell) {
        const hosts = cell.querySelectorAll('.verso-monaco-editor');
        for (let i = 0; i < hosts.length; i++) {
            if (!hosts[i].style.height) return false;
        }
        return true;
    }

    // Eagerly start loading Monaco at page load so it is fully initialized
    // (and define.amd removed) before any notebook opens.  This prevents
    // <script> tags in saved cell outputs from interfering with the AMD
    // module loader — by the time outputs render, Monaco no longer needs
    // the define function.
    ensureMonaco(function () {});

    return {
        // Returns a Promise that resolves when Monaco is fully loaded and
        // define.amd has been removed.  Resolves immediately if already ready.
        waitForReady: function () {
            return new Promise(function (resolve) {
                if (monacoReady) { resolve(); }
                else { onReadyCallbacks.push(resolve); }
            });
        },

        create: function (elementId, options, dotnetRef) {
            // Set up before Monaco loads, so a suspension .NET sends in the meantime is kept.
            if (dotnetRef) {
                diagnosticsStateFor(elementId).suspended = !!options.diagnosticsSuspended;
            }

            ensureMonaco(function () {
                const container = document.getElementById(elementId);
                if (!container) return;

                const editor = monaco.editor.create(container, {
                    value: options.value || '',
                    language: options.language || 'csharp',
                    theme: _currentTheme,
                    readOnly: options.readOnly || false,
                    minimap: { enabled: false },
                    scrollBeyondLastLine: false,
                    lineNumbers: 'on',
                    glyphMargin: false,
                    folding: false,
                    lineDecorationsWidth: 10,
                    lineNumbersMinChars: 3,
                    renderLineHighlight: 'line',
                    automaticLayout: true,
                    fontSize: _editorSettings.fontSize,
                    fontFamily: _editorSettings.fontFamily,
                    fontLigatures: _editorSettings.fontLigatures,
                    scrollbar: {
                        vertical: 'auto',
                        horizontal: 'auto',
                        verticalScrollbarSize: 10,
                        horizontalScrollbarSize: 10,
                        alwaysConsumeMouseWheel: false
                    }
                });

                // Size the editor to its content. Returns false when Monaco cannot measure yet —
                // an editor created inside a display:none container (the custom-layout cell pool,
                // before a cell is portaled into a visible slot) reports a 0 lineHeight, and
                // sizing then would lock it at the padding height even after it becomes visible.
                function applyHeight() {
                    const lineHeight = editor.getOption(monaco.editor.EditorOption.lineHeight);
                    if (!lineHeight) return false;
                    const maxLines = cellMaxLines();
                    const contentHeight = editor.getContentHeight();
                    const lineCount = editor.getModel().getLineCount();
                    // A cell neither wraps nor has view zones, so whatever its content measures
                    // beyond its lines is the horizontal scrollbar. The limit counts lines, so a
                    // cell of exactly the limit with one long line still shows every line.
                    const scrollbar = Math.max(0, contentHeight - lineCount * lineHeight);
                    const height = editorHeight(contentHeight, lineHeight, 10, maxLines, scrollbar) + 'px';
                    if (container.style.height !== height) {
                        container.style.height = height;
                        editor.layout();
                    }
                    reportLineLimit(maxLines > 0 && lineCount > maxLines, lineCount, maxLines);
                    return true;
                }

                // Tells the cell when its content no longer fits under the limit, so it can say
                // how much is out of view and where the limit is changed. Sent only on a change.
                // Starts as the not-clipped report, so a cell that fits says nothing on open.
                let lastLimitReport = '';
                function reportLineLimit(clipped, lineCount, maxLines) {
                    if (!dotnetRef) return;
                    const report = clipped ? lineCount + '/' + maxLines : '';
                    if (report === lastLimitReport) return;
                    lastLimitReport = report;
                    dotnetRef.invokeMethodAsync('OnLineLimitChanged', clipped, lineCount, maxLines)
                        .catch(function () { /* component disposed */ });
                }

                // When created hidden, retry for a short, bounded window until the editor is on
                // screen and measurable. The bound matters: a layout that never portals a pooled
                // cell would otherwise spin forever. relayout() re-runs this when the portal moves
                // the cell into a slot, so a cell that becomes visible later still sizes correctly.
                function updateHeight() {
                    if (applyHeight()) return;
                    let tries = 0;
                    (function retry() {
                        if (!layoutFns[elementId]) return; // editor disposed mid-window
                        if (applyHeight() || tries++ > 60) return;
                        requestAnimationFrame(retry);
                    })();
                }
                layoutFns[elementId] = updateHeight;

                // Content height changes on more than edits: a horizontal scrollbar appearing,
                // a font change, a view zone. This is the event Monaco raises for all of them.
                editor.onDidContentSizeChange(function (e) {
                    if (e.contentHeightChanged) updateHeight();
                });

                editor.onDidChangeModelContent(function () {
                    if (dotnetRef) {
                        const value = editor.getValue();
                        dotnetRef.invokeMethodAsync('OnContentChanged', value);
                        // Text set from .NET (a reload, a tool editing many cells) would otherwise
                        // start a compile in every editor at once. Only the editor being typed in
                        // asks now; the rest have a new version and ask when next focused.
                        const state = diagnosticsState[elementId];
                        if (!state || !state.applyingValue || editor.hasTextFocus()) {
                            scheduleDiagnostics(elementId);
                        }
                    }
                });

                updateHeight();

                // Editors created inside the custom-layout cell pool start hidden (display:none),
                // so the initial layout measures a zero-size box; Monaco's own automaticLayout
                // ResizeObserver does not reliably re-fire when the cell is later portaled into a
                // visible slot (timing varies by host, and the portal-mount relayout can race the
                // editor's async creation). Watch the container directly and re-apply the height
                // the first time it actually has a size, then stop. This is the timing-independent
                // backstop that makes a freshly added cell come up at full height everywhere.
                if (window.ResizeObserver) {
                    const ro = new ResizeObserver(function () {
                        if (container.clientHeight > 0 || container.offsetParent !== null) {
                            if (applyHeight()) {
                                ro.disconnect();
                                delete visibilityObservers[elementId];
                            }
                        }
                    });
                    ro.observe(container);
                    visibilityObservers[elementId] = ro;
                }

                // Register keyboard shortcuts that call back to .NET
                if (dotnetRef) {
                    // Every cell's editor shares Monaco's single global keybinding service, so
                    // registering the same chord (e.g. Shift+Enter) on each editor with no
                    // condition makes the resolver keep only the last registration: the shortcut
                    // then always fires against the last-created cell instead of the focused one.
                    // Gate each command on a context key that is true only while this editor holds
                    // text focus, so the chords resolve to the focused cell. The key name is unique
                    // per editor and sanitised, since '-' is an operator in a when expression.
                    const focusKeyName = 'versoEditorFocused_' + elementId.replace(/[^a-zA-Z0-9]/g, '_');
                    const focusKey = editor.createContextKey(focusKeyName, false);
                    // Track edit mode in .NET: while the editor holds focus the notebook is in
                    // edit mode and its command-mode keys (a/b/x, arrows) must stand down so those
                    // letters edit code instead of mutating the notebook.
                    editor.onDidFocusEditorText(function () {
                        focusKey.set(true);
                        dotnetRef.invokeMethodAsync('OnEditorActionShortcut', 'focus');
                        // Variable-dependent kernels (SQL, HTTP, HTML) change their answer when
                        // another cell runs, not when this text changes; refresh as the user
                        // comes back to the cell.
                        refreshDiagnostics(elementId, true);
                    });
                    editor.onDidBlurEditorText(function () { focusKey.set(false); });

                    editor.addCommand(
                        monaco.KeyMod.Shift | monaco.KeyCode.Enter,
                        function () { dotnetRef.invokeMethodAsync('OnEditorActionShortcut', 'run-and-select-below'); },
                        focusKeyName
                    );
                    editor.addCommand(
                        monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter,
                        function () { dotnetRef.invokeMethodAsync('OnEditorActionShortcut', 'run-and-stay'); },
                        focusKeyName
                    );
                    editor.addCommand(
                        monaco.KeyMod.Alt | monaco.KeyCode.Enter,
                        function () { dotnetRef.invokeMethodAsync('OnEditorActionShortcut', 'run-and-insert-below'); },
                        focusKeyName
                    );
                    editor.addCommand(
                        monaco.KeyCode.Escape,
                        function () {
                            // Leave edit mode: move focus to the notebook so command-mode keys work
                            // immediately (Jupyter-style). Blurring the editor fires the widget-blur
                            // handler below, which notifies .NET to leave edit mode.
                            const notebook = container.closest('.verso-notebook');
                            if (notebook) notebook.focus();
                            dotnetRef.invokeMethodAsync('OnEditorActionShortcut', 'escape');
                        },
                        focusKeyName + ' && !suggestWidgetVisible && !parameterHintsVisible'
                    );

                    // When the editor loses focus (e.g. the user clicks another cell), notify
                    // .NET so render-only cells (Markdown, etc.) render. onDidBlurEditorWidget
                    // (not ...EditorText) does not fire when focus moves to the suggestion or
                    // parameter-hint widget, so in-editor completions do not trigger a render.
                    editor.onDidBlurEditorWidget(function () {
                        dotnetRef.invokeMethodAsync('OnEditorActionShortcut', 'blur');
                    });
                }

                editors[elementId] = editor;

                // Store dotnetRef keyed by model URI for hover/completion routing
                const modelUri = editor.getModel().uri.toString();
                if (dotnetRef) {
                    dotnetRefs[modelUri] = dotnetRef;
                }
                registerProviders(options.language || 'csharp');
            });
        },

        dispose: function (elementId) {
            clearDiagnostics(elementId);
            delete diagnosticsState[elementId];
            const editor = editors[elementId];
            if (editor) {
                const modelUri = editor.getModel()?.uri?.toString();
                if (modelUri) {
                    delete dotnetRefs[modelUri];
                }
                editor.dispose();
                delete editors[elementId];
                delete layoutFns[elementId];
            }
            const observer = visibilityObservers[elementId];
            if (observer) {
                observer.disconnect();
                delete visibilityObservers[elementId];
            }
        },

        // Creates a read-only side-by-side (or inline) diff editor comparing two source strings.
        // Diff editors are display-only: no completion providers, keyboard shortcuts, or .NET
        // callbacks are wired, and content never changes after creation.
        createDiffEditor: function (elementId, options) {
            ensureMonaco(function () {
                const container = document.getElementById(elementId);
                if (!container) return;

                const originalModel = monaco.editor.createModel(options.originalValue || '', options.language || 'csharp');
                const modifiedModel = monaco.editor.createModel(options.modifiedValue || '', options.language || 'csharp');
                const editor = monaco.editor.createDiffEditor(container, {
                    theme: _currentTheme,
                    readOnly: true,
                    originalEditable: false,
                    renderSideBySide: options.renderSideBySide !== false,
                    minimap: { enabled: false },
                    scrollBeyondLastLine: false,
                    lineNumbers: 'on',
                    glyphMargin: false,
                    folding: false,
                    lineDecorationsWidth: 10,
                    lineNumbersMinChars: 3,
                    renderOverviewRuler: false,
                    automaticLayout: true,
                    fontSize: _editorSettings.fontSize,
                    fontFamily: _editorSettings.fontFamily,
                    fontLigatures: _editorSettings.fontLigatures,
                    scrollbar: {
                        vertical: 'auto',
                        horizontal: 'auto',
                        verticalScrollbarSize: 10,
                        horizontalScrollbarSize: 10,
                        alwaysConsumeMouseWheel: false
                    }
                });
                editor.setModel({ original: originalModel, modified: modifiedModel });
                diffEditors[elementId] = { editor: editor, originalModel: originalModel, modifiedModel: modifiedModel };

                // Size to content like create()'s applyHeight, using the taller side. The diff
                // view mounts its editors only while visible, but the first measure can still
                // race Monaco's async setup, so keep a short bounded retry.
                function applyHeight() {
                    const originalEditor = editor.getOriginalEditor();
                    const modifiedEditor = editor.getModifiedEditor();
                    const lineHeight = modifiedEditor.getOption(monaco.editor.EditorOption.lineHeight);
                    if (!lineHeight) return false;
                    const contentHeight = Math.max(originalEditor.getContentHeight(), modifiedEditor.getContentHeight());
                    const height = editorHeight(contentHeight, lineHeight, 14, diffMaxLines) + 'px';
                    if (container.style.height !== height) {
                        container.style.height = height;
                        editor.layout();
                    }
                    return true;
                }

                function updateHeight() {
                    if (applyHeight()) return;
                    let tries = 0;
                    (function retry() {
                        if (!diffEditors[elementId]) return; // disposed mid-window
                        if (applyHeight() || tries++ > 60) return;
                        requestAnimationFrame(retry);
                    })();
                }

                // The diff is computed after the editor is created, and it is what adds the
                // padding that lines up the two sides and, inline, the deleted lines. Both
                // change the content height after the first measure.
                [editor.getOriginalEditor(), editor.getModifiedEditor()].forEach(function (side) {
                    side.onDidContentSizeChange(function (e) {
                        if (e.contentHeightChanged) updateHeight();
                    });
                });

                updateHeight();
            });
        },

        disposeDiffEditor: function (elementId) {
            const entry = diffEditors[elementId];
            if (!entry) return;
            entry.editor.dispose();
            entry.originalModel.dispose();
            entry.modifiedModel.dispose();
            delete diffEditors[elementId];
        },

        // Focuses an element by id, used by overlay surfaces to capture keyboard events
        // (e.g. Escape-to-close) as soon as they mount.
        focusElement: function (elementId) {
            const el = document.getElementById(elementId);
            if (el) el.focus();
        },

        // Scrolls an element into view by id, for walking a list of anchors (the diff
        // view's prev/next change). 'start' rather than 'nearest' so a change already
        // partly visible still moves to the top, which is what makes stepping feel like
        // stepping rather than nothing happening.
        scrollElementIntoView: function (elementId) {
            const el = document.getElementById(elementId);
            if (el) el.scrollIntoView({ block: 'start', behavior: 'smooth' });
        },

        // Re-measure and re-lay-out an editor. Used after a custom layout portals a cell from the
        // hidden pool into a visible slot: the editor may have been created while unmeasurable, so
        // re-running its height updater now that it is on-screen restores the correct size.
        relayout: function (elementId) {
            const update = layoutFns[elementId];
            if (update) { update(); return; }
            const editor = editors[elementId];
            if (editor) editor.layout();
        },

        relayoutAll: function () {
            Object.keys(layoutFns).forEach(function (id) {
                try { layoutFns[id](); } catch (e) { /* editor torn down mid-flight */ }
            });
        },

        getValue: function (elementId) {
            const editor = editors[elementId];
            return editor ? editor.getValue() : '';
        },

        setValue: function (elementId, value) {
            const editor = editors[elementId];
            if (editor && editor.getValue() !== value) {
                const state = diagnosticsState[elementId];
                if (state) state.applyingValue = true;
                try {
                    editor.setValue(value);
                } finally {
                    if (state) state.applyingValue = false;
                }
            }
        },

        setLanguage: function (elementId, language) {
            const editor = editors[elementId];
            if (editor) {
                const model = editor.getModel();
                if (model && monaco.editor.getModel(model.uri)) {
                    // The previous kernel's diagnostics say nothing about the new language.
                    clearDiagnostics(elementId);
                    monaco.editor.setModelLanguage(model, language);
                    registerProviders(language);
                }
            }
        },

        // Ask the kernel for this editor's diagnostics now (after its cell ran, for example).
        refreshDiagnostics: function (elementId) {
            refreshDiagnostics(elementId);
        },

        clearDiagnostics: function (elementId) {
            clearDiagnostics(elementId);
        },

        // While suspended no request is issued; a refresh asked for in the meantime runs once
        // the suspension lifts.
        setDiagnosticsSuspended: function (elementId, suspended) {
            const state = diagnosticsState[elementId];
            if (!state) return;
            // A run that just ended may have changed what other cells' code refers to.
            if (state.suspended && !suspended) markDiagnosticsStale(state);
            state.suspended = !!suspended;
            if (!state.suspended && state.pending) {
                refreshDiagnostics(elementId);
            }
        },

        setTheme: function (theme) {
            _themeSpec = null;
            _currentTheme = theme || 'vs';
            if (monacoReady) {
                monaco.editor.setTheme(_currentTheme);
            }
        },

        // Themes every editor from a description the host builds out of its own theme:
        //   base   one of 'vs', 'vs-dark', 'hc-black', 'hc-light'; supplies whatever the
        //          description leaves out
        //   inherit false to take only the base's light/dark defaults and none of its
        //          syntax rules, for a description whose rules are complete
        //   colors Monaco color id -> CSS color ('editor.background', ...)
        //   rules  [{ token, foreground, fontStyle }] in Monaco's token names
        // Calling it again replaces the previous description.
        applyTheme: function (spec) {
            if (!spec) return;
            _themeSpec = spec;
            _currentTheme = CUSTOM_THEME;
            if (monacoReady) {
                defineCustomTheme();
                monaco.editor.setTheme(CUSTOM_THEME);
            }
        },

        focus: function (elementId) {
            const editor = editors[elementId];
            if (editor) {
                editor.focus();
            }
        },

        focusByContainer: function (containerSelector) {
            const container = document.querySelector(containerSelector);
            if (!container) return;
            const editorEl = container.querySelector('.verso-monaco-editor');
            if (!editorEl) return;
            const editor = editors[editorEl.id];
            if (!editor) return;
            // Defer focus until the current key event has fully finished. This path runs when
            // the user presses Enter in command mode to start editing; focusing synchronously
            // would let that same Enter land in the editor as a newline. By the next tick the
            // Enter has already been handled on the notebook container (a div, where it does
            // nothing), so the editor receives only the keystrokes typed afterwards.
            setTimeout(function () { editor.focus(); }, 0);
        },

        scrollToSelected: function () {
            // Poll across animation frames until the selected cell is both placed and
            // sized, then scroll to it. Both halves of that wait are needed.
            //
            // Placed: in the custom portaling layouts every cell is first rendered inside
            // a hidden pool (display:none, so offsetHeight is 0) and only moved into a
            // visible slot once the layout HTML regenerates. For a newly added cell that
            // regeneration is a full round trip on the server host (over the circuit), so
            // a fixed delay or a bare offsetHeight check can fire while the cell is still
            // pooled, where scrollIntoView does nothing. Non-portaling layouts have no
            // pool, so the closest() check is null.
            //
            // Sized: selecting a cell can swap a short rendered preview for an editor, and
            // the editor's own height lands later again, once Monaco has been created and
            // can measure a line. Scrolling in between measures a cell hundreds of pixels
            // shorter than it ends up, and a smooth scroll fixes its destination at that
            // moment rather than following the element, so the cell settles with its
            // bottom below the fold. The last cell shows this worst: there is nothing
            // after it to scroll up into view.
            //
            // The cap bounds both waits. A cell that is at least placed is still worth
            // scrolling to, so a never-measurable editor costs accuracy rather than the
            // whole reveal.
            let tries = 0;
            (function retry() {
                const el = document.querySelector('.verso-cell--selected');
                const placed = el && !el.closest('.verso-cell-pool') && el.offsetHeight > 0;
                if (placed && editorsSized(el)) {
                    el.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
                    return;
                }
                if (tries++ > 180) { // ~3s at 60fps
                    // Out of patience: scroll to it anyway, but never to a hidden node.
                    if (placed) el.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
                    return;
                }
                requestAnimationFrame(retry);
            })();
        },

        updateEditorSettings: function (settings) {
            Object.assign(_editorSettings, settings);
            const opts = {};
            if (settings.fontSize !== undefined) opts.fontSize = settings.fontSize;
            if (settings.fontFamily !== undefined) opts.fontFamily = settings.fontFamily;
            if (settings.fontLigatures !== undefined) opts.fontLigatures = settings.fontLigatures;
            Object.values(editors).forEach(function (ed) { ed.updateOptions(opts); });
            Object.values(diffEditors).forEach(function (entry) { entry.editor.updateOptions(opts); });
            // A font change resizes through onDidContentSizeChange; a new limit alone does not.
            if (settings.maxLines !== undefined) {
                Object.values(layoutFns).forEach(function (fn) { try { fn(); } catch (e) { } });
            }
        }
    };
})();
