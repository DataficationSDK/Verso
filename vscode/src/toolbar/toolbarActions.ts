import * as vscode from "vscode";
import { HostProcess } from "../host/hostProcess";
import { VersoController } from "../notebook/versoController";
import { DashboardPanel } from "../layout/dashboardPanel";
import { LayoutsResult, LayoutDto, ThemesResult, ThemeListItemDto, ThemeResult } from "../host/protocol";
import { applyEngineTheme } from "../theme/themeMapper";

export function registerToolbarActions(
  context: vscode.ExtensionContext,
  host: HostProcess,
  controller: VersoController,
  dashboardPanel: DashboardPanel
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

  context.subscriptions.push(
    vscode.commands.registerCommand("verso.switchLayout", async () => {
      const notebook = vscode.window.activeNotebookEditor?.notebook;
      if (notebook?.notebookType !== "verso-notebook") {
        return;
      }

      // Request available layouts from host
      const result = (await host.sendRequest(
        "layout/getLayouts"
      )) as LayoutsResult;
      if (!result?.layouts || result.layouts.length <= 1) {
        vscode.window.showInformationMessage("No other layouts available.");
        return;
      }

      // Show QuickPick with available layouts
      const items = result.layouts.map((l: LayoutDto) => ({
        label: l.displayName + (l.isActive ? " (active)" : ""),
        description: l.requiresCustomRenderer ? "Custom renderer" : "Native",
        layoutId: l.id,
        requiresCustomRenderer: l.requiresCustomRenderer,
      }));

      const selected = await vscode.window.showQuickPick(items, {
        placeHolder: "Select a layout",
      });

      if (!selected) return;

      // Switch layout on host
      await host.sendRequest("layout/switch", {
        layoutId: selected.layoutId,
      });

      // If custom renderer needed, open dashboard panel
      if (selected.requiresCustomRenderer) {
        await dashboardPanel.show();
      } else {
        dashboardPanel.close();
      }
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand("verso.switchTheme", async () => {
      const notebook = vscode.window.activeNotebookEditor?.notebook;
      if (notebook?.notebookType !== "verso-notebook") {
        return;
      }

      // Request available themes from host
      const result = (await host.sendRequest(
        "theme/getThemes"
      )) as ThemesResult;
      if (!result?.themes || result.themes.length <= 1) {
        vscode.window.showInformationMessage("No other themes available.");
        return;
      }

      // Show QuickPick with available themes
      const items = result.themes.map((t: ThemeListItemDto) => ({
        label: t.displayName + (t.isActive ? " (active)" : ""),
        description: t.themeKind,
        themeId: t.id,
      }));

      const selected = await vscode.window.showQuickPick(items, {
        placeHolder: "Select a theme",
      });

      if (!selected) return;

      // Switch theme on host
      await host.sendRequest("theme/switch", {
        themeId: selected.themeId,
      });

      // Re-apply theme colors
      await applyEngineTheme(host);
    })
  );
}
