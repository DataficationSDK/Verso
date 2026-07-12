import * as vscode from "vscode";
import * as path from "path";
import { BlazorEditorProvider } from "./blazor/blazorEditorProvider";
import { registerParticipant } from "./copilot/participant";
import { registerTools, resolveNotebook } from "./copilot/tools";
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

  await migrateExtensionsPathSetting();

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

  // Command-palette entry to start a blank notebook without first creating a
  // file. Opens a scratch .verso the provider cleans up if it's never kept.
  context.subscriptions.push(
    vscode.commands.registerCommand("verso.newNotebook", () =>
      blazorProvider.createScratchNotebook()
    )
  );

  // Opens the notebook diff view: the source is picked natively here, then the webview
  // is told which comparison to run (it fetches the baseline via the bridge and renders
  // the diff). resolveNotebook shows a quick pick when more than one notebook is open.
  context.subscriptions.push(
    vscode.commands.registerCommand("verso.compareWithBaseline", async () => {
      const notebook = await resolveNotebook();
      if (!notebook) {
        vscode.window.showInformationMessage(
          "Verso: Open a notebook to compare it with a baseline."
        );
        return;
      }

      const { sources } = notebook.bridge.listDiffSources();
      const picked = await vscode.window.showQuickPick(
        sources.map((s) => ({
          label: s.label,
          description: s.available ? "" : s.description ?? "unavailable",
          sourceId: s.id,
          available: s.available,
        })),
        { placeHolder: "Compare notebook with..." }
      );
      if (!picked) {
        return;
      }
      if (!picked.available) {
        vscode.window.showInformationMessage(
          `Verso: ${picked.description || "This comparison source is not available."}`
        );
        return;
      }
      notebook.bridge.notify("diff/requested", { sourceId: picked.sourceId });
    })
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

// verso.extensionsPath used to accept a single string. The setting is now a
// string array so the Settings UI can render an editable list. Rewrite any
// existing string value to a single-element array at the scope where it lives
// so legacy users keep their path and the Settings editor shows no type warning.
async function migrateExtensionsPathSetting(): Promise<void> {
  const config = vscode.workspace.getConfiguration("verso");
  const inspected = config.inspect<string | string[]>("extensionsPath");
  if (!inspected) {
    return;
  }

  const scopes: Array<[unknown, vscode.ConfigurationTarget]> = [
    [inspected.globalValue, vscode.ConfigurationTarget.Global],
    [inspected.workspaceValue, vscode.ConfigurationTarget.Workspace],
  ];

  for (const [value, target] of scopes) {
    // Only act on a legacy string; arrays and unset values are left alone.
    if (typeof value !== "string") {
      continue;
    }
    const trimmed = value.trim();
    // A non-empty path becomes a one-element array; an explicit empty string is
    // cleared so the [] default applies.
    const migrated = trimmed.length > 0 ? [trimmed] : undefined;
    try {
      await config.update("extensionsPath", migrated, target);
      log.info(
        `Migrated verso.extensionsPath (${vscode.ConfigurationTarget[target]}) from string to array.`
      );
    } catch (err) {
      log.error(`Failed to migrate verso.extensionsPath: ${err}`);
    }
  }
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
        // Workspace is a parent
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
