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

/**
 * Resolves the notebookId for a cell document by finding its parent notebook.
 */
export function resolveNotebookIdForCell(
  document: vscode.TextDocument
): string | undefined {
  for (const nb of vscode.workspace.notebookDocuments) {
    if (nb.notebookType !== "verso-notebook") {
      continue;
    }
    const cell = nb
      .getCells()
      .find((c) => c.document.uri.toString() === document.uri.toString());
    if (cell) {
      return notebookRegistry.getByUri(nb.uri);
    }
  }
  return undefined;
}

/**
 * Gets the notebookId for the active notebook editor.
 */
export function getActiveNotebookId(): string | undefined {
  const notebook = vscode.window.activeNotebookEditor?.notebook;
  if (!notebook || notebook.notebookType !== "verso-notebook") {
    return undefined;
  }
  return notebookRegistry.getByUri(notebook.uri);
}
