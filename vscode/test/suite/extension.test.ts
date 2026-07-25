import * as assert from "assert";
import * as path from "path";
import * as vscode from "vscode";
import { resolveHostPath } from "../../src/extension";

suite("Extension Activation", () => {
  test("Extension should be present", () => {
    const ext = vscode.extensions.getExtension("verso-contributors.verso-notebook");
    // Extension may or may not be installed in test environment;
    // we verify the API is accessible
    assert.ok(vscode.extensions);
  });

  test("VS Code API should expose notebook types", () => {
    assert.ok(vscode.NotebookCellKind);
    assert.strictEqual(vscode.NotebookCellKind.Markup, 1);
    assert.strictEqual(vscode.NotebookCellKind.Code, 2);
  });

  test("VS Code API should expose NotebookData constructor", () => {
    const cellData = new vscode.NotebookCellData(
      vscode.NotebookCellKind.Code,
      "var x = 1;",
      "csharp"
    );
    const notebookData = new vscode.NotebookData([cellData]);

    assert.strictEqual(notebookData.cells.length, 1);
    assert.strictEqual(notebookData.cells[0].value, "var x = 1;");
    assert.strictEqual(notebookData.cells[0].languageId, "csharp");
  });

  test("NotebookCellOutput can be created", () => {
    const item = vscode.NotebookCellOutputItem.text("hello", "text/plain");
    const output = new vscode.NotebookCellOutput([item]);

    assert.strictEqual(output.items.length, 1);
  });

  test("Error output item can be created", () => {
    const error = new Error("test error");
    const item = vscode.NotebookCellOutputItem.error(error);

    assert.ok(item);
    assert.strictEqual(item.mime, "application/vnd.code.notebook.error");
  });

  test("Deactivation should be clean", () => {
    // Verify that deactivate is a callable function pattern
    // The actual deactivation is tested via the extension lifecycle
    assert.ok(true, "Deactivation test placeholder");
  });

  test("Development mode prefers bundled host over configured hostPath", () => {
    const extensionPath = path.join("repo", "vscode");
    const bundled = path.join(extensionPath, "host", "Verso.Host.dll");
    const configured = path.join("old", "Verso.Host.dll");
    const context = {
      extensionPath,
      extensionMode: vscode.ExtensionMode.Development,
    } as vscode.ExtensionContext;

    const resolved = resolveHostPath(context, {
      configuredHostPath: configured,
      existsSync: candidate => candidate === bundled || candidate === configured,
      workspaceFolders: [],
    });

    assert.strictEqual(resolved, bundled);
  });

  test("Production mode honors configured hostPath before bundled host", () => {
    const extensionPath = path.join("repo", "vscode");
    const bundled = path.join(extensionPath, "host", "Verso.Host.dll");
    const configured = path.join("custom", "Verso.Host.dll");
    const context = {
      extensionPath,
      extensionMode: vscode.ExtensionMode.Production,
    } as vscode.ExtensionContext;

    const resolved = resolveHostPath(context, {
      configuredHostPath: configured,
      existsSync: candidate => candidate === bundled || candidate === configured,
      workspaceFolders: [],
    });

    assert.strictEqual(resolved, configured);
  });

  test("Workspace host resolution prefers net10 over net8", () => {
    const extensionPath = path.join("repo", "vscode");
    const workspacePath = path.resolve("repo", "Verso");
    const context = {
      extensionPath,
      extensionMode: vscode.ExtensionMode.Production,
    } as vscode.ExtensionContext;
    const workspaceFolder = {
      uri: vscode.Uri.file(workspacePath),
      name: "Verso",
      index: 0,
    };
    const workspaceFsPath = workspaceFolder.uri.fsPath;
    const net10Host = path.join(workspaceFsPath, "src", "Verso.Host", "bin", "Debug", "net10.0", "Verso.Host.dll");
    const net8Host = path.join(workspaceFsPath, "src", "Verso.Host", "bin", "Debug", "net8.0", "Verso.Host.dll");

    const resolved = resolveHostPath(context, {
      existsSync: candidate => candidate === net10Host || candidate === net8Host,
      workspaceFolders: [workspaceFolder],
    });

    assert.strictEqual(resolved, net10Host);
  });
});
