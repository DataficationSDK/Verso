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

    // Listen for messages from the VS Code extension host
    window.addEventListener("message", function (event) {
        const msg = event.data;
        if (!msg || !msg.type) return;

        if (msg.type === "jsonrpc-response") {
            const entry = pending.get(msg.id);
            if (!entry) return;
            pending.delete(msg.id);

            if (msg.error) {
                entry.reject(new Error(msg.error.message || "JSON-RPC error"));
            } else {
                entry.resolve(JSON.stringify(msg.result));
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
        registerNotificationHandler: registerNotificationHandler,
        isVsCodeWebview: isVsCodeWebview,
        getThemeKind: getThemeKind
    };
})();
