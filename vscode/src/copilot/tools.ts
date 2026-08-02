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
  LayoutsResult,
  ParameterDefDto,
  ParameterListResult,
  PropertiesGetSectionsResult,
  PropertySectionResultDto,
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
      // @verso is how the participant is addressed, so it is typed, not translated.
      placeHolder: vscode.l10n.t({
        message: "Select a notebook for @verso",
        comment: ["@verso is typed to address the assistant and stays as written."],
      }),
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

/**
 * Wraps a tool's answer for the model.
 *
 * What goes through here is read by the model, not by anyone, and it stays in English.
 * The model reasons over these answers and quotes them back in whatever language the
 * conversation is in, so translating them would leave one turn of a conversation written
 * in two languages while gaining nothing a reader would ever see. What a reader does see
 * while a tool runs, its name and the line describing the call, is translated.
 */
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
    // Two entries rather than a "line(s)" no other language can copy. See the note on
    // cellCount in participant.ts for why the choice is made here and not by the
    // translation.
    const counted =
      lines === 1
        ? vscode.l10n.t({
            message: "{0} line",
            args: [lines],
            comment: ["Used when {0} is 1. Paired with the entry below."],
          })
        : vscode.l10n.t({
            message: "{0} lines",
            args: [lines],
            comment: [
              "Used for every count other than 1. A language with one form for both translates this the same as the entry above.",
            ],
          });
    return {
      invocationMessage: vscode.l10n.t("Adding {0} cell ({1})", lang, counted),
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
      invocationMessage: vscode.l10n.t(
        "Updating cell {0}",
        options.input.cellNumber
      ),
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
      invocationMessage: vscode.l10n.t(
        "Removing cell {0}",
        options.input.cellNumber
      ),
      confirmationMessages: {
        title: vscode.l10n.t("Remove cell"),
        message: new vscode.MarkdownString(
          vscode.l10n.t(
            "Remove cell **{0}** from the notebook?",
            options.input.cellNumber
          )
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
      invocationMessage: vscode.l10n.t(
        "Running cell {0}",
        options.input.cellNumber
      ),
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
      invocationMessage: vscode.l10n.t("Running all cells"),
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
      invocationMessage: vscode.l10n.t(
        'Adding parameter "{0}" ({1})',
        options.input.name,
        options.input.type
      ),
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
      invocationMessage: vscode.l10n.t(
        'Updating parameter "{0}"',
        options.input.name
      ),
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
      invocationMessage: vscode.l10n.t(
        'Removing parameter "{0}"',
        options.input.name
      ),
      confirmationMessages: {
        title: vscode.l10n.t("Remove parameter"),
        message: new vscode.MarkdownString(
          vscode.l10n.t(
            "Remove parameter **{0}** from the notebook?",
            options.input.name
          )
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

// ── Property tools ─────────────────────────────────────────────────

function formatPropertySection(result: PropertySectionResultDto): string {
  const section = result.section;
  const lines: string[] = [`**${section.title}**`];
  if (section.description) {
    lines.push(section.description);
  }
  for (const field of section.fields) {
    const ro = field.isReadOnly ? " [read-only]" : "";
    const value = field.currentValue ?? "(not set)";
    let line = `- ${field.displayName} (${field.fieldType}${ro}): ${value}`;
    if (field.options && field.options.length > 0) {
      const opts = field.options.map((o) => o.displayName).join(", ");
      line += ` [options: ${opts}]`;
    }
    lines.push(line);
  }
  return lines.join("\n");
}

export class GetCellPropertiesTool
  implements vscode.LanguageModelTool<{ cellNumber: number }>
{
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

    const result = await ctx.host.sendRequest<PropertiesGetSectionsResult>(
      "properties/getSections",
      { notebookId: ctx.notebookId, cellId: cell.id }
    );

    if (result.sections.length === 0) {
      return textResult(
        `Cell ${options.input.cellNumber} has no configurable properties.`
      );
    }

    const text = result.sections
      .map((s) => formatPropertySection(s))
      .join("\n\n");
    return textResult(
      `Properties for cell ${options.input.cellNumber}:\n\n${text}`
    );
  }
}

export class UpdateCellPropertyTool
  implements
    vscode.LanguageModelTool<{
      cellNumber: number;
      providerExtensionId: string;
      propertyName: string;
      value: string;
    }>
{
  async prepareInvocation(
    options: vscode.LanguageModelToolInvocationPrepareOptions<{
      cellNumber: number;
      providerExtensionId: string;
      propertyName: string;
      value: string;
    }>,
    _token: vscode.CancellationToken
  ) {
    return {
      invocationMessage: vscode.l10n.t(
        'Setting "{0}" on cell {1}',
        options.input.propertyName,
        options.input.cellNumber
      ),
    };
  }

  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<{
      cellNumber: number;
      providerExtensionId: string;
      propertyName: string;
      value: string;
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

    const { providerExtensionId, propertyName, value } = options.input;

    await ctx.host.sendRequest("properties/updateProperty", {
      notebookId: ctx.notebookId,
      cellId: cell.id,
      providerExtensionId,
      propertyName,
      value,
    });

    notifyWebviewChanged(ctx);

    return textResult(
      `Updated "${propertyName}" to "${value}" on cell ${options.input.cellNumber}.`
    );
  }
}

// ── Layout tools ────────────────────────────────────────────────────

export class ListLayoutsTool
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

    const result = await ctx.host.sendRequest<LayoutsResult>(
      "layout/getLayouts",
      { notebookId: ctx.notebookId }
    );

    if (result.layouts.length === 0) {
      return textResult("No layouts are registered.");
    }

    const lines = result.layouts.map((l) => {
      const active = l.isActive ? " (active)" : "";
      return `${l.displayName}${active} - extensionId: "${l.extensionId}", layoutId: "${l.id}"`;
    });
    return textResult(
      `Available layouts (pass both extensionId and layoutId to verso_switchLayout):\n${lines.join("\n")}`
    );
  }
}

export class SwitchLayoutTool
  implements
    vscode.LanguageModelTool<{ extensionId: string; layoutId: string }>
{
  async prepareInvocation(
    options: vscode.LanguageModelToolInvocationPrepareOptions<{
      extensionId: string;
      layoutId: string;
    }>,
    _token: vscode.CancellationToken
  ) {
    return {
      invocationMessage: vscode.l10n.t(
        'Switching layout to "{0}"',
        options.input.layoutId
      ),
    };
  }

  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<{
      extensionId: string;
      layoutId: string;
    }>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const ctx = await resolveNotebook();
    if (!ctx) {
      return textResult("No Verso notebook is currently open.");
    }

    const { extensionId, layoutId } = options.input;

    // Resolve against the registered layouts so the call always carries the
    // qualified (extensionId, layoutId) pair, even if the model supplied only
    // a bare layoutId (the host's bare-string path is deprecated).
    const layouts = await ctx.host.sendRequest<LayoutsResult>(
      "layout/getLayouts",
      { notebookId: ctx.notebookId }
    );
    const target = layouts.layouts.find(
      (l) =>
        l.id.toLowerCase() === layoutId.toLowerCase() &&
        (!extensionId ||
          l.extensionId.toLowerCase() === extensionId.toLowerCase())
    );
    if (!target) {
      const available = layouts.layouts
        .map((l) => `"${l.id}" (${l.extensionId})`)
        .join(", ");
      return textResult(
        `Layout "${layoutId}" not found. Call verso_listLayouts first. Available: ${available || "none"}.`
      );
    }

    await ctx.host.sendRequest("layout/switch", {
      notebookId: ctx.notebookId,
      extensionId: target.extensionId,
      layoutId: target.id,
    });
    // The host switched server-side, but the webview drives its own active-layout
    // state, so tell it to re-sync and swap the rendered view to the new layout.
    ctx.bridge.notify("layout/activeChanged", {
      notebookId: ctx.notebookId,
      extensionId: target.extensionId,
      layoutId: target.id,
    });
    ctx.bridge.markDirty();

    return textResult(
      `Switched the notebook layout to "${target.displayName}". It is now the active layout and the choice is saved with the notebook.`
    );
  }
}

// ── Structural cell tools ───────────────────────────────────────────

export class MoveCellTool
  implements
    vscode.LanguageModelTool<{ cellNumber: number; toPosition: number }>
{
  async prepareInvocation(
    options: vscode.LanguageModelToolInvocationPrepareOptions<{
      cellNumber: number;
      toPosition: number;
    }>,
    _token: vscode.CancellationToken
  ) {
    return {
      invocationMessage: vscode.l10n.t(
        "Moving cell {0} to position {1}",
        options.input.cellNumber,
        options.input.toPosition
      ),
    };
  }

  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<{
      cellNumber: number;
      toPosition: number;
    }>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const ctx = await resolveNotebook();
    if (!ctx) {
      return textResult("No Verso notebook is currently open.");
    }

    const cells = await listCellsRaw(ctx);
    const { cellNumber, toPosition } = options.input;
    if (!resolveCell(cells, cellNumber)) {
      return textResult(
        `Cell ${cellNumber} not found. The notebook has ${cells.length} cell(s).`
      );
    }
    if (toPosition < 1 || toPosition > cells.length) {
      return textResult(
        `Target position ${toPosition} is out of range (1..${cells.length}).`
      );
    }

    await ctx.host.sendRequest("cell/move", {
      notebookId: ctx.notebookId,
      fromIndex: cellNumber - 1,
      toIndex: toPosition - 1,
    });

    notifyWebviewChanged(ctx);

    return textResult(`Moved cell ${cellNumber} to position ${toPosition}.`);
  }
}

