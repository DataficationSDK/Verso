import * as vscode from "vscode";

/**
 * Maps notebook document URIs to their host-assigned notebookId.
 * This allows any component to look up the notebookId for a given notebook.
 */
class NotebookRegistry {
  private readonly uriToId = new Map<string, string>();

  register(uri: vscode.Uri, notebookId: string): void {
    this.uriToId.set(uri.toString(), notebookId);
  }

  unregister(uri: vscode.Uri): string | undefined {
    const key = uri.toString();
    const id = this.uriToId.get(key);
    this.uriToId.delete(key);
    return id;
  }

  getByUri(uri: vscode.Uri): string | undefined {
    return this.uriToId.get(uri.toString());
  }
}

export const notebookRegistry = new NotebookRegistry();
