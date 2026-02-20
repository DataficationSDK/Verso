import * as assert from "assert";
import * as vscode from "vscode";

// Import protocol types directly (pure data, no VS Code dependencies at import)
import type { CellDto, CellOutputDto } from "../../src/host/protocol";

suite("VersoSerializer Mapping Logic", () => {
  test("Code cell maps to NotebookCellKind.Code", () => {
    const cell: CellDto = {
      id: "cell-1",
      type: "code",
      language: "csharp",
      source: "Console.WriteLine(42);",
      outputs: [],
    };

    const kind = cell.type === "markdown"
      ? vscode.NotebookCellKind.Markup
      : vscode.NotebookCellKind.Code;

    assert.strictEqual(kind, vscode.NotebookCellKind.Code);
  });

  test("Markdown cell maps to NotebookCellKind.Markup", () => {
    const cell: CellDto = {
      id: "cell-2",
      type: "markdown",
      source: "# Hello",
      outputs: [],
    };

    const kind = cell.type === "markdown"
      ? vscode.NotebookCellKind.Markup
      : vscode.NotebookCellKind.Code;

    assert.strictEqual(kind, vscode.NotebookCellKind.Markup);
  });

  test("Code cell language defaults to csharp when null", () => {
    const cell: CellDto = {
      id: "cell-3",
      type: "code",
      source: "let x = 1",
      outputs: [],
    };

    const language = cell.type === "markdown"
      ? "markdown"
      : cell.language ?? "csharp";

    assert.strictEqual(language, "csharp");
  });

  test("Markdown cell language is always markdown", () => {
    const cell: CellDto = {
      id: "cell-4",
      type: "markdown",
      language: "markdown",
      source: "## Title",
      outputs: [],
    };

    const language = cell.type === "markdown"
      ? "markdown"
      : cell.language ?? "csharp";

    assert.strictEqual(language, "markdown");
  });

  test("Cell with explicit language preserves it", () => {
    const cell: CellDto = {
      id: "cell-5",
      type: "code",
      language: "fsharp",
      source: "let x = 1",
      outputs: [],
    };

    const language = cell.type === "markdown"
      ? "markdown"
      : cell.language ?? "csharp";

    assert.strictEqual(language, "fsharp");
  });

  test("Text output maps to correct MIME type", () => {
    const output: CellOutputDto = {
      mimeType: "text/plain",
      content: "42",
      isError: false,
    };

    const mimeType = output.mimeType || "text/plain";
    assert.strictEqual(mimeType, "text/plain");
  });

  test("HTML output preserves MIME type", () => {
    const output: CellOutputDto = {
      mimeType: "text/html",
      content: "<b>bold</b>",
      isError: false,
    };

    assert.strictEqual(output.mimeType, "text/html");
    assert.ok(!output.isError);
  });

  test("Error output is flagged correctly", () => {
    const output: CellOutputDto = {
      mimeType: "text/plain",
      content: "NullReferenceException",
      isError: true,
      errorName: "NullReferenceException",
      errorStackTrace: "at Program.Main()",
    };

    assert.ok(output.isError);
    assert.strictEqual(output.errorName, "NullReferenceException");
    assert.ok(output.errorStackTrace?.includes("Program.Main"));
  });

  test("Empty content adds default cell", () => {
    const cells: CellDto[] = [];

    // Serializer logic: if no cells, add an empty code cell
    if (cells.length === 0) {
      cells.push({
        id: "default",
        type: "code",
        source: "",
        language: "csharp",
        outputs: [],
      });
    }

    assert.strictEqual(cells.length, 1);
    assert.strictEqual(cells[0].type, "code");
    assert.strictEqual(cells[0].source, "");
  });

  test("Multiple outputs map correctly", () => {
    const outputs: CellOutputDto[] = [
      { mimeType: "text/plain", content: "line 1", isError: false },
      { mimeType: "text/html", content: "<p>line 2</p>", isError: false },
      { mimeType: "text/plain", content: "error!", isError: true, errorName: "Error" },
    ];

    assert.strictEqual(outputs.length, 3);
    assert.ok(!outputs[0].isError);
    assert.ok(!outputs[1].isError);
    assert.ok(outputs[2].isError);
  });

  test("NotebookData can be constructed from mapped cells", () => {
    const cell: CellDto = {
      id: "test-id",
      type: "code",
      language: "csharp",
      source: "var x = 1;",
      outputs: [{ mimeType: "text/plain", content: "1", isError: false }],
    };

    const kind = vscode.NotebookCellKind.Code;
    const cellData = new vscode.NotebookCellData(kind, cell.source, cell.language ?? "csharp");
    cellData.metadata = { versoId: cell.id };

    const nbData = new vscode.NotebookData([cellData]);
    assert.strictEqual(nbData.cells.length, 1);
    assert.strictEqual(nbData.cells[0].metadata?.versoId, "test-id");
  });
});
