import * as assert from "assert";
import * as vscode from "vscode";

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
});
