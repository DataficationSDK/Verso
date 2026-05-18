import * as vscode from "vscode";
import * as path from "path";
import { BlazorEditorProvider } from "./blazor/blazorEditorProvider";
import { registerParticipant } from "./copilot/participant";
import { registerTools } from "./copilot/tools";
import { initialize as initializeLog, log } from "./log";

type HostPathResolutionOptions = {
  existsSync?: (path: string) => boolean;
  configuredHostPath?: string;
  workspaceFolders?: readonly vscode.WorkspaceFolder[];
};

export async function activate(
  context: vscode.ExtensionContext
): Promise<void> {
  initializeLog(context);
  log.info(`Verso extension activated (v${context.extension.packageJSON.version})`);

  const hostDllPath = resolveHostPath(context);
  if (hostDllPath) {
    log.info(`Resolved Verso.Host.dll: ${hostDllPath}`);
  } else {
    log.error('Could not find Verso.Host.dll. Set "verso.hostPath" in settings to the path of your built Verso.Host.dll.');
    vscode.window.showErrorMessage(
      'Verso: Could not find Verso.Host.dll. Set "verso.hostPath" in settings to the path of your built Verso.Host.dll.'
    );
  }

  // Register Blazor WASM custom editor — each notebook spawns its own host process
  const blazorProvider = new BlazorEditorProvider(context, hostDllPath);
  context.subscriptions.push(
    vscode.window.registerCustomEditorProvider(
      BlazorEditorProvider.viewType,
      blazorProvider,
      { webviewOptions: { retainContextWhenHidden: true } }
    )
  );

  // Register Copilot chat participant and tools (requires vscode.chat and vscode.lm APIs,
  // which are not available in VSCodium or other VS Code forks that strip Copilot)
  if (typeof vscode.chat?.createChatParticipant === "function" &&
      typeof vscode.lm?.registerTool === "function") {
    registerTools(context);
    registerParticipant(context);
  }
}

export function deactivate(): void {
  // Host processes are disposed per-notebook when their webview panels close.
}

export function resolveHostPath(
  context: vscode.ExtensionContext,
  options: HostPathResolutionOptions = {}
): string {
  const fs = require("fs");
  const existsSync = options.existsSync ?? fs.existsSync;

  const bundled = path.join(context.extensionPath, "host", "Verso.Host.dll");

  // In F5/development mode the extension should always use the freshly built
  // bundled host from the workspace, even if a user-level verso.hostPath points
  // at an older installed host.
  if (context.extensionMode === vscode.ExtensionMode.Development && existsSync(bundled)) {
    return bundled;
  }

  // Check user configuration first
  const configured = options.configuredHostPath ??
    vscode.workspace.getConfiguration("verso").get<string>("hostPath");
  if (configured && existsSync(configured)) {
    return configured;
  }

  // Check bundled host (inside the installed extension)
  if (existsSync(bundled)) {
    return bundled;
  }

  // Search workspace folders for the Verso.Host.dll (check Release first, then Debug)
  const configs = ["Release", "Debug"];
  const workspaceFolders = options.workspaceFolders ?? vscode.workspace.workspaceFolders ?? [];
  for (const folder of workspaceFolders) {
    for (const cfg of configs) {
      const candidates = [
        // Direct workspace is the Verso project
        path.join(folder.uri.fsPath, "src", "Verso.Host", "bin", cfg, "net8.0", "Verso.Host.dll"),
        // Workspace is a parent (e.g., Datafication.DataIntegration)
        path.join(folder.uri.fsPath, "tools", "Verso", "src", "Verso.Host", "bin", cfg, "net8.0", "Verso.Host.dll"),
      ];
      for (const candidate of candidates) {
        if (existsSync(candidate)) {
          return candidate;
        }
      }
    }
  }

  // Fallback: relative to extension path (works in dev host / local install)
  for (const cfg of configs) {
    const extensionRelative = path.join(context.extensionPath, "..", "src", "Verso.Host", "bin", cfg, "net8.0", "Verso.Host.dll");
    if (existsSync(extensionRelative)) {
      return extensionRelative;
    }
  }

  // Nothing found — return the configured value (or empty) so the error is clear
  return configured || "";
}