export class ChangeCellTypeTool
  implements vscode.LanguageModelTool<{ cellNumber: number; type: string }>
{
  async prepareInvocation(
    options: vscode.LanguageModelToolInvocationPrepareOptions<{
      cellNumber: number;
      type: string;
    }>,
    _token: vscode.CancellationToken
  ) {
    return {
      invocationMessage: vscode.l10n.t(
        'Changing cell {0} to type "{1}"',
        options.input.cellNumber,
        options.input.type
      ),
    };
  }

  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<{
      cellNumber: number;
      type: string;
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

    const result = await ctx.host.sendRequest<{ success: boolean }>(
      "cell/changeType",
      {
        notebookId: ctx.notebookId,
        cellId: cell.id,
        type: options.input.type,
      }
    );
    if (!result.success) {
      return textResult(
        `Could not change cell ${options.input.cellNumber} to type "${options.input.type}".`
      );
    }

    notifyWebviewChanged(ctx);

    return textResult(
      `Changed cell ${options.input.cellNumber} to type "${options.input.type}". Changing the type clears its previous outputs.`
    );
  }
}

export class ChangeCellLanguageTool
  implements
    vscode.LanguageModelTool<{ cellNumber: number; language: string }>
{
  async prepareInvocation(
    options: vscode.LanguageModelToolInvocationPrepareOptions<{
      cellNumber: number;
      language: string;
    }>,
    _token: vscode.CancellationToken
  ) {
    return {
      invocationMessage: vscode.l10n.t(
        'Changing cell {0} language to "{1}"',
        options.input.cellNumber,
        options.input.language
      ),
    };
  }

  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<{
      cellNumber: number;
      language: string;
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

    const result = await ctx.host.sendRequest<{ success: boolean }>(
      "cell/changeLanguage",
      {
        notebookId: ctx.notebookId,
        cellId: cell.id,
        language: options.input.language,
      }
    );
    if (!result.success) {
      return textResult(
        `Could not change cell ${options.input.cellNumber} language to "${options.input.language}". It may not be a registered language. Call verso_getLanguages to list valid languages.`
      );
    }

    notifyWebviewChanged(ctx);

    return textResult(
      `Changed cell ${options.input.cellNumber} language to "${options.input.language}". Changing the language clears its previous outputs.`
    );
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
    ),
    vscode.lm.registerTool(
      "verso_getCellProperties",
      new GetCellPropertiesTool()
    ),
    vscode.lm.registerTool(
      "verso_updateCellProperty",
      new UpdateCellPropertyTool()
    ),
    vscode.lm.registerTool("verso_listLayouts", new ListLayoutsTool()),
    vscode.lm.registerTool("verso_switchLayout", new SwitchLayoutTool()),
    vscode.lm.registerTool("verso_moveCell", new MoveCellTool()),
    vscode.lm.registerTool("verso_changeCellType", new ChangeCellTypeTool()),
    vscode.lm.registerTool(
      "verso_changeCellLanguage",
      new ChangeCellLanguageTool()
    )
  );
}
