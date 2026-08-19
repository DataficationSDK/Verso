import * as vscode from "vscode";
import { HostProcess } from "./hostProcess";
import { BlazorBridge } from "../blazor/blazorBridge";

/**
 * Maps notebook document URIs to their HostProcess and BlazorBridge instances.
 * This allows the Copilot participant (and other components) to reach
 * the host process and bridge for a given open notebook.
 */

export interface NotebookSession {
  host: HostProcess;
  bridge: BlazorBridge;
}

class HostRegistry {
  private readonly uriToSession = new Map<string, NotebookSession>();
  private notebookOpen = false;

  register(uri: vscode.Uri, session: NotebookSession): void {
    this.uriToSession.set(uri.toString(), session);
    this.syncContextKey();
  }

  unregister(uri: vscode.Uri): void {
    this.uriToSession.delete(uri.toString());
    this.syncContextKey();
  }

  getByUri(uri: vscode.Uri): NotebookSession | undefined {
    return this.uriToSession.get(uri.toString());
  }

  entries(): [string, NotebookSession][] {
    return [...this.uriToSession.entries()];
  }

  get size(): number {
    return this.uriToSession.size;
  }

  /**
   * Mirrors "is a notebook open" into the verso.notebookOpen context key.
   * The language model tools declare it as their `when` condition so they
   * stay out of chat sessions that have no notebook to act on.
   */
  private syncContextKey(): void {
    const open = this.uriToSession.size > 0;
    if (open === this.notebookOpen) {
      return;
    }
    this.notebookOpen = open;
    void vscode.commands.executeCommand("setContext", "verso.notebookOpen", open);
  }
}

export const hostRegistry = new HostRegistry();
