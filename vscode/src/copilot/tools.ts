import * as vscode from "vscode";
import * as path from "path";
import { HostProcess } from "../host/hostProcess";
import { BlazorBridge } from "../blazor/blazorBridge";
import { hostRegistry } from "../host/hostRegistry";
import { notebookRegistry } from "../host/notebookRegistry";
import {
  CellDto,
  ExecutionResultDto,
  ExecutionRunAllResult,
  LanguagesResult,
  ParameterDefDto,
  ParameterListResult,
  VariableListResult,
  VariableInspectResult,
} from "../host/protocol";

// ── Notebook resolution ─────────────────────────────────────────────

export interface NotebookContext {
  host: HostProcess;
  bridge: BlazorBridge;
  notebookId: string;
  uri: vscode.Uri;
}

export async function resolveNotebook(): Promise<NotebookContext | undefined> {
  const entries = hostRegistry.entries();
  if (entries.length === 0) {
    return undefined;
  }

  let uriStr: string;
  let host: HostProcess;
  let bridge: BlazorBridge;

  if (entries.length === 1) {
    const [u, session] = entries[0];
    uriStr = u;
    host = session.host;
    bridge = session.bridge;
  } else {
    const items = entries.map(([u, session]) => {
      const uri = vscode.Uri.parse(u);
      return {
        label: path.basename(uri.fsPath),
        description: uri.fsPath,
        uriStr: u,
        session,
      };
    });
    const picked = await vscode.window.showQuickPick(items, {
      placeHolder: "Select a notebook for @verso",
    });
    if (!picked) {
      return undefined;
    }
    uriStr = picked.uriStr;
    host = picked.session.host;
    bridge = picked.session.bridge;
  }

  const uri = vscode.Uri.parse(uriStr);
  const notebookId = notebookRegistry.getByUri(uri);
  if (!notebookId) {
    return undefined;
  }

  return { host, bridge, notebookId, uri };
}

// ── Helpers ─────────────────────────────────────────────────────────

async function listCellsRaw(ctx: NotebookContext): Promise<CellDto[]> {
  const result = await ctx.host.sendRequest<{ cells: CellDto[] }>(
    "cell/list",
    { notebookId: ctx.notebookId }
  );
  return result.cells;
}

/**
 * Notify the WASM webview that cell data has changed so it re-fetches
 * from the host, and mark the document as dirty so VS Code shows the
 * unsaved indicator. The WASM app already handles "output/update" by
 * calling RefreshCellListAsync() which does a full cell/list round-trip.
 */
function notifyWebviewChanged(ctx: NotebookContext): void {
  ctx.bridge.notify("output/update", { notebookId: ctx.notebookId });
  ctx.bridge.markDirty();
}

function formatCellOutput(cell: CellDto): string {
  if (!cell.outputs || cell.outputs.length === 0) {
    return "";
  }
  const parts: string[] = [];
  for (const output of cell.outputs) {
    if (output.isError) {
      parts.push(`Error: ${output.errorName ?? ""}  ${output.content}`);
    } else if (
      output.mimeType === "text/plain" ||
      output.mimeType === "text/csv"
    ) {
      parts.push(output.content);
    } else if (output.mimeType.startsWith("text/html")) {
      parts.push("[HTML output]");
    } else {
      parts.push(`[${output.mimeType} output]`);
    }
  }
  return parts.join("\n");
}

function formatCellSummary(cell: CellDto, index: number): string {
  const lang = cell.language ?? cell.type;
  const source =
    cell.source.length > 500
      ? cell.source.substring(0, 500) + "..."
      : cell.source;
  const output = formatCellOutput(cell);
  let text = `Cell ${index + 1} [${lang}] (id: ${cell.id}):\n\`\`\`${lang}\n${source}\n\`\`\``;
  if (output) {
    text += `\nOutput:\n${output}`;
  }
  return text;
}

function resolveCell(
  cells: CellDto[],
  cellNumber: number
): CellDto | undefined {
  return cells[cellNumber - 1];
}

