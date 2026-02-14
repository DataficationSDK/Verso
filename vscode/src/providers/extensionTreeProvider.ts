import * as vscode from "vscode";
import { HostProcess } from "../host/hostProcess";
import { ExtensionListResult, ExtensionInfoDto } from "../host/protocol";

const categoryDisplayNames: Record<string, string> = {
  LanguageKernel: "Language Kernels",
  CellRenderer: "Cell Renderers",
  DataFormatter: "Data Formatters",
  CellType: "Cell Types",
  NotebookSerializer: "Serializers",
  Theme: "Themes",
  LayoutEngine: "Layout Engines",
  ToolbarAction: "Toolbar Actions",
  MagicCommand: "Magic Commands",
};

const categoryOrder: string[] = [
  "Language Kernels",
  "Themes",
  "Layout Engines",
  "Cell Types",
  "Cell Renderers",
  "Data Formatters",
  "Serializers",
  "Toolbar Actions",
  "Magic Commands",
];

const categoryIcons: Record<string, string> = {
  "Language Kernels": "symbol-event",
  Themes: "symbol-color",
  "Layout Engines": "layout",
  "Cell Types": "symbol-class",
  "Cell Renderers": "preview",
  "Data Formatters": "output",
  Serializers: "save",
  "Toolbar Actions": "tools",
  "Magic Commands": "wand",
};

type ExtensionTreeNode = ExtensionCategoryItem | ExtensionTreeItem;

export class ExtensionCategoryItem extends vscode.TreeItem {
  constructor(
    public readonly category: string,
    public readonly extensions: ExtensionInfoDto[]
  ) {
    super(category, vscode.TreeItemCollapsibleState.Collapsed);
    this.description = `${extensions.length}`;
    this.iconPath = new vscode.ThemeIcon(
      categoryIcons[category] ?? "extensions"
    );
    this.contextValue = "category";
  }
}

export class ExtensionTreeItem extends vscode.TreeItem {
  constructor(public readonly info: ExtensionInfoDto) {
    super(info.name, vscode.TreeItemCollapsibleState.None);
    this.description = `${info.version}`;
    this.tooltip = [
      info.name,
      `Version: ${info.version}`,
      info.author ? `Author: ${info.author}` : undefined,
      info.description ?? undefined,
      `Status: ${info.status}`,
      `Capabilities: ${info.capabilities.join(", ")}`,
    ]
      .filter(Boolean)
      .join("\n");

    this.contextValue = info.status.toLowerCase();
    this.iconPath = new vscode.ThemeIcon(
      info.status === "Enabled" ? "extensions" : "circle-slash"
    );
  }
}

export class ExtensionTreeProvider
  implements vscode.TreeDataProvider<ExtensionTreeNode>
{
  private _onDidChangeTreeData = new vscode.EventEmitter<
    ExtensionTreeNode | undefined | void
  >();
  readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

  private _extensions: ExtensionInfoDto[] = [];

  constructor(private readonly host: HostProcess) {}

  refresh(): void {
    this._extensions = [];
    this._onDidChangeTreeData.fire();
  }

  getTreeItem(element: ExtensionTreeNode): vscode.TreeItem {
    return element;
  }

  async getChildren(
    element?: ExtensionTreeNode
  ): Promise<ExtensionTreeNode[]> {
    if (element instanceof ExtensionCategoryItem) {
      return element.extensions.map((ext) => new ExtensionTreeItem(ext));
    }

    if (element instanceof ExtensionTreeItem) {
      return [];
    }

    // Root level: fetch and group
    try {
      const result = await this.host.sendRequest<ExtensionListResult>(
        "extension/list"
      );
      this._extensions = result.extensions;
      return this.buildCategoryNodes(result.extensions);
    } catch {
      return [];
    }
  }

  private buildCategoryNodes(
    extensions: ExtensionInfoDto[]
  ): ExtensionCategoryItem[] {
    const groups = new Map<string, ExtensionInfoDto[]>();

    for (const ext of extensions) {
      const primaryCap = ext.capabilities.length > 0 ? ext.capabilities[0] : "Other";
      const displayName = categoryDisplayNames[primaryCap] ?? primaryCap;

      let list = groups.get(displayName);
      if (!list) {
        list = [];
        groups.set(displayName, list);
      }
      list.push(ext);
    }

    return [...groups.entries()]
      .sort(([a], [b]) => {
        const ai = categoryOrder.indexOf(a);
        const bi = categoryOrder.indexOf(b);
        return (ai < 0 ? categoryOrder.length : ai) - (bi < 0 ? categoryOrder.length : bi);
      })
      .map(([category, exts]) => new ExtensionCategoryItem(category, exts));
  }
}
