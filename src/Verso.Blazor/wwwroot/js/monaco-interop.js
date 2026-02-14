// Monaco Editor JS interop for Verso.Blazor
window.versoMonaco = (function () {
    const editors = {};
    let monacoReady = false;
    let readyCallbacks = [];
    let _currentTheme = 'vs';

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
                editors[elementId] = editor;
            });
        },

        dispose: function (elementId) {
            const editor = editors[elementId];
            if (editor) {
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
                }
            }
        },

        setTheme: function (theme) {
            _currentTheme = theme || 'vs';
            if (monacoReady) {
                monaco.editor.setTheme(_currentTheme);
            }
        }
    };
})();
