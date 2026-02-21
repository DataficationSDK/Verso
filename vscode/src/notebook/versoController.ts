import * as vscode from "vscode";
import { HostProcess } from "../host/hostProcess";
import { notebookRegistry } from "../host/notebookRegistry";
import {
  CellOutputDto,
  CellUpdateSourceParams,
  ExecutionResultDto,
  ExecutionRunParams,
  ExecutionRunAllResult,
  LanguagesResult,
} from "../host/protocol";

export class VersoController {
  private readonly controller: vscode.NotebookController;

  constructor(private readonly host: HostProcess) {
    this.controller = vscode.notebooks.createNotebookController(
      "verso-kernel",
      "verso-notebook",
      "Verso"
    );

    this.controller.description = ".NET Notebook Engine";
    this.controller.supportsExecutionOrder = true;
    this.controller.executeHandler = this.executeHandler.bind(this);
  }

  private async executeHandler(
    cells: vscode.NotebookCell[],
    notebook: vscode.NotebookDocument,
    controller: vscode.NotebookController
  ): Promise<void> {
    const notebookId = notebookRegistry.getByUri(notebook.uri);
    for (const cell of cells) {
      await this.executeCell(cell, controller, notebookId);
    }
  }

  private async executeCell(
    cell: vscode.NotebookCell,
    controller: vscode.NotebookController,
    notebookId?: string
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
        notebookId,
      } as CellUpdateSourceParams & { notebookId?: string });

      // Execute
      const result = await this.host.sendRequest<ExecutionResultDto>(
        "execution/run",
        { cellId: versoId, notebookId } as ExecutionRunParams & {
          notebookId?: string;
        }
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
    const notebookId = notebookRegistry.getByUri(notebook.uri);

    // Sync all cell sources first
    for (const cell of notebook.getCells()) {
      const versoId = cell.metadata?.versoId as string | undefined;
      if (versoId) {
        await this.host.sendRequest("cell/updateSource", {
          cellId: versoId,
          source: cell.document.getText(),
          notebookId,
        } as CellUpdateSourceParams & { notebookId?: string });
      }
    }

    const result = await this.host.sendRequest<ExecutionRunAllResult>(
      "execution/runAll",
      { notebookId }
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
      const errorName = output.errorName || "Error";
      const message = output.content || errorName;
      const err = new Error(message);
      err.name = errorName;
      // Format stack in JS convention ("Name: message\n  at ...") so VS Code
      // renders the error name and message above the stack frames.
      err.stack = output.errorStackTrace
        ? `${errorName}: ${message}\n${output.errorStackTrace}`
        : `${errorName}: ${message}`;
      return new vscode.NotebookCellOutput([
        vscode.NotebookCellOutputItem.error(err),
      ]);
    }

    const mimeType = output.mimeType || "text/plain";
    return new vscode.NotebookCellOutput([
      vscode.NotebookCellOutputItem.text(output.content, mimeType),
    ]);
  }

  /**
   * Query the host for registered languages and set them on the controller.
   * The first language becomes the default for new code cells.
   */
  async updateSupportedLanguages(notebookId?: string): Promise<void> {
    const result = await this.host.sendRequest<LanguagesResult>(
      "notebook/getLanguages",
      { notebookId }
    );
    if (result.languages.length > 0) {
      this.controller.supportedLanguages = result.languages.map((l) => l.id);
    }
  }

  dispose(): void {
    this.controller.dispose();
  }
}
