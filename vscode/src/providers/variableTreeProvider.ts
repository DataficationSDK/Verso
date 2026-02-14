import * as vscode from "vscode";
import { HostProcess } from "../host/hostProcess";
import { VariableListResult, VariableEntryDto } from "../host/protocol";

export class VariableTreeItem extends vscode.TreeItem {
  constructor(public readonly entry: VariableEntryDto) {
    super(entry.name, vscode.TreeItemCollapsibleState.None);
    this.description = `${entry.typeName} = ${entry.valuePreview}`;
    this.tooltip = [
      `Name: ${entry.name}`,
      `Type: ${entry.typeName}`,
      `Value: ${entry.valuePreview}`,
    ].join("\n");
    this.contextValue = "variable";
    this.iconPath = new vscode.ThemeIcon("symbol-variable");
  }
}

export class VariableTreeProvider
  implements vscode.TreeDataProvider<VariableTreeItem>
{
  private _onDidChangeTreeData = new vscode.EventEmitter<
    VariableTreeItem | undefined | void
  >();
  readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

  constructor(private readonly host: HostProcess) {}

  refresh(): void {
    this._onDidChangeTreeData.fire();
  }

  getTreeItem(element: VariableTreeItem): vscode.TreeItem {
    return element;
  }

  async getChildren(
    _element?: VariableTreeItem
  ): Promise<VariableTreeItem[]> {
    if (_element) {
      return [];
    }

    try {
      const result = await this.host.sendRequest<VariableListResult>(
        "variable/list"
      );
      return result.variables.map((v) => new VariableTreeItem(v));
    } catch {
      return [];
    }
  }
}
