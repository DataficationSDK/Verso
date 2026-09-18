/**
 * VS Code Webview ↔ Blazor WASM bridge.
 * Sends JSON-RPC requests via postMessage and resolves responses.
 * Also dispatches host notifications to registered .NET callbacks.
 */
(function () {
    "use strict";

    const vscode = typeof acquireVsCodeApi === "function" ? acquireVsCodeApi() : null;
    let nextId = 1;
    const pending = new Map(); // id → { resolve, reject }
    let notificationCallback = null; // DotNetObjectReference for notifications
    const pendingNotifications = []; // queued before handler is registered
    const pendingResponses = []; // queued before handler is registered

    /**
     * Send a JSON-RPC request to the VS Code extension host.
     * Returns a promise that resolves with the result or rejects with an error.
     * @param {string} method - The JSON-RPC method name
     * @param {string} paramsJson - Serialized JSON params
     * @returns {Promise<string>} - Serialized JSON result
     */
    function sendRequest(method, paramsJson) {
        return new Promise((resolve, reject) => {
            const id = nextId++;
            pending.set(id, { resolve, reject });

            const message = {
                type: "jsonrpc-request",
                id: id,
                method: method,
                params: paramsJson ? JSON.parse(paramsJson) : null
            };

            if (vscode) {
                vscode.postMessage(message);
            } else {
                // Not in VS Code — reject immediately
                pending.delete(id);
                reject(new Error("Not running inside a VS Code webview."));
            }
        });
    }

    /**
     * Send a JSON-RPC request and return immediately. The eventual response is
     * delivered to .NET through OnResponse. This avoids keeping a long-running
     * .NET→JS interop promise open while execution notifications are streaming.
     * @param {number} id - JSON-RPC request id allocated by .NET
     * @param {string} method - The JSON-RPC method name
     * @param {string} paramsJson - Serialized JSON params
     */
    function sendRequestDetached(id, method, paramsJson) {
        const message = {
            type: "jsonrpc-request",
            id: id,
            method: method,
            params: paramsJson ? JSON.parse(paramsJson) : null
        };

        if (vscode) {
            vscode.postMessage(message);
        } else {
            throw new Error("Not running inside a VS Code webview.");
        }
    }

    /**
     * Register a .NET object reference to receive host notifications.
     * The object must have an InvokeMethodAsync-compatible method named "OnNotification".
     * @param {object} dotNetRef - DotNetObjectReference
     */
    function registerNotificationHandler(dotNetRef) {
        notificationCallback = dotNetRef;
        // Replay any notifications that arrived before the handler was registered
        while (pendingNotifications.length > 0) {
            var n = pendingNotifications.shift();
            notificationCallback.invokeMethodAsync("OnNotification", n.method, n.params);
        }

        while (pendingResponses.length > 0) {
            var r = pendingResponses.shift();
            notificationCallback.invokeMethodAsync("OnResponse", r.id, r.result, r.error);
        }
    }

    /**
     * Check whether the bridge is running inside a VS Code webview.
     * @returns {boolean}
     */
    function isVsCodeWebview() {
        return vscode !== null;
    }

    /**
     * Returns "dark" or "light" based on the VS Code webview body class.
     * VS Code sets vscode-dark / vscode-light / vscode-high-contrast on <body>.
     * @returns {string}
     */
    function getThemeKind() {
        if (document.body.classList.contains("vscode-dark") ||
            document.body.classList.contains("vscode-high-contrast")) {
            return "dark";
        }
        return "light";
    }

    // Editor colors the notebook's Monaco editors take from the VS Code theme. Monaco and
    // VS Code share their color ids, and VS Code publishes every one of them on the page
    // as a CSS variable ("editor.background" as --vscode-editor-background), so the
    // colors can be read here with no help from the extension host.
    const MONACO_COLOR_IDS = [
        "focusBorder", "foreground",
        "editor.background", "editor.foreground",
        "editor.selectionBackground", "editor.selectionForeground",
        "editor.inactiveSelectionBackground", "editor.selectionHighlightBackground",
        "editor.lineHighlightBackground", "editor.lineHighlightBorder",
        "editor.wordHighlightBackground", "editor.wordHighlightStrongBackground",
        "editor.findMatchBackground", "editor.findMatchHighlightBackground",
        "editor.findRangeHighlightBackground", "editor.hoverHighlightBackground",
        "editorCursor.foreground", "editorWhitespace.foreground",
        "editorLineNumber.foreground", "editorLineNumber.activeForeground",
        "editorIndentGuide.background1", "editorIndentGuide.activeBackground1",
        "editorBracketMatch.background", "editorBracketMatch.border",
        "editorBracketHighlight.foreground1", "editorBracketHighlight.foreground2",
        "editorBracketHighlight.foreground3", "editorBracketHighlight.foreground4",
        "editorBracketHighlight.foreground5", "editorBracketHighlight.foreground6",
        "editorBracketHighlight.unexpectedBracket.foreground",
        "editorGutter.background", "editorLink.activeForeground",
        "editorError.foreground", "editorWarning.foreground", "editorInfo.foreground",
        "editorHint.foreground", "editorUnnecessaryCode.opacity",
        "editorGhostText.foreground",
        "editorWidget.background", "editorWidget.foreground", "editorWidget.border",
        "editorSuggestWidget.background", "editorSuggestWidget.border",
        "editorSuggestWidget.foreground", "editorSuggestWidget.selectedBackground",
        "editorSuggestWidget.selectedForeground", "editorSuggestWidget.highlightForeground",
        "editorSuggestWidget.focusHighlightForeground",
        "editorHoverWidget.background", "editorHoverWidget.foreground",
        "editorHoverWidget.border", "editorHoverWidget.statusBarBackground",
        "diffEditor.insertedTextBackground", "diffEditor.removedTextBackground",
        "diffEditor.insertedLineBackground", "diffEditor.removedLineBackground",
        "diffEditor.diagonalFill",
        "input.background", "input.foreground", "input.border",
        "inputOption.activeBorder", "inputOption.activeBackground",
        "list.hoverBackground", "list.activeSelectionBackground",
        "list.activeSelectionForeground", "list.highlightForeground",
        "scrollbar.shadow", "scrollbarSlider.background",
        "scrollbarSlider.hoverBackground", "scrollbarSlider.activeBackground",
        "textLink.foreground", "textCodeBlock.background", "widget.shadow"
    ];

    // Syntax rules for the active theme, read from the theme's file by the extension host:
    // window.__versoTokenTheme holds the ones the page was opened with, and this the ones
    // from the latest theme change, if there has been one. Empty rules mean the file
    // could not be read, in which case Monaco's stock syntax colors stand in.
    let tokenTheme = null;
    let appliedSignature = null;

    function getBaseTheme() {
        const c = document.body.classList;
        if (c.contains("vscode-high-contrast-light")) return "hc-light";
        if (c.contains("vscode-high-contrast")) return "hc-black";
        return c.contains("vscode-dark") ? "vs-dark" : "vs";
    }

    /**
     * Themes the Monaco editors from the active VS Code color theme: the editor surface
     * from the page's --vscode-* variables, the syntax colors from the extension host.
     */
    function applyMonacoTheme() {
        if (!window.versoMonaco || typeof window.versoMonaco.applyTheme !== "function") return;

        const style = getComputedStyle(document.documentElement);
        const colors = {};
        MONACO_COLOR_IDS.forEach(function (id) {
            const value = style.getPropertyValue("--vscode-" + id.replace(/\./g, "-")).trim();
            if (value) colors[id] = value;
        });

        // With the theme's own rules in hand, the stock ones are left out: a token the
        // theme says nothing about should fall to the editor's text color, as it does
        // in VS Code, not to a color from a palette the user never chose.
        const active = tokenTheme || window.__versoTokenTheme;
        const rules = (active && active.rules) || [];
        const spec = {
            base: getBaseTheme(),
            inherit: rules.length === 0,
            colors: colors,
            rules: rules
        };

        // The page is watched for changes of any kind, most of which are not the theme.
        const signature = JSON.stringify(spec);
        if (signature === appliedSignature) return;
        appliedSignature = signature;
        window.versoMonaco.applyTheme(spec);
    }

    // VS Code rewrites the page's variables when the theme changes, and also when the
    // user edits "workbench.colorCustomizations", which no theme event reports. Watching
    // the page itself covers both, and runs after the new values are in place.
    (function watchPageTheme() {
        if (typeof MutationObserver !== "function") return;
        let scheduled = false;
        const observer = new MutationObserver(function () {
            if (scheduled) return;
            scheduled = true;
            setTimeout(function () { scheduled = false; applyMonacoTheme(); }, 0);
        });
        const start = function () {
            observer.observe(document.documentElement, { attributes: true, attributeFilter: ["style", "class"] });
            observer.observe(document.body, { attributes: true, attributeFilter: ["class"] });
            // Every script is in by now, so the first editor is created already themed.
            applyMonacoTheme();
        };
        if (document.body) start();
        else document.addEventListener("DOMContentLoaded", start);
    })();

    // Listen for messages from the VS Code extension host
    window.addEventListener("message", function (event) {
        const msg = event.data;
        if (!msg || !msg.type) return;

        if (msg.type === "jsonrpc-response") {
            const entry = pending.get(msg.id);
            if (entry) {
                pending.delete(msg.id);

                if (msg.error) {
                    entry.reject(new Error(msg.error.message || "JSON-RPC error"));
                } else {
                    entry.resolve(JSON.stringify(msg.result));
                }
            } else {
                const result = msg.error ? null : JSON.stringify(msg.result);
                const error = msg.error ? JSON.stringify(msg.error) : null;
                if (notificationCallback) {
                    notificationCallback.invokeMethodAsync("OnResponse", msg.id, result, error);
                } else {
                    pendingResponses.push({ id: msg.id, result: result, error: error });
                }
            }
        } else if (msg.type === "editor-settings-changed") {
            if (window.versoMonaco && typeof window.versoMonaco.updateEditorSettings === "function") {
                window.versoMonaco.updateEditorSettings(msg.settings);
            }
        } else if (msg.type === "theme-kind-changed") {
            if (msg.tokenTheme) tokenTheme = msg.tokenTheme;
            applyMonacoTheme();
            // Tell .NET the theme changed so isolated layout frames are re-sent the
            // current theme. Defer one frame so the webview's --vscode-* custom
            // properties have settled before the frame interop reads them.
            if (notificationCallback) {
                const cb = notificationCallback;
                const fire = function () {
                    try {
                        cb.invokeMethodAsync(
                            "OnNotification",
                            "theme/vscodeKindChanged",
                            JSON.stringify({ kind: msg.kind }));
                    } catch (_) { /* handler may have detached */ }
                };
                if (typeof requestAnimationFrame === "function") {
                    requestAnimationFrame(fire);
                } else {
                    setTimeout(fire, 0);
                }
            }
        } else if (msg.type === "jsonrpc-notification") {
            var method = msg.method;
            var params = msg.params ? JSON.stringify(msg.params) : null;
            if (notificationCallback) {
                notificationCallback.invokeMethodAsync("OnNotification", method, params);
            } else {
                // Queue for replay when the .NET handler registers
                pendingNotifications.push({ method: method, params: params });
            }
        }
    });

    // Expose to Blazor JS interop
    window.vscodeBridge = {
        sendRequest: sendRequest,
        sendRequestDetached: sendRequestDetached,
        registerNotificationHandler: registerNotificationHandler,
        isVsCodeWebview: isVsCodeWebview,
        getThemeKind: getThemeKind,
        applyMonacoTheme: applyMonacoTheme
    };
})();
