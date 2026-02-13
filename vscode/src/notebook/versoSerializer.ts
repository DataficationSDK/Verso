import * as vscode from "vscode";
import { HostProcess } from "../host/hostProcess";
import {
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

    const cells = result.cells.map((cell) => this.mapCellToNotebookData(cell));

    // If no cells, add an empty code cell
    if (cells.length === 0) {
      cells.push(
        new vscode.NotebookCellData(
          vscode.NotebookCellKind.Code,
          "",
          "csharp"
        )
      );
    }

    const data = new vscode.NotebookData(cells);
    data.metadata = {
      title: result.title,
      defaultKernel: result.defaultKernel,
    };
    return data;
  }

  async serializeNotebook(
    data: vscode.NotebookData,
    _token: vscode.CancellationToken
  ): Promise<Uint8Array> {
    // Sync all cell sources to the engine
    for (const cell of data.cells) {
      const versoId = cell.metadata?.versoId as string | undefined;
      if (versoId) {
        await this.host.sendRequest("cell/updateSource", {
          cellId: versoId,
          source: cell.value,
        } satisfies CellUpdateSourceParams);
      }
    }

    const result = await this.host.sendRequest<NotebookSaveResult>(
      "notebook/save"
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
