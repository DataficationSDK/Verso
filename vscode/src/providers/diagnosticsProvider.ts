import * as vscode from "vscode";
import { HostProcess } from "../host/hostProcess";
import { DiagnosticsParams, DiagnosticsResult } from "../host/protocol";

const severityMap: Record<string, vscode.DiagnosticSeverity> = {
  Error: vscode.DiagnosticSeverity.Error,
  Warning: vscode.DiagnosticSeverity.Warning,
  Info: vscode.DiagnosticSeverity.Information,
  Hidden: vscode.DiagnosticSeverity.Hint,
};

export class VersoDiagnosticsProvider implements vscode.Disposable {
  private readonly collection: vscode.DiagnosticCollection;
  private readonly disposables: vscode.Disposable[] = [];
  private debounceTimer: ReturnType<typeof setTimeout> | undefined;

  constructor(private readonly host: HostProcess) {
    this.collection = vscode.languages.createDiagnosticCollection("verso");
    this.disposables.push(this.collection);

    this.disposables.push(
      vscode.workspace.onDidChangeTextDocument((e) => {
        if (e.document.uri.scheme === "vscode-notebook-cell") {
          this.debouncedUpdate(e.document);
        }
      })
    );
  }

  private debouncedUpdate(document: vscode.TextDocument): void {
    if (this.debounceTimer) {
      clearTimeout(this.debounceTimer);
    }
    this.debounceTimer = setTimeout(() => {
      this.updateDiagnostics(document);
    }, 500);
  }

  private async updateDiagnostics(document: vscode.TextDocument): Promise<void> {
    const cell = vscode.window.activeNotebookEditor?.notebook
      .getCells()
      .find((c) => c.document.uri.toString() === document.uri.toString());

    const versoId = cell?.metadata?.versoId as string | undefined;
    if (!versoId) {
      return;
    }

    try {
      const result = await this.host.sendRequest<DiagnosticsResult>(
        "kernel/getDiagnostics",
        {
          cellId: versoId,
          code: document.getText(),
        } satisfies DiagnosticsParams
      );

      const diagnostics = result.items.map((item) => {
        const range = new vscode.Range(
          item.startLine,
          item.startColumn,
          item.endLine,
          item.endColumn
        );
        const diag = new vscode.Diagnostic(
          range,
          item.message,
          severityMap[item.severity] ?? vscode.DiagnosticSeverity.Error
        );
        if (item.code) {
          diag.code = item.code;
        }
        diag.source = "verso";
        return diag;
      });

      this.collection.set(document.uri, diagnostics);
    } catch {
      // Silently ignore diagnostic errors
    }
  }

  dispose(): void {
    if (this.debounceTimer) {
      clearTimeout(this.debounceTimer);
    }
    for (const d of this.disposables) {
      d.dispose();
    }
  }
}