function textResult(text: string): vscode.LanguageModelToolResult {
  return new vscode.LanguageModelToolResult([
    new vscode.LanguageModelTextPart(text),
  ]);
}

// ── Tool implementations ────────────────────────────────────────────

export class ListCellsTool
  implements vscode.LanguageModelTool<Record<string, never>>
{
  async invoke(
    _options: vscode.LanguageModelToolInvocationOptions<Record<string, never>>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const ctx = await resolveNotebook();
    if (!ctx) {
      return textResult("No Verso notebook is currently open.");
    }
    const cells = await listCellsRaw(ctx);
    if (cells.length === 0) {
      return textResult("The notebook is empty (no cells).");
    }
    const summary = cells
      .map((c, i) => formatCellSummary(c, i))
      .join("\n\n");
    return textResult(
      `Notebook: ${path.basename(ctx.uri.fsPath)}\n${cells.length} cell(s):\n\n${summary}`
    );
  }
}

export class AddCellTool
  implements
    vscode.LanguageModelTool<{
      language: string;
      source: string;
      type?: string;
      position?: number;
    }>
{
  async prepareInvocation(
    options: vscode.LanguageModelToolInvocationPrepareOptions<{
      language: string;
      source: string;
      type?: string;
      position?: number;
    }>,
    _token: vscode.CancellationToken
  ) {
    const lang = options.input.language;
    const lines = options.input.source.split("\n").length;
    return {
      invocationMessage: `Adding ${lang} cell (${lines} line${lines === 1 ? "" : "s"})`,
    };
  }

  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<{
      language: string;
      source: string;
      type?: string;
      position?: number;
    }>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const ctx = await resolveNotebook();
    if (!ctx) {
      return textResult("No Verso notebook is currently open.");
    }

    const { language, source, type, position } = options.input;
    const cellType = type ?? "code";

    const params: Record<string, unknown> = {
      notebookId: ctx.notebookId,
      type: cellType,
      source,
    };
    // Only include language for code cells
    if (language) {
      params.language = language;
    }

    let cell: CellDto;
    if (position !== undefined) {
      params.index = position - 1;
      cell = await ctx.host.sendRequest<CellDto>("cell/insert", params);
    } else {
      cell = await ctx.host.sendRequest<CellDto>("cell/add", params);
    }

    notifyWebviewChanged(ctx);

    const label = language ?? cellType;
    return textResult(
      `Added ${label} cell (id: ${cell.id})${position ? ` at position ${position}` : " at the end"}.`
    );
  }
}

export class UpdateCellTool
  implements
    vscode.LanguageModelTool<{ cellNumber: number; source: string }>
{
  async prepareInvocation(
    options: vscode.LanguageModelToolInvocationPrepareOptions<{
      cellNumber: number;
      source: string;
    }>,
    _token: vscode.CancellationToken
  ) {
    return {
      invocationMessage: `Updating cell ${options.input.cellNumber}`,
    };
  }

  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<{
      cellNumber: number;
      source: string;
    }>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const ctx = await resolveNotebook();
    if (!ctx) {
      return textResult("No Verso notebook is currently open.");
    }

    const cells = await listCellsRaw(ctx);
    const cell = resolveCell(cells, options.input.cellNumber);
    if (!cell) {
      return textResult(
        `Cell ${options.input.cellNumber} not found. The notebook has ${cells.length} cell(s).`
      );
    }

    await ctx.host.sendRequest("cell/updateSource", {
      notebookId: ctx.notebookId,
      cellId: cell.id,
      source: options.input.source,
    });

    notifyWebviewChanged(ctx);

    return textResult(`Updated cell ${options.input.cellNumber}.`);
  }
}

