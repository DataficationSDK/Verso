import * as vscode from "vscode";
import { HostProcess } from "../host/hostProcess";
import { ThemeResult } from "../host/protocol";

const tokenTypeToScope: Record<string, string> = {
  keyword: "keyword",
  comment: "comment",
  string: "string",
  number: "constant.numeric",
  type: "entity.name.type",
  function: "entity.name.function",
  variable: "variable",
  operator: "keyword.operator",
  parameter: "variable.parameter",
  property: "variable.other.property",
  namespace: "entity.name.namespace",
  punctuation: "punctuation",
  "string.escape": "constant.character.escape",
  preprocessor: "meta.preprocessor",
};

export async function applyEngineTheme(host: HostProcess): Promise<void> {
  try {
    const theme = await host.sendRequest<ThemeResult | null>(
      "notebook/getTheme"
    );
    if (!theme) {
      return;
    }

    // Build TextMate token color customizations from engine syntax colors
    const tokenColorCustomizations: {
      scope: string;
      settings: { foreground: string };
    }[] = [];

    for (const [tokenType, color] of Object.entries(theme.syntaxColors)) {
      const scope = tokenTypeToScope[tokenType] ?? tokenType;
      tokenColorCustomizations.push({
        scope,
        settings: { foreground: color },
      });
    }

    // Apply as workspace-level textmate token customizations
    if (tokenColorCustomizations.length > 0) {
      const config = vscode.workspace.getConfiguration("editor");
      await config.update(
        "tokenColorCustomizations",
        { textMateRules: tokenColorCustomizations },
        vscode.ConfigurationTarget.Workspace
      );
    }
  } catch {
    // Theme application is best-effort
  }
}
