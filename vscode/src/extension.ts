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

  host = new HostProcess(hostDllPath);
  context.subscriptions.push(host);

  try {
    await host.start();
  } catch (err) {
    vscode.window.showErrorMessage(
      `Failed to start Verso host: ${err instanceof Error ? err.message : err}`
    );
    return;
  }

  // Register notebook serializer
  const serializer = new VersoSerializer(host);
  context.subscriptions.push(
    vscode.workspace.registerNotebookSerializer("verso-notebook", serializer, {
      transientOutputs: false,
    })
  );

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
  // Check user configuration first
  const configured = vscode.workspace
    .getConfiguration("verso")
    .get<string>("hostPath");
  if (configured) {
    return configured;
  }

  // Default: look for Verso.Host.dll relative to the extension
  return path.join(context.extensionPath, "..", "src", "Verso.Host", "bin", "Debug", "net8.0", "Verso.Host.dll");
}
