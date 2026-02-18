// Monaco Editor JS interop for Verso.Blazor
window.versoMonaco = (function () {
    const editors = {};
    const dotnetRefs = {};          // model URI → DotNetObjectReference
    const registeredLanguages = new Set();
    let monacoReady = false;
    let readyCallbacks = [];
    let _currentTheme = 'vs';

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
                monacoReady = true;
                readyCallbacks.forEach(cb => cb());
                readyCallbacks = [];
            });
        }
    }

    return {
        create: function (elementId, options, dotnetRef) {
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
                    fontSize: 14,
                    fontFamily: "'Cascadia Code', 'Fira Code', Consolas, monospace",
                    scrollbar: {
                        vertical: 'auto',
                        horizontal: 'auto',
                        verticalScrollbarSize: 10,
                        horizontalScrollbarSize: 10
                    }
                });

                // Auto-resize to content
                function updateHeight() {
                    const lineCount = editor.getModel().getLineCount();
                    const minLines = 3;
                    const maxLines = 30;
                    const lines = Math.max(minLines, Math.min(maxLines, lineCount));
                    const lineHeight = editor.getOption(monaco.editor.EditorOption.lineHeight);
                    const padding = 10;
                    const newHeight = lines * lineHeight + padding;
                    container.style.height = newHeight + 'px';
                    editor.layout();
                }

                editor.onDidChangeModelContent(function () {
                    updateHeight();
                    if (dotnetRef) {
                        const value = editor.getValue();
                        dotnetRef.invokeMethodAsync('OnContentChanged', value);
                    }
                });

                updateHeight();
                // Register keyboard shortcuts that call back to .NET
                if (dotnetRef) {
                    editor.addCommand(
                        monaco.KeyMod.Shift | monaco.KeyCode.Enter,
                        function () { dotnetRef.invokeMethodAsync('OnEditorActionShortcut', 'run-and-select-below'); }
                    );
                    editor.addCommand(
                        monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter,
                        function () { dotnetRef.invokeMethodAsync('OnEditorActionShortcut', 'run-and-stay'); }
                    );
                    editor.addCommand(
                        monaco.KeyMod.Alt | monaco.KeyCode.Enter,
                        function () { dotnetRef.invokeMethodAsync('OnEditorActionShortcut', 'run-and-insert-below'); }
                    );
                    editor.addCommand(
                        monaco.KeyCode.Escape,
                        function () { dotnetRef.invokeMethodAsync('OnEditorActionShortcut', 'escape'); }
                    );
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
            const editor = editors[elementId];
            if (editor) {
                const modelUri = editor.getModel()?.uri?.toString();
                if (modelUri) {
                    delete dotnetRefs[modelUri];
                }
                editor.dispose();
                delete editors[elementId];
            }
        },

        getValue: function (elementId) {
            const editor = editors[elementId];
            return editor ? editor.getValue() : '';
        },

        setValue: function (elementId, value) {
            const editor = editors[elementId];
            if (editor && editor.getValue() !== value) {
                editor.setValue(value);
            }
        },

        setLanguage: function (elementId, language) {
            const editor = editors[elementId];
            if (editor) {
                const model = editor.getModel();
                if (model && monaco.editor.getModel(model.uri)) {
                    monaco.editor.setModelLanguage(model, language);
                    registerProviders(language);
                }
            }
        },

        setTheme: function (theme) {
            _currentTheme = theme || 'vs';
            if (monacoReady) {
                monaco.editor.setTheme(_currentTheme);
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
            if (editor) {
                editor.focus();
            }
        }
    };
})();
