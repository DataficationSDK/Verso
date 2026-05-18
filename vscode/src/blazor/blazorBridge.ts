import * as vscode from "vscode";
import { HostProcess } from "../host/hostProcess";
import { log } from "../log";

/**
 * Bridges communication between the Blazor WASM webview and the Verso.Host process.
 *
 * Blazor WASM → postMessage { type: "jsonrpc-request", id, method, params }
 *   → HostProcess.sendRequest(method, params)
 *   → postMessage { type: "jsonrpc-response", id, result/error }
 *
 * Host notifications → postMessage { type: "jsonrpc-notification", method, params }
 *
 * Some methods are intercepted and handled directly by the bridge:
 *   - "extension/writeFile" — writes content to the document URI via VS Code API
 *
 * Some host notifications are handled by the bridge instead of forwarded:
 *   - "file/download" — shows a save dialog and writes the file
 *   - "input/request" — asks VS Code for user input and replies to the host
 */
export class BlazorBridge implements vscode.Disposable {
  private readonly disposables: vscode.Disposable[] = [];
  private readonly notificationMethods = [
    "cell/executionState",
    "settings/changed",
    "variable/changed",
    "output/update",
    "extension/consentRequest",
    "extension/changed",
    "layout/missing",
  ];

  private static readonly mutationMethods = new Set([
    "cell/add",
    "cell/insert",
    "cell/remove",
    "cell/move",
    "cell/updateSource",
    "cell/changeType",
    "cell/changeLanguage",
    "notebook/setDefaultKernel",
    "execution/run",
    "execution/runAll",
    "output/clearAll",
    "properties/updateProperty",
  ]);

  private documentUri: vscode.Uri | undefined;
  private notebookId: string | undefined;
  private readonly webview: vscode.Webview;
  private host: HostProcess;

  /**
   * Set while a host restart is in progress. Webview-originated requests await
   * this promise before being forwarded so they target the new host once it is
   * ready. Provider-driven requests (snapshot capture, re-open) bypass the gate
   * because they call {@link HostProcess.sendRequest} directly via {@link getHost}.
   */
  private restartInFlight: Promise<void> | undefined;

  /** Callback fired when the webview sends a request that mutates the notebook. */
  onDidEdit: (() => void) | undefined;

  /**
   * Callback fired when a kernel restart is requested, either via the
   * `kernel/restart` JSON-RPC method from the toolbar or via a
   * `kernel/restartRequested` notification from the host (e.g. `#!restart`
   * magic command). The provider owns the kill+respawn lifecycle.
   */
  onRestartRequested: ((kernelId: string | undefined) => Promise<void>) | undefined;

  constructor(
    webview: vscode.Webview,
    host: HostProcess,
    private readonly globalState?: vscode.Memento
  ) {
    this.webview = webview;
    this.host = host;

    // Listen for messages from the webview (Blazor WASM)
    this.disposables.push(
      webview.onDidReceiveMessage(async (msg) => {
        if (msg.type === "jsonrpc-request") {
          await this.handleWebviewRequest(msg.id, msg.method, msg.params);
        }
      })
    );

    this.subscribeHostNotifications();
  }

  /**
   * Subscribes notification handlers against the current {@link host}. Called
   * from the constructor and again from {@link setHost} after a restart so the
   * fresh process pipes notifications back to the webview.
   */
  private subscribeHostNotifications(): void {
    for (const method of this.notificationMethods) {
      this.host.onNotification(method, (params) => {
        this.webview.postMessage({
          type: "jsonrpc-notification",
          method,
          params,
        });
      });
    }

    this.host.onNotification("file/download", (params) => {
      this.handleFileDownload(params).catch((err) => {
        log.error(`file/download error: ${err instanceof Error ? err.message : String(err)}`);
        vscode.window.showErrorMessage(
          `Export failed: ${err instanceof Error ? err.message : String(err)}`
        );
      });
    });

    this.host.onNotification("kernel/restartRequested", (params) => {
      const p = params as { kernelId?: string } | undefined;
      this.triggerRestart(p?.kernelId);
    });

    this.host.onNotification("input/request", (params) => {
      this.handleInputRequest(params).catch((err) => {
        log.error(`input/request error: ${err instanceof Error ? err.message : String(err)}`);
        vscode.window.showErrorMessage(
          `Input request failed: ${err instanceof Error ? err.message : String(err)}`
        );
      });
    });
  }

  /**
   * Routes a restart request through the provider's lifecycle handler. Errors
   * are logged here because the call site is fire-and-forget (notification
   * handler) and we do not want unhandled rejections.
   */
  private triggerRestart(kernelId: string | undefined): void {
    if (this.onRestartRequested === undefined) {
      log.warn("kernel restart requested but no provider handler is registered");
      return;
    }
    this.onRestartRequested(kernelId).catch((err) => {
      log.error(`kernel restart failed: ${err instanceof Error ? err.message : String(err)}`);
    });
  }

