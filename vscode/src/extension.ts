import * as vscode from "vscode";
import * as path from "path";
import { HostProcess } from "./host/hostProcess";
import { BlazorEditorProvider } from "./blazor/blazorEditorProvider";

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

  // Register Blazor WASM custom editor
  const blazorProvider = new BlazorEditorProvider(context, host);
  context.subscriptions.push(
    vscode.window.registerCustomEditorProvider(
      BlazorEditorProvider.viewType,
      blazorProvider,
      { webviewOptions: { retainContextWhenHidden: true } }
    )
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

  // Check bundled host (inside the installed extension)
  const bundled = path.join(context.extensionPath, "host", "Verso.Host.dll");
  if (fs.existsSync(bundled)) {
    return bundled;
  }

  // Search workspace folders for the Verso.Host.dll (check Release first, then Debug)
  const configs = ["Release", "Debug"];
  const workspaceFolders = vscode.workspace.workspaceFolders ?? [];
  for (const folder of workspaceFolders) {
    for (const cfg of configs) {
      const candidates = [
        // Direct workspace is the Verso project
        path.join(folder.uri.fsPath, "src", "Verso.Host", "bin", cfg, "net8.0", "Verso.Host.dll"),
        // Workspace is a parent (e.g., Datafication.DataIntegration)
        path.join(folder.uri.fsPath, "tools", "Verso", "src", "Verso.Host", "bin", cfg, "net8.0", "Verso.Host.dll"),
      ];
      for (const candidate of candidates) {
        if (fs.existsSync(candidate)) {
          return candidate;
        }
      }
    }
  }

  // Fallback: relative to extension path (works in dev host / local install)
  for (const cfg of configs) {
    const extensionRelative = path.join(context.extensionPath, "..", "src", "Verso.Host", "bin", cfg, "net8.0", "Verso.Host.dll");
    if (fs.existsSync(extensionRelative)) {
      return extensionRelative;
    }
  }

  // Nothing found — return the configured value (or empty) so the error is clear
  return configured || "";
}
