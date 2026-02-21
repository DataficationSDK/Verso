import * as vscode from "vscode";
import { HostProcess } from "../host/hostProcess";
import { notebookRegistry } from "../host/notebookRegistry";
import {
  CellAddParams,
  CellDto,
  CellUpdateSourceParams,
  NotebookOpenParams,
  NotebookOpenResult,
  NotebookSaveResult,
} from "../host/protocol";

export class VersoSerializer implements vscode.NotebookSerializer {
  constructor(private readonly host: HostProcess) {}

  async deserializeNotebook(
    content: Uint8Array,
    _token: vscode.CancellationToken
  ): Promise<vscode.NotebookData> {
    const text = new TextDecoder().decode(content);

    const result = await this.host.sendRequest<NotebookOpenResult>(
      "notebook/open",
      { content: text } satisfies NotebookOpenParams
    );

    const notebookId = result.notebookId;

    const cells = result.cells.map((cell) => this.mapCellToNotebookData(cell));

    // If no cells, register a default code cell with the host so it
    // gets a proper versoId and can be executed immediately.
    if (cells.length === 0) {
      const cellData = new vscode.NotebookCellData(
        vscode.NotebookCellKind.Code,
        "",
        "csharp"
      );
      try {
        const added = await this.host.sendRequest<CellDto>("cell/add", {
          type: "code",
          language: "csharp",
          source: "",
          notebookId,
        } as CellAddParams & { notebookId: string });
        cellData.metadata = { versoId: added.id };
      } catch {
        // Host may not be ready — cell will be registered when opened later
      }
      cells.push(cellData);
    }

    const data = new vscode.NotebookData(cells);
    data.metadata = {
      title: result.title,
      defaultKernel: result.defaultKernel,
      notebookId,
    };
    return data;
  }

  async serializeNotebook(
    data: vscode.NotebookData,
    _token: vscode.CancellationToken
  ): Promise<Uint8Array> {
    const notebookId = data.metadata?.notebookId as string | undefined;

    // Sync all cell sources to the engine
    for (const cell of data.cells) {
      const versoId = cell.metadata?.versoId as string | undefined;
      if (versoId) {
        await this.host.sendRequest("cell/updateSource", {
          cellId: versoId,
          source: cell.value,
          notebookId,
        } as CellUpdateSourceParams & { notebookId?: string });
      }
    }

    const result = await this.host.sendRequest<NotebookSaveResult>(
      "notebook/save",
      { notebookId }
    );
    return new TextEncoder().encode(result.content);
  }

  private mapCellToNotebookData(cell: CellDto): vscode.NotebookCellData {
    const kind =
      cell.type === "markdown"
        ? vscode.NotebookCellKind.Markup
        : vscode.NotebookCellKind.Code;

    const language =
      cell.type === "markdown" ? "markdown" : cell.language ?? "csharp";

    const cellData = new vscode.NotebookCellData(kind, cell.source, language);

    cellData.metadata = { versoId: cell.id };

    if (cell.outputs.length > 0) {
      cellData.outputs = cell.outputs.map((output) => {
        if (output.isError) {
          return new vscode.NotebookCellOutput([
            vscode.NotebookCellOutputItem.error(
              new Error(output.content ?? output.errorName ?? "Error")
            ),
          ]);
        }
        const mimeType = output.mimeType || "text/plain";
        return new vscode.NotebookCellOutput([
          vscode.NotebookCellOutputItem.text(output.content, mimeType),
        ]);
      });
    }

    return cellData;
  }
}