export class RemoveCellTool
  implements vscode.LanguageModelTool<{ cellNumber: number }>
{
  async prepareInvocation(
    options: vscode.LanguageModelToolInvocationPrepareOptions<{
      cellNumber: number;
    }>,
    _token: vscode.CancellationToken
  ) {
    return {
      invocationMessage: `Removing cell ${options.input.cellNumber}`,
      confirmationMessages: {
        title: "Remove cell",
        message: new vscode.MarkdownString(
          `Remove cell **${options.input.cellNumber}** from the notebook?`
        ),
      },
    };
  }

  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<{
      cellNumber: number;
    }>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const ctx = await resolveNotebook();
    if (!ctx) {
      return textResult("No Verso notebook is currently open.");
    }

    const cells = await listCellsRaw(ctx);
    const cell = resolveCell(cells, options.input.cellNumber);
    if (!cell) {
      return textResult(
        `Cell ${options.input.cellNumber} not found. The notebook has ${cells.length} cell(s).`
      );
    }

    await ctx.host.sendRequest("cell/remove", {
      notebookId: ctx.notebookId,
      cellId: cell.id,
    });

    notifyWebviewChanged(ctx);

    return textResult(`Removed cell ${options.input.cellNumber}.`);
  }
}

export class RunCellTool
  implements vscode.LanguageModelTool<{ cellNumber: number }>
{
  async prepareInvocation(
    options: vscode.LanguageModelToolInvocationPrepareOptions<{
      cellNumber: number;
    }>,
    _token: vscode.CancellationToken
  ) {
    return {
      invocationMessage: `Running cell ${options.input.cellNumber}`,
    };
  }

  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<{
      cellNumber: number;
    }>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const ctx = await resolveNotebook();
    if (!ctx) {
      return textResult("No Verso notebook is currently open.");
    }

    const cells = await listCellsRaw(ctx);
    const cell = resolveCell(cells, options.input.cellNumber);
    if (!cell) {
      return textResult(
        `Cell ${options.input.cellNumber} not found. The notebook has ${cells.length} cell(s).`
      );
    }

    const result = await ctx.host.sendRequest<ExecutionResultDto>(
      "execution/run",
      {
        notebookId: ctx.notebookId,
        cellId: cell.id,
      }
    );

    notifyWebviewChanged(ctx);

    const output = formatCellOutput({
      ...cell,
      outputs: result.outputs,
    });
    let text = `Cell ${options.input.cellNumber}: ${result.status} (${result.elapsedMs}ms)`;
    if (result.errorMessage) {
      text += `\nError: ${result.errorMessage}`;
    }
    if (output) {
      text += `\nOutput:\n${output}`;
    }
    return textResult(text);
  }
}

export class RunAllTool
  implements vscode.LanguageModelTool<Record<string, never>>
{
  async prepareInvocation(
    _options: vscode.LanguageModelToolInvocationPrepareOptions<Record<string, never>>,
    _token: vscode.CancellationToken
  ) {
    return {
      invocationMessage: "Running all cells",
    };
  }

  async invoke(
    _options: vscode.LanguageModelToolInvocationOptions<Record<string, never>>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const ctx = await resolveNotebook();
    if (!ctx) {
      return textResult("No Verso notebook is currently open.");
    }

    const result = await ctx.host.sendRequest<ExecutionRunAllResult>(
      "execution/runAll",
      { notebookId: ctx.notebookId }
    );

    notifyWebviewChanged(ctx);

    const cells = await listCellsRaw(ctx);
    const summaries = result.results.map((r, i) => {
      const cell = cells.find((c) => c.id === r.cellId);
      const lang = cell?.language ?? cell?.type ?? "unknown";
      const output = formatCellOutput({
        id: r.cellId,
        type: cell?.type ?? "code",
        language: lang,
        source: "",
        outputs: r.outputs,
      });
      let text = `Cell ${i + 1} [${lang}]: ${r.status} (${r.elapsedMs}ms)`;
      if (r.errorMessage) {
        text += ` - ${r.errorMessage}`;
      }
      if (output) {
        text += `\n${output}`;
      }
      return text;
    });

    return textResult(summaries.join("\n\n"));
  }
}

