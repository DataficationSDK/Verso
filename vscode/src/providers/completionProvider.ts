import * as vscode from "vscode";
import { HostProcess } from "../host/hostProcess";
import { resolveNotebookIdForCell } from "../host/notebookRegistry";
import { CompletionsParams, CompletionsResult } from "../host/protocol";

const completionKindMap: Record<string, vscode.CompletionItemKind> = {
  Method: vscode.CompletionItemKind.Method,
  Property: vscode.CompletionItemKind.Property,
  Field: vscode.CompletionItemKind.Field,
  Class: vscode.CompletionItemKind.Class,
  Interface: vscode.CompletionItemKind.Interface,
  Struct: vscode.CompletionItemKind.Struct,
  Enum: vscode.CompletionItemKind.Enum,
  EnumMember: vscode.CompletionItemKind.EnumMember,
  Variable: vscode.CompletionItemKind.Variable,
  Function: vscode.CompletionItemKind.Function,
  Namespace: vscode.CompletionItemKind.Module,
  Keyword: vscode.CompletionItemKind.Keyword,
  Snippet: vscode.CompletionItemKind.Snippet,
  Event: vscode.CompletionItemKind.Event,
  Operator: vscode.CompletionItemKind.Operator,
  TypeParameter: vscode.CompletionItemKind.TypeParameter,
  Constant: vscode.CompletionItemKind.Constant,
};

export class VersoCompletionProvider implements vscode.CompletionItemProvider {
  constructor(private readonly host: HostProcess) {}

  async provideCompletionItems(
    document: vscode.TextDocument,
    position: vscode.Position,
    _token: vscode.CancellationToken,
    _context: vscode.CompletionContext
  ): Promise<vscode.CompletionItem[]> {
    const notebookId = resolveNotebookIdForCell(document);

    const cell = vscode.window.activeNotebookEditor?.notebook
      .getCells()
      .find((c) => c.document.uri.toString() === document.uri.toString());

    const versoId = cell?.metadata?.versoId as string | undefined;
    if (!versoId) {
      return [];
    }

    const code = document.getText();
    const cursorPosition = document.offsetAt(position);

    try {
      const result = await this.host.sendRequest<CompletionsResult>(
        "kernel/getCompletions",
        {
          cellId: versoId,
          code,
          cursorPosition,
          notebookId,
        } as CompletionsParams & { notebookId?: string }
      );

      return result.items.map((item) => {
        const completion = new vscode.CompletionItem(
          item.displayText,
          completionKindMap[item.kind] ?? vscode.CompletionItemKind.Text
        );
        completion.insertText = item.insertText;
        completion.detail = item.description;
        completion.sortText = item.sortText;
        return completion;
      });
    } catch {
      return [];
    }
  }
}
