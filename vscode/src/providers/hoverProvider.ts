import * as vscode from "vscode";
import { HostProcess } from "../host/hostProcess";
import { HoverParams, HoverResult } from "../host/protocol";

export class VersoHoverProvider implements vscode.HoverProvider {
  constructor(private readonly host: HostProcess) {}

  async provideHover(
    document: vscode.TextDocument,
    position: vscode.Position,
    _token: vscode.CancellationToken
  ): Promise<vscode.Hover | undefined> {
    const cell = vscode.window.activeNotebookEditor?.notebook
      .getCells()
      .find((c) => c.document.uri.toString() === document.uri.toString());

    const versoId = cell?.metadata?.versoId as string | undefined;
    if (!versoId) {
      return undefined;
    }

    const code = document.getText();
    const cursorPosition = document.offsetAt(position);

    try {
      const result = await this.host.sendRequest<HoverResult | null>(
        "kernel/getHoverInfo",
        {
          cellId: versoId,
          code,
          cursorPosition,
        } satisfies HoverParams
      );

      if (!result?.content) {
        return undefined;
      }

      const markdown = new vscode.MarkdownString(result.content);
      markdown.isTrusted = true;

      let range: vscode.Range | undefined;
      if (result.range) {
        range = new vscode.Range(
          result.range.startLine,
          result.range.startColumn,
          result.range.endLine,
          result.range.endColumn
        );
      }

      return new vscode.Hover(markdown, range);
    } catch {
      return undefined;
    }
  }
}
