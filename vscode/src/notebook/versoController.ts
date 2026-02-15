import * as vscode from "vscode";
import { HostProcess } from "../host/hostProcess";
import {
  CellOutputDto,
  CellUpdateSourceParams,
  ExecutionResultDto,
  ExecutionRunParams,
  ExecutionRunAllResult,
  NotebookOpenParams,
  NotebookOpenResult,
} from "../host/protocol";

export class VersoController {
  private readonly controller: vscode.NotebookController;
  private notebookOpenedUri: string | undefined;

  constructor(private readonly host: HostProcess) {
    this.controller = vscode.notebooks.createNotebookController(
      "verso-kernel",
      "verso-notebook",
      "Verso"
    );

    this.controller.description = ".NET Notebook Engine";
    // Don't restrict languages — the engine handles routing cells to the
    // correct kernel or cell type.  Setting this to undefined lets VS Code
    // pass all code cells to the controller regardless of language.
    this.controller.supportedLanguages = undefined;
    this.controller.supportsExecutionOrder = true;
    this.controller.executeHandler = this.executeHandler.bind(this);
  }

  /** Ensure the notebook is open on the host. Re-opens from disk if needed. */
  async ensureNotebookOpen(notebook: vscode.NotebookDocument): Promise<void> {
    if (this.notebookOpenedUri === notebook.uri.toString()) {
      return;
    }
    const fileContent = new TextDecoder().decode(
      await vscode.workspace.fs.readFile(notebook.uri)
    );
    await this.host.sendRequest<NotebookOpenResult>("notebook/open", {
      content: fileContent,
    } satisfies NotebookOpenParams);
    this.notebookOpenedUri = notebook.uri.toString();
  }

  /** Mark the notebook as needing re-open (e.g. after kernel restart). */
  resetNotebookState(): void {
    this.notebookOpenedUri = undefined;
  }

  private async executeHandler(
    cells: vscode.NotebookCell[],
    notebook: vscode.NotebookDocument,
    controller: vscode.NotebookController
  ): Promise<void> {
    await this.ensureNotebookOpen(notebook);
    for (const cell of cells) {
      await this.executeCell(cell, controller);
    }
  }

  private async executeCell(
    cell: vscode.NotebookCell,
    controller: vscode.NotebookController
  ): Promise<void> {
    const versoId = cell.metadata?.versoId as string | undefined;
    if (!versoId) {
      return;
    }

    const execution = controller.createNotebookCellExecution(cell);
    execution.executionOrder = undefined;
    execution.start(Date.now());

    try {
      // Sync source before execution
      await this.host.sendRequest("cell/updateSource", {
        cellId: versoId,
        source: cell.document.getText(),
      } satisfies CellUpdateSourceParams);

      // Execute
      const result = await this.host.sendRequest<ExecutionResultDto>(
        "execution/run",
        { cellId: versoId } satisfies ExecutionRunParams
      );

      execution.executionOrder = result.executionCount;

      // Map outputs
      const outputs = result.outputs.map((o) => this.mapOutput(o));
      execution.replaceOutput(outputs);

      execution.end(result.status === "completed", Date.now());
    } catch (err) {
      execution.replaceOutput([
        new vscode.NotebookCellOutput([
          vscode.NotebookCellOutputItem.error(
            err instanceof Error ? err : new Error(String(err))
          ),
        ]),
      ]);
      execution.end(false, Date.now());
    }
  }

  async runAll(notebook: vscode.NotebookDocument): Promise<void> {
    await this.ensureNotebookOpen(notebook);

    // Sync all cell sources first
    for (const cell of notebook.getCells()) {
      const versoId = cell.metadata?.versoId as string | undefined;
      if (versoId) {
        await this.host.sendRequest("cell/updateSource", {
          cellId: versoId,
          source: cell.document.getText(),
        } satisfies CellUpdateSourceParams);
      }
    }

    const result = await this.host.sendRequest<ExecutionRunAllResult>(
      "execution/runAll"
    );

    // Match results to cells and update outputs
    for (const execResult of result.results) {
      const cell = notebook
        .getCells()
        .find((c) => c.metadata?.versoId === execResult.cellId);
      if (!cell) {
        continue;
      }

      const execution = this.controller.createNotebookCellExecution(cell);
      execution.executionOrder = execResult.executionCount;
      execution.start(Date.now());
      const outputs = execResult.outputs.map((o) => this.mapOutput(o));
      execution.replaceOutput(outputs);
      execution.end(execResult.status === "completed", Date.now());
    }
  }

  private mapOutput(output: CellOutputDto): vscode.NotebookCellOutput {
    if (output.isError) {
      const err = new Error(output.content || output.errorName || "Error");
      if (output.errorStackTrace) {
        err.stack = output.errorStackTrace;
      }
      return new vscode.NotebookCellOutput([
        vscode.NotebookCellOutputItem.error(err),
      ]);
    }

    const mimeType = output.mimeType || "text/plain";
    return new vscode.NotebookCellOutput([
      vscode.NotebookCellOutputItem.text(output.content, mimeType),
    ]);
  }

  dispose(): void {
    this.controller.dispose();
  }
}
