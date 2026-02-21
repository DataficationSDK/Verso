import * as vscode from "vscode";
import { HostProcess } from "../host/hostProcess";

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
 */
export class BlazorBridge implements vscode.Disposable {
  private readonly disposables: vscode.Disposable[] = [];
  private readonly notificationMethods = [
    "cell/executionState",
    "settings/changed",
    "variable/changed",
  ];

  private static readonly mutationMethods = new Set([
    "cell/add",
    "cell/insert",
    "cell/remove",
    "cell/move",
    "cell/updateSource",
    "execution/run",
    "execution/runAll",
    "output/clearAll",
  ]);

  private documentUri: vscode.Uri | undefined;

  /** Callback fired when the webview sends a request that mutates the notebook. */
  onDidEdit: (() => void) | undefined;

  constructor(
    private readonly webview: vscode.Webview,
    private readonly host: HostProcess
  ) {
    // Listen for messages from the webview (Blazor WASM)
    this.disposables.push(
      webview.onDidReceiveMessage(async (msg) => {
        if (msg.type === "jsonrpc-request") {
          await this.handleWebviewRequest(msg.id, msg.method, msg.params);
        }
      })
    );

    // Forward host notifications to the webview
    for (const method of this.notificationMethods) {
      host.onNotification(method, (params) => {
        this.webview.postMessage({
          type: "jsonrpc-notification",
          method,
          params,
        });
      });
    }

    // Handle file download notifications from the host (export actions)
    host.onNotification("file/download", (params) => {
      this.handleFileDownload(params).catch((err) => {
        console.error("[BlazorBridge] file/download error:", err);
        vscode.window.showErrorMessage(
          `Export failed: ${err instanceof Error ? err.message : String(err)}`
        );
      });
    });
  }

  /**
   * Set the document URI so the bridge knows where to write on save.
   */
  setDocumentUri(uri: vscode.Uri): void {
    this.documentUri = uri;
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
      } else {
        result = await this.host.sendRequest(method, params);

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

    const uri = await vscode.window.showSaveDialog({
      defaultUri: vscode.Uri.file(p.fileName),
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
   * Send a notification to the webview (e.g. when the notebook is opened).
   */
  notify(method: string, params?: unknown): void {
    this.webview.postMessage({
      type: "jsonrpc-notification",
      method,
      params,
    });
  }

  dispose(): void {
    for (const d of this.disposables) {
      d.dispose();
    }
  }
}
