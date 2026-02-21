import * as vscode from "vscode";
import * as path from "path";
import { HostProcess } from "./host/hostProcess";
import { notebookRegistry, getActiveNotebookId } from "./host/notebookRegistry";
import { VersoSerializer } from "./notebook/versoSerializer";
import { VersoController } from "./notebook/versoController";
import { VersoCompletionProvider } from "./providers/completionProvider";
import { VersoDiagnosticsProvider } from "./providers/diagnosticsProvider";
import { VersoHoverProvider } from "./providers/hoverProvider";
import {
  ExtensionTreeProvider,
  ExtensionTreeItem,
} from "./providers/extensionTreeProvider";
import {
  VariableTreeProvider,
  VariableTreeItem,
} from "./providers/variableTreeProvider";
import { registerToolbarActions } from "./toolbar/toolbarActions";
import { DashboardPanel } from "./layout/dashboardPanel";
import { BlazorEditorProvider } from "./blazor/blazorEditorProvider";
import { applyEngineTheme } from "./theme/themeMapper";
import {
  CellAddParams,
  CellDto,
  ExtensionListResult,
  ExtensionToggleParams,
  NotebookCloseParams,
  NotebookSetFilePathParams,
  SettingsGetDefinitionsResult,
  VariableInspectParams,
  VariableInspectResult,
} from "./host/protocol";

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

  // Send the notebook file path to the host after deserialization completes.
  // The NotebookSerializer API does not provide the URI during deserializeNotebook,
  // so we send it as a follow-up once the document is available.
  context.subscriptions.push(
    vscode.workspace.onDidOpenNotebookDocument(async (doc) => {
      if (doc.notebookType !== "verso-notebook") {
        return;
      }
      const notebookId = doc.metadata?.notebookId as string | undefined;
      if (notebookId) {
        notebookRegistry.register(doc.uri, notebookId);
      }
      if (doc.uri.scheme === "file" && notebookId) {
        try {
          await host!.sendRequest("notebook/setFilePath", {
            filePath: doc.uri.fsPath,
            notebookId,
          } satisfies NotebookSetFilePathParams & { notebookId: string });
        } catch {
          // Host may not be running yet — ignore
        }
      }
    })
  );

  // Clean up when a notebook document is closed
  context.subscriptions.push(
    vscode.workspace.onDidCloseNotebookDocument(async (doc) => {
      if (doc.notebookType !== "verso-notebook") {
        return;
      }
      const notebookId = notebookRegistry.unregister(doc.uri);
      if (notebookId) {
        try {
          await host!.sendRequest("notebook/close", {
            notebookId,
          } satisfies NotebookCloseParams);
        } catch {
          // Host may already be gone — ignore
        }
      }
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

  // Register new cells added via the VS Code UI (e.g. the "+" button) with
  // the host engine so they receive a versoId and can be executed.
  context.subscriptions.push(
    vscode.workspace.onDidChangeNotebookDocument(async (e) => {
      if (e.notebook.notebookType !== "verso-notebook") {
        return;
      }
      const notebookId = notebookRegistry.getByUri(e.notebook.uri);
      for (const change of e.contentChanges) {
        for (const cell of change.addedCells) {
          if (cell.metadata?.versoId) {
            continue;
          }
          const type =
            cell.kind === vscode.NotebookCellKind.Code ? "code" : "markdown";
          const language =
            cell.kind === vscode.NotebookCellKind.Code
              ? cell.document.languageId
              : undefined;
          try {
            const result = await host!.sendRequest<CellDto>("cell/add", {
              type,
              language,
              source: cell.document.getText(),
              notebookId,
            } as CellAddParams & { notebookId?: string });

            const edit = new vscode.WorkspaceEdit();
            const nbEdit = vscode.NotebookEdit.updateCellMetadata(
              cell.index,
              { ...cell.metadata, versoId: result.id }
            );
            edit.set(e.notebook.uri, [nbEdit]);
            await vscode.workspace.applyEdit(edit);
          } catch {
            // Host may not be ready — ignore
          }
        }
      }
    })
  );

  // Register Blazor WASM custom editor (available via "Open With...")
  const blazorProvider = new BlazorEditorProvider(context, host);
  context.subscriptions.push(
    vscode.window.registerCustomEditorProvider(
      BlazorEditorProvider.viewType,
      blazorProvider,
      { webviewOptions: { retainContextWhenHidden: true } }
    )
  );

  // Register notebook controller
  const controller = new VersoController(host);
  context.subscriptions.push(controller);

  // Populate the controller's supported languages from the host once the
  // first notebook is opened (the host session must exist before
  // notebook/getLanguages can be called).  The first language in the list
  // becomes the default for new code cells created via the VS Code UI.
  let languagesInitialized = false;
  context.subscriptions.push(
    vscode.workspace.onDidOpenNotebookDocument(async (doc) => {
      if (languagesInitialized || doc.notebookType !== "verso-notebook") {
        return;
      }
      languagesInitialized = true;
      try {
        const notebookId = notebookRegistry.getByUri(doc.uri);
        await controller.updateSupportedLanguages(notebookId);
      } catch {
        // Best effort — controller still works without the language list
      }
    })
  );

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

  // Register dashboard panel for custom layout rendering
  const dashboardPanel = new DashboardPanel(host);
  context.subscriptions.push(dashboardPanel);

  // Register toolbar commands
  registerToolbarActions(context, host, controller, dashboardPanel);

  // Apply engine theme (best-effort)
  applyEngineTheme(host);

  // --- Extension and Variable tree views ---

  const extensionTreeProvider = new ExtensionTreeProvider(host);
  const variableTreeProvider = new VariableTreeProvider(host);

  context.subscriptions.push(
    vscode.window.registerTreeDataProvider(
      "verso.extensions",
      extensionTreeProvider
    )
  );
  context.subscriptions.push(
    vscode.window.registerTreeDataProvider(
      "verso.variables",
      variableTreeProvider
    )
  );

  // Set context for tree view visibility
  vscode.commands.executeCommand("setContext", "verso.notebookOpen", true);

  // Register extension management commands
  context.subscriptions.push(
    vscode.commands.registerCommand("verso.refreshExtensions", () => {
      extensionTreeProvider.refresh();
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand("verso.refreshVariables", () => {
      variableTreeProvider.refresh();
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand(
      "verso.enableExtension",
      async (item: ExtensionTreeItem) => {
        const notebookId = getActiveNotebookId();
        try {
          await host!.sendRequest<ExtensionListResult>("extension/enable", {
            extensionId: item.info.extensionId,
            notebookId,
          } as ExtensionToggleParams & { notebookId?: string });
          extensionTreeProvider.refresh();
        } catch (err) {
          vscode.window.showErrorMessage(
            `Failed to enable extension: ${err instanceof Error ? err.message : err}`
          );
        }
      }
    )
  );

  context.subscriptions.push(
    vscode.commands.registerCommand(
      "verso.disableExtension",
      async (item: ExtensionTreeItem) => {
        const notebookId = getActiveNotebookId();
        try {
          await host!.sendRequest<ExtensionListResult>("extension/disable", {
            extensionId: item.info.extensionId,
            notebookId,
          } as ExtensionToggleParams & { notebookId?: string });
          extensionTreeProvider.refresh();
        } catch (err) {
          vscode.window.showErrorMessage(
            `Failed to disable extension: ${err instanceof Error ? err.message : err}`
          );
        }
      }
    )
  );

  context.subscriptions.push(
    vscode.commands.registerCommand(
      "verso.inspectVariable",
      async (item: VariableTreeItem) => {
        const notebookId = getActiveNotebookId();
        try {
          const result = await host!.sendRequest<VariableInspectResult>(
            "variable/inspect",
            {
              name: item.entry.name,
              notebookId,
            } as VariableInspectParams & { notebookId?: string }
          );

          const doc = await vscode.workspace.openTextDocument({
            content: result.content,
            language:
              result.mimeType === "text/html" ? "html" : "plaintext",
          });
          await vscode.window.showTextDocument(doc, { preview: true });
        } catch (err) {
          vscode.window.showErrorMessage(
            `Failed to inspect variable: ${err instanceof Error ? err.message : err}`
          );
        }
      }
    )
  );

  // Register settings command
  context.subscriptions.push(
    vscode.commands.registerCommand("verso.openSettings", async () => {
      const notebookId = getActiveNotebookId();
      try {
        const result = await host!.sendRequest<SettingsGetDefinitionsResult>(
          "settings/getDefinitions",
          { notebookId }
        );

        if (result.extensions.length === 0) {
          vscode.window.showInformationMessage(
            "No extensions with configurable settings are loaded."
          );
          return;
        }

        // Show a quick summary of available settings
        const items = result.extensions.map((ext) => ({
          label: ext.extensionName,
          description: `${ext.definitions.length} setting(s)`,
          detail: ext.extensionId,
        }));

        await vscode.window.showQuickPick(items, {
          placeHolder: "Extension settings (read-only preview)",
        });
      } catch (err) {
        vscode.window.showErrorMessage(
          `Failed to load settings: ${err instanceof Error ? err.message : err}`
        );
      }
    })
  );

  // Auto-refresh variables on cell execution completion
  host.onNotification("cell/executionState", () => {
    variableTreeProvider.refresh();
  });

  // Initial refresh
  extensionTreeProvider.refresh();
  variableTreeProvider.refresh();
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