export class ListVariablesTool
  implements vscode.LanguageModelTool<Record<string, never>>
{
  async invoke(
    _options: vscode.LanguageModelToolInvocationOptions<Record<string, never>>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const ctx = await resolveNotebook();
    if (!ctx) {
      return textResult("No Verso notebook is currently open.");
    }

    const result = await ctx.host.sendRequest<VariableListResult>(
      "variable/list",
      { notebookId: ctx.notebookId }
    );

    if (result.variables.length === 0) {
      return textResult("No variables in scope. Run some cells first.");
    }

    const lines = result.variables.map(
      (v) => `${v.name} (${v.typeName}): ${v.valuePreview}`
    );
    return textResult(lines.join("\n"));
  }
}

export class InspectVariableTool
  implements vscode.LanguageModelTool<{ name: string }>
{
  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<{ name: string }>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const ctx = await resolveNotebook();
    if (!ctx) {
      return textResult("No Verso notebook is currently open.");
    }

    const result = await ctx.host.sendRequest<VariableInspectResult>(
      "variable/inspect",
      { notebookId: ctx.notebookId, name: options.input.name }
    );

    return textResult(
      `${result.name} (${result.typeName}):\n${result.content}`
    );
  }
}

export class GetLanguagesTool
  implements vscode.LanguageModelTool<Record<string, never>>
{
  async invoke(
    _options: vscode.LanguageModelToolInvocationOptions<Record<string, never>>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const ctx = await resolveNotebook();
    if (!ctx) {
      return textResult("No Verso notebook is currently open.");
    }

    const result = await ctx.host.sendRequest<LanguagesResult>(
      "notebook/getLanguages",
      { notebookId: ctx.notebookId }
    );

    const lines = result.languages.map(
      (l) => `${l.id}: ${l.displayName}`
    );
    return textResult(`Available languages:\n${lines.join("\n")}`);
  }
}

// ── Parameter tools ─────────────────────────────────────────────────

export class ListParametersTool
  implements vscode.LanguageModelTool<Record<string, never>>
{
  async invoke(
    _options: vscode.LanguageModelToolInvocationOptions<Record<string, never>>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const ctx = await resolveNotebook();
    if (!ctx) {
      return textResult("No Verso notebook is currently open.");
    }

    const result = await ctx.host.sendRequest<ParameterListResult>(
      "parameter/list",
      { notebookId: ctx.notebookId }
    );

    const entries = Object.entries(result.parameters);
    if (entries.length === 0) {
      return textResult("No parameters defined in this notebook.");
    }

    const lines = entries.map(([name, def]) => {
      const parts = [`${name} (${def.type})`];
      if (def.description) {
        parts.push(`- ${def.description}`);
      }
      if (def.default !== undefined && def.default !== null) {
        parts.push(`default: ${def.default}`);
      }
      if (def.required) {
        parts.push("[required]");
      }
      return parts.join(" ");
    });

    return textResult(`Parameters:\n${lines.join("\n")}`);
  }
}

export class AddParameterTool
  implements
    vscode.LanguageModelTool<{
      name: string;
      type: string;
      description?: string;
      defaultValue?: string;
      required?: boolean;
    }>
{
  async prepareInvocation(
    options: vscode.LanguageModelToolInvocationPrepareOptions<{
      name: string;
      type: string;
      description?: string;
      defaultValue?: string;
      required?: boolean;
    }>,
    _token: vscode.CancellationToken
  ) {
    return {
      invocationMessage: `Adding parameter "${options.input.name}" (${options.input.type})`,
    };
  }

  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<{
      name: string;
      type: string;
      description?: string;
      defaultValue?: string;
      required?: boolean;
    }>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const ctx = await resolveNotebook();
    if (!ctx) {
      return textResult("No Verso notebook is currently open.");
    }

    const { name, type, description, defaultValue, required } = options.input;

    await ctx.host.sendRequest("parameter/add", {
      notebookId: ctx.notebookId,
      name,
      type,
      description,
      defaultValue,
      required,
    });

    notifyWebviewChanged(ctx);

    return textResult(
      `Added parameter "${name}" (${type})${required ? " [required]" : ""}.`
    );
  }
}