  /**
   * Marks the bridge as gated for the duration of an in-flight restart. The
   * provider calls {@link beginRestart} before tearing down the host and
   * {@link endRestart} once the new host is ready. Webview requests received
   * between those two calls await the restart promise before being forwarded.
   */
  beginRestart(): void {
    if (this.restartInFlight !== undefined) return;
    let resolveFn!: () => void;
    this.restartInFlight = new Promise<void>((resolve) => {
      resolveFn = resolve;
    });
    (this.restartInFlight as Promise<void> & { __resolve?: () => void }).__resolve = resolveFn;
  }

  endRestart(): void {
    if (this.restartInFlight === undefined) return;
    const promise = this.restartInFlight as Promise<void> & { __resolve?: () => void };
    this.restartInFlight = undefined;
    promise.__resolve?.();
  }

  /** Whether a host restart is in progress. */
  get isRestarting(): boolean {
    return this.restartInFlight !== undefined;
  }

  /**
   * Posts a notification to the webview signaling that a kernel restart has
   * begun. The WASM app shows a status banner.
   */
  notifyRestarting(kernelId: string | undefined): void {
    this.webview.postMessage({
      type: "jsonrpc-notification",
      method: "kernel/restarting",
      params: { kernelId },
    });
  }

  /**
   * Posts a notification to the webview signaling that the kernel restart is
   * complete. The WASM app clears execution badges and the variable inspector
   * and updates the status banner.
   */
  notifyRestarted(kernelId: string | undefined): void {
    this.webview.postMessage({
      type: "jsonrpc-notification",
      method: "kernel/restarted",
      params: { kernelId },
    });
  }

  /**
   * Swaps the underlying host process after a restart and re-binds notification
   * handlers against the new process. The provider calls this once the new
   * {@link HostProcess} is started and the notebook has been reopened.
   */
  setHost(host: HostProcess): void {
    this.host = host;
    this.subscribeHostNotifications();
  }

  /**
   * Returns the currently bound host. The provider needs this so it can call
   * {@link HostProcess.sendRequest} for the snapshot capture before disposal.
   */
  getHost(): HostProcess {
    return this.host;
  }

  /**
   * Set the document URI so the bridge knows where to write on save.
   */
  setDocumentUri(uri: vscode.Uri): void {
    this.documentUri = uri;
  }

  /**
   * Get the document URI for this editor session.
   */
  getDocumentUri(): vscode.Uri | undefined {
    return this.documentUri;
  }

  /**
   * Set the notebookId assigned by the host for this editor session.
   */
  setNotebookId(id: string): void {
    this.notebookId = id;
  }

  /**
   * Get the notebookId for this editor session.
   */
  getNotebookId(): string | undefined {
    return this.notebookId;
  }

  /**
   * Handle a JSON-RPC request from the webview. Methods prefixed with
   * "extension/" are handled directly; all others are forwarded to the host.
   */
  private async handleWebviewRequest(
    id: number,
    method: string,
    params: unknown
  ): Promise<void> {
    try {
      let result: unknown;

      if (method === "extension/writeFile") {
        // The WASM app triggers save via this method. Route through VS Code's
        // save command so the CustomEditorProvider clears the dirty indicator.
        await vscode.commands.executeCommand("workbench.action.files.save");
        result = { success: true };
      } else if (method === "userPrefs/getDisabledExtensions") {
        const ids =
          this.globalState?.get<string[]>("verso.disabledExtensions") ?? null;
        result = { ids };
      } else if (method === "userPrefs/setDisabledExtensions") {
        const p = params as { ids?: string[] } | undefined;
        await this.globalState?.update(
          "verso.disabledExtensions",
          p?.ids ?? []
        );
        result = { success: true };
      } else if (method === "kernel/restart") {
        // The toolbar action and #!restart magic command both reach the host as
        // kernel/restart, but the in-process restart cannot release pinned DLL
        // handles on Windows. Intercept here so the provider can kill and respawn
        // the host. The original webview request is acknowledged after the
        // restart completes, matching the prior `{ success: true }` shape.
        const p = params as { kernelId?: string } | undefined;
        if (this.onRestartRequested === undefined) {
          throw new Error("kernel restart handler not registered on bridge");
        }
        await this.onRestartRequested(p?.kernelId);
        result = { success: true };
      } else {
        // Gate webview requests during an in-flight restart so they target the
        // new host once it is ready. Provider-driven sendRequest calls (for the
        // snapshot capture and notebook re-open) bypass this because they go
        // through getHost().sendRequest directly.
        if (this.restartInFlight !== undefined) {
          await this.restartInFlight;
        }

        // Inject notebookId into the forwarded request params
        const enrichedParams =
          this.notebookId && params && typeof params === "object"
            ? { ...(params as Record<string, unknown>), notebookId: this.notebookId }
            : this.notebookId
              ? { notebookId: this.notebookId }
              : params;
        result = await this.host.sendRequest(method, enrichedParams);

        // Notify the provider that the document was mutated.
        if (BlazorBridge.mutationMethods.has(method)) {
          this.onDidEdit?.();
        }
      }

      this.webview.postMessage({
        type: "jsonrpc-response",
        id,
        result,
      });
    } catch (err) {
      this.webview.postMessage({
        type: "jsonrpc-response",
        id,
        error: {
          code: -32603,
          message: err instanceof Error ? err.message : String(err),
        },
      });
    }
  }

