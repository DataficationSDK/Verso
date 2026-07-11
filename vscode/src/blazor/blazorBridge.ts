import * as vscode from "vscode";
import { HostProcess } from "../host/hostProcess";
import { GitBaselineProvider } from "../git/gitBaselineProvider";
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
    "extension/unavailable",
    "kernel/restarting",
    "kernel/restarted",
    "kernel/faulted",
    "layout/missing",
    "layout/updated",
    "layout/frameMessage",
    "notebook/cellsChanged",
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
    "output/clearAll",
    "properties/updateProperty",
    // Installing or removing an extension rewrites the notebook's required-extensions
    // list, so the document must be marked dirty just like a cell edit.
    "extension/install",
    "extension/installLocal",
    "extension/uninstall",
  ]);

  // Methods that change persisted state only sometimes: executing a cell dirties the document
  // only if that cell type persists its outputs, and a cell interaction dirties it only when it
  // edits persisted state (e.g. a parameter definition). The host reports the decision as a
  // `dirty` hint on the response, so these are gated on the response rather than the method name.
  private static readonly responseDirtyMethods = new Set([
    "execution/run",
    "execution/runAll",
    "cell/interact",
  ]);

  private static responseIndicatesDirty(result: unknown): boolean {
    if (result === null || typeof result !== "object") {
      return false;
    }
    const r = result as { dirty?: boolean; results?: Array<{ dirty?: boolean }> };
    if (Array.isArray(r.results)) {
      return r.results.some((x) => x?.dirty === true);
    }
    return r.dirty === true;
  }

  private documentUri: vscode.Uri | undefined;
  private notebookId: string | undefined;
  private readonly webview: vscode.Webview;
  private host: HostProcess;
  private readonly gitProvider = new GitBaselineProvider();

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

    // Fire-and-forget: the git API is only needed once the user opens the
    // Compare menu, and every failure downgrades to "git sources unavailable".
    void this.gitProvider.activate();
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
   * Posts a notification to the webview signaling that a kernel restart failed
   * and the kernel is now unavailable. The WASM app resolves its status pill to
   * an error state carrying {@link message} so it never hangs on "Restarting…".
   * The property name is `message` to match the webview's kernel/faulted handler.
   */
  notifyFaulted(message: string): void {
    this.webview.postMessage({
      type: "jsonrpc-notification",
      method: "kernel/faulted",
      params: { message },
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
      } else if (method === "extension/browseLocalFile") {
        // Show VS Code's native open dialog for a sideloaded extension file. The chosen
        // path is returned to the webview, which then calls extension/installLocal so the
        // host reads the file directly from disk. A cancelled dialog returns a null path.
        const picked = await vscode.window.showOpenDialog({
          canSelectMany: false,
          openLabel: "Install Extension",
          filters: { "Extensions": ["dll", "nupkg"] },
        });
        result = { path: picked?.[0]?.fsPath ?? null };
      } else if (method === "diff/sources") {
        // Read-only: lists the baselines this notebook can be compared against.
        // Deliberately NOT in mutationMethods; comparing must never dirty the document.
        result = this.listDiffSources();
      } else if (method === "diff/baseline") {
        // Read-only: resolves a baseline's content via native pickers and the git API.
        const p = params as { sourceId?: string } | undefined;
        result = await this.resolveDiffBaseline(p?.sourceId);
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
        } else if (
          BlazorBridge.responseDirtyMethods.has(method) &&
          BlazorBridge.responseIndicatesDirty(result)
        ) {
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

  /** Baseline sources for the Compare menu, with git entries gated on repo membership. */
  listDiffSources(): {
    sources: Array<{
      id: string;
      label: string;
      kind: string;
      available: boolean;
      description: string | null;
    }>;
  } {
    const uri = this.documentUri;
    const hasUri = uri !== undefined;
    const gitAvailable = hasUri && this.gitProvider.isAvailableFor(uri);
    const notInRepo = "The notebook file is not inside a git repository.";
    return {
      sources: [
        {
          id: "lastSaved",
          label: "Last Saved",
          kind: "lastSaved",
          available: hasUri,
          description: hasUri ? null : "The notebook has no file on disk yet.",
        },
        {
          id: "gitHead",
          label: "Git: HEAD",
          kind: "git",
          available: gitAvailable,
          description: gitAvailable ? null : notInRepo,
        },
        {
          id: "gitRef",
          label: "Git: Compare with Ref...",
          kind: "git",
          available: gitAvailable,
          description: gitAvailable ? null : notInRepo,
        },
        {
          id: "file",
          label: "Choose File...",
          kind: "file",
          available: true,
          description: null,
        },
      ],
    };
  }

  /**
   * Resolves a comparison baseline's content. Pickers (ref quick pick, file dialog) run
   * natively here; a dismissed picker reports `cancelled` rather than an error.
   */
  private async resolveDiffBaseline(
    sourceId: string | undefined
  ): Promise<
    | { content: string; filePath?: string; label: string }
    | { cancelled: true }
  > {
    const uri = this.documentUri;
    switch (sourceId) {
      case "lastSaved": {
        if (!uri) {
          throw new Error("The notebook has no file on disk yet.");
        }
        const bytes = await vscode.workspace.fs.readFile(uri);
        return {
          content: new TextDecoder().decode(bytes),
          filePath: uri.fsPath,
          label: "Last Saved",
        };
      }

      case "gitHead": {
        if (!uri) {
          throw new Error("The notebook has no file on disk yet.");
        }
        const content = await this.gitProvider.showAtRef(uri, "HEAD");
        return { content, filePath: uri.fsPath, label: "Git: HEAD" };
      }

      case "gitRef": {
        if (!uri) {
          throw new Error("The notebook has no file on disk yet.");
        }
        const typedItem = {
          label: "Type a ref or commit...",
          description: "",
          ref: "__typed__",
        };
        const picked = await vscode.window.showQuickPick(
          [...this.gitProvider.listRefsForQuickPick(uri), typedItem],
          { placeHolder: "Compare notebook with..." }
        );
        if (!picked) {
          return { cancelled: true };
        }
        const ref =
          picked.ref === "__typed__"
            ? await vscode.window.showInputBox({
                prompt: "Git branch, tag, or commit SHA",
                placeHolder: "main",
              })
            : picked.ref;
        if (!ref) {
          return { cancelled: true };
        }
        const content = await this.gitProvider.showAtRef(uri, ref);
        return { content, filePath: uri.fsPath, label: `Git: ${ref}` };
      }

      case "file": {
        const picked = await vscode.window.showOpenDialog({
          canSelectMany: false,
          openLabel: "Compare",
          filters: { "Notebook Files": ["verso", "ipynb", "dib"] },
        });
        const file = picked?.[0];
        if (!file) {
          return { cancelled: true };
        }
        const bytes = await vscode.workspace.fs.readFile(file);
        return {
          content: new TextDecoder().decode(bytes),
          filePath: file.fsPath,
          label: file.path.split("/").pop() ?? "File",
        };
      }

      default:
        throw new Error(`Unknown comparison source '${sourceId}'.`);
    }
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