export class UpdateParameterTool
  implements
    vscode.LanguageModelTool<{
      name: string;
      type?: string;
      description?: string;
      defaultValue?: string;
      required?: boolean;
    }>
{
  async prepareInvocation(
    options: vscode.LanguageModelToolInvocationPrepareOptions<{
      name: string;
      type?: string;
      description?: string;
      defaultValue?: string;
      required?: boolean;
    }>,
    _token: vscode.CancellationToken
  ) {
    return {
      invocationMessage: `Updating parameter "${options.input.name}"`,
    };
  }

  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<{
      name: string;
      type?: string;
      description?: string;
      defaultValue?: string;
      required?: boolean;
    }>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const ctx = await resolveNotebook();
    if (!ctx) {
      return textResult("No Verso notebook is currently open.");
    }

    const { name, type, description, defaultValue, required } = options.input;

    await ctx.host.sendRequest("parameter/update", {
      notebookId: ctx.notebookId,
      name,
      type,
      description,
      defaultValue,
      required,
    });

    notifyWebviewChanged(ctx);

    const changes: string[] = [];
    if (type !== undefined) changes.push(`type=${type}`);
    if (description !== undefined) changes.push(`description="${description}"`);
    if (defaultValue !== undefined) changes.push(`default=${defaultValue}`);
    if (required !== undefined) changes.push(`required=${required}`);

    return textResult(
      `Updated parameter "${name}": ${changes.join(", ")}.`
    );
  }
}

export class RemoveParameterTool
  implements vscode.LanguageModelTool<{ name: string }>
{
  async prepareInvocation(
    options: vscode.LanguageModelToolInvocationPrepareOptions<{
      name: string;
    }>,
    _token: vscode.CancellationToken
  ) {
    return {
      invocationMessage: `Removing parameter "${options.input.name}"`,
      confirmationMessages: {
        title: "Remove parameter",
        message: new vscode.MarkdownString(
          `Remove parameter **${options.input.name}** from the notebook?`
        ),
      },
    };
  }

  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<{ name: string }>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const ctx = await resolveNotebook();
    if (!ctx) {
      return textResult("No Verso notebook is currently open.");
    }

    await ctx.host.sendRequest("parameter/remove", {
      notebookId: ctx.notebookId,
      name: options.input.name,
    });

    notifyWebviewChanged(ctx);

    return textResult(`Removed parameter "${options.input.name}".`);
  }
}

// ── Registration ────────────────────────────────────────────────────

export function registerTools(
  context: vscode.ExtensionContext
): void {
  context.subscriptions.push(
    vscode.lm.registerTool("verso_listCells", new ListCellsTool()),
    vscode.lm.registerTool("verso_addCell", new AddCellTool()),
    vscode.lm.registerTool("verso_updateCell", new UpdateCellTool()),
    vscode.lm.registerTool("verso_removeCell", new RemoveCellTool()),
    vscode.lm.registerTool("verso_runCell", new RunCellTool()),
    vscode.lm.registerTool("verso_runAll", new RunAllTool()),
    vscode.lm.registerTool("verso_listVariables", new ListVariablesTool()),
    vscode.lm.registerTool(
      "verso_inspectVariable",
      new InspectVariableTool()
    ),
    vscode.lm.registerTool("verso_getLanguages", new GetLanguagesTool()),
    vscode.lm.registerTool(
      "verso_listParameters",
      new ListParametersTool()
    ),
    vscode.lm.registerTool("verso_addParameter", new AddParameterTool()),
    vscode.lm.registerTool(
      "verso_updateParameter",
      new UpdateParameterTool()
    ),
    vscode.lm.registerTool(
      "verso_removeParameter",
      new RemoveParameterTool()
    )
  );
}
