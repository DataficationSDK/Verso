import * as vscode from "vscode";
import { HostProcess } from "../host/hostProcess";
import { VersoController } from "../notebook/versoController";

export function registerToolbarActions(
  context: vscode.ExtensionContext,
  host: HostProcess,
  controller: VersoController
): void {
  context.subscriptions.push(
    vscode.commands.registerCommand("verso.runAll", async () => {
      const notebook = vscode.window.activeNotebookEditor?.notebook;
      if (notebook?.notebookType === "verso-notebook") {
        await controller.runAll(notebook);
      }
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand("verso.runCell", async () => {
      const editor = vscode.window.activeNotebookEditor;
      if (!editor || editor.notebook.notebookType !== "verso-notebook") {
        return;
      }
      // The built-in notebook run cell will invoke the controller's executeHandler
      await vscode.commands.executeCommand("notebook.cell.execute");
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand("verso.clearOutputs", async () => {
      const notebook = vscode.window.activeNotebookEditor?.notebook;
      if (notebook?.notebookType !== "verso-notebook") {
        return;
      }
      await host.sendRequest("output/clearAll");
      // Clear VS Code side outputs
      await vscode.commands.executeCommand("notebook.clearAllCellsOutputs");
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand("verso.restartKernel", async () => {
      const notebook = vscode.window.activeNotebookEditor?.notebook;
      if (notebook?.notebookType !== "verso-notebook") {
        return;
      }
      await host.sendRequest("kernel/restart");
      vscode.window.showInformationMessage("Verso kernel restarted.");
    })
  );
}
