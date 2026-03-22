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

  register(uri: vscode.Uri, session: NotebookSession): void {
    this.uriToSession.set(uri.toString(), session);
  }

  unregister(uri: vscode.Uri): void {
    this.uriToSession.delete(uri.toString());
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
}

export const hostRegistry = new HostRegistry();