  /**
   * Handle "extension/writeFile" — write serialized notebook content to the document URI.
   */
  private async handleWriteFile(
    params: unknown
  ): Promise<{ success: boolean }> {
    const p = params as { content?: string; filePath?: string } | undefined;
    const content = p?.content;
    if (!content) {
      throw new Error("Missing content for extension/writeFile");
    }

    const uri = this.documentUri;
    if (!uri) {
      throw new Error("No document URI available for save");
    }

    const data = new TextEncoder().encode(content);
    await vscode.workspace.fs.writeFile(uri, data);
    return { success: true };
  }

  /**
   * Handle "file/download" notification — show a save dialog and write the file.
   */
  private async handleFileDownload(params: unknown): Promise<void> {
    const p = params as
      | { fileName?: string; contentType?: string; data?: string }
      | undefined;
    if (!p?.fileName || !p.data) {
      return;
    }

    const defaultUri = this.documentUri
      ? vscode.Uri.joinPath(this.documentUri, "..", p.fileName)
      : vscode.Uri.file(p.fileName);

    const uri = await vscode.window.showSaveDialog({
      defaultUri,
      filters: this.getFileFilters(p.contentType, p.fileName),
    });

    if (!uri) {
      return; // User cancelled
    }

    const bytes = Buffer.from(p.data, "base64");
    await vscode.workspace.fs.writeFile(uri, bytes);
    vscode.window.showInformationMessage(`Exported to ${uri.fsPath}`);
  }

  /**
   * Handle "input/request" notification — prompt through VS Code and reply
   * directly to the host. The reply bypasses the host's sequential execution
   * queue so a running execution can continue.
   */
  private async handleInputRequest(params: unknown): Promise<void> {
    const p = params as
      | {
          notebookId?: string;
          requestId?: string;
          prompt?: string;
          isPassword?: boolean;
        }
      | undefined;

    if (!p?.requestId) {
      return;
    }

    const notebookId = p.notebookId ?? this.notebookId;
    if (!notebookId) {
      throw new Error("No notebookId available for input response.");
    }

    const value = await vscode.window.showInputBox({
      prompt: p.prompt || "PowerShell input",
      password: !!p.isPassword,
      ignoreFocusOut: true,
    });

    await this.host.sendRequest("input/response", {
      notebookId,
      requestId: p.requestId,
      value: value ?? null,
      cancelled: value === undefined,
    });
  }

  /**
   * Build file filter map from content type and file name.
   */
  private getFileFilters(
    contentType?: string,
    fileName?: string
  ): Record<string, string[]> {
    const ext = fileName?.split(".").pop()?.toLowerCase();
    switch (contentType) {
      case "text/csv":
        return { "CSV Files": ["csv"], "All Files": ["*"] };
      case "application/json":
        return { "JSON Files": ["json"], "All Files": ["*"] };
      case "text/html":
        return { "HTML Files": ["html", "htm"], "All Files": ["*"] };
      case "text/markdown":
        return { "Markdown Files": ["md"], "All Files": ["*"] };
      default:
        if (ext) {
          return { Files: [ext], "All Files": ["*"] };
        }
        return { "All Files": ["*"] };
    }
  }

  /**
   * Mark the document as dirty. Called by external callers (e.g. Copilot
   * participant) that mutate the notebook by calling the host directly
   * rather than going through the webview request flow.
   */
  markDirty(): void {
    this.onDidEdit?.();
  }

  /**
   * Send a notification to the webview (e.g. when the notebook is opened).
   */
  notify(method: string, params?: unknown): void {
    this.webview.postMessage({
      type: "jsonrpc-notification",
      method,
      params,
    });
  }

  /**
   * Push updated VS Code editor settings to the webview's Monaco editors.
   */
  postEditorSettings(settings: {
    fontSize: number;
    fontFamily: string;
    fontLigatures: boolean | string;
  }): void {
    this.webview.postMessage({
      type: "editor-settings-changed",
      settings,
    });
  }

  /**
   * Push a theme kind change to the webview so Monaco editors switch
   * between light and dark themes when the VS Code color theme changes.
   */
  postThemeKind(kind: "dark" | "light"): void {
    this.webview.postMessage({
      type: "theme-kind-changed",
      kind,
    });
  }

  dispose(): void {
    for (const d of this.disposables) {
      d.dispose();
    }
  }
}
