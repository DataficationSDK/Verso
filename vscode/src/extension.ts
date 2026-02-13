import * as vscode from "vscode";
import * as path from "path";
import { HostProcess } from "./host/hostProcess";
import { VersoSerializer } from "./notebook/versoSerializer";
import { VersoController } from "./notebook/versoController";
import { VersoCompletionProvider } from "./providers/completionProvider";
import { VersoDiagnosticsProvider } from "./providers/diagnosticsProvider";
import { VersoHoverProvider } from "./providers/hoverProvider";
import { registerToolbarActions } from "./toolbar/toolbarActions";
import { applyEngineTheme } from "./theme/themeMapper";

let host: HostProcess | undefined;

export async function activate(
  context: vscode.ExtensionContext
): Promise<void> {
  const hostDllPath = resolveHostPath(context);

  if (!hostDllPath) {
    vscode.window.showErrorMessage(
      'Verso: Could not find Verso.Host.dll. Set "verso.hostPath" in settings to the path of your built Verso.Host.dll.'
    );
  }

  host = new HostProcess(hostDllPath);
  context.subscriptions.push(host);

  // Register notebook serializer BEFORE starting the host so VS Code
  // can always open .verso files even if the host fails to start
  const serializer = new VersoSerializer(host);
  context.subscriptions.push(
    vscode.workspace.registerNotebookSerializer("verso-notebook", serializer, {
      transientOutputs: false,
    })
  );

  if (!hostDllPath) {
    return;
  }

  try {
    await host.start();
  } catch (err) {
    vscode.window.showErrorMessage(
      `Failed to start Verso host: ${err instanceof Error ? err.message : err}. Searched for Verso.Host.dll in workspace folders. Set "verso.hostPath" in settings.`
    );
    return;
  }

  // Register notebook controller
  const controller = new VersoController(host);
  context.subscriptions.push(controller);

  // Register language intelligence providers for notebook cells
  const cellSelector: vscode.DocumentSelector = {
    scheme: "vscode-notebook-cell",
    language: "csharp",
  };

  context.subscriptions.push(
    vscode.languages.registerCompletionItemProvider(
      cellSelector,
      new VersoCompletionProvider(host),
      "."
    )
  );

  context.subscriptions.push(
    vscode.languages.registerHoverProvider(
      cellSelector,
      new VersoHoverProvider(host)
    )
  );

  // Register diagnostics provider
  const diagnosticsProvider = new VersoDiagnosticsProvider(host);
  context.subscriptions.push(diagnosticsProvider);

  // Register toolbar commands
  registerToolbarActions(context, host, controller);

  // Apply engine theme (best-effort)
  applyEngineTheme(host);
}

export function deactivate(): void {
  host?.dispose();
}

function resolveHostPath(context: vscode.ExtensionContext): string {
  const fs = require("fs");

  // Check user configuration first
  const configured = vscode.workspace
    .getConfiguration("verso")
    .get<string>("hostPath");
  if (configured && fs.existsSync(configured)) {
    return configured;
  }

  // Search workspace folders for the Verso.Host.dll
  const workspaceFolders = vscode.workspace.workspaceFolders ?? [];
  for (const folder of workspaceFolders) {
    const candidates = [
      // Direct workspace is the Verso project
      path.join(folder.uri.fsPath, "src", "Verso.Host", "bin", "Debug", "net8.0", "Verso.Host.dll"),
      // Workspace is a parent (e.g., Datafication.DataIntegration)
      path.join(folder.uri.fsPath, "tools", "Verso", "src", "Verso.Host", "bin", "Debug", "net8.0", "Verso.Host.dll"),
    ];
    for (const candidate of candidates) {
      if (fs.existsSync(candidate)) {
        return candidate;
      }
    }
  }

  // Fallback: relative to extension path (works in dev host / local install)
  const extensionRelative = path.join(context.extensionPath, "..", "src", "Verso.Host", "bin", "Debug", "net8.0", "Verso.Host.dll");
  if (fs.existsSync(extensionRelative)) {
    return extensionRelative;
  }

  // Nothing found — return the configured value (or empty) so the error is clear
  return configured || "";
}
