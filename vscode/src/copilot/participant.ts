import * as vscode from "vscode";
import * as path from "path";
import { resolveNotebook, NotebookContext } from "./tools";
import { hostRegistry } from "../host/hostRegistry";
import {
  CellDto,
  ExecutionRunAllResult,
  LanguagesResult,
  VariableListResult,
} from "../host/protocol";

const PARTICIPANT_ID = "verso.copilot.notebook";

const BASE_SYSTEM_PROMPT = `You are a Verso notebook assistant integrated into GitHub Copilot Chat.
Verso is an interactive notebook environment for .NET, similar to Jupyter or Polyglot Notebooks.

You help users by creating, editing, running, and explaining notebook cells.

Cell types and languages:
- Cell TYPE determines the kind of cell (e.g. code, markdown, html, mermaid).
- Cell LANGUAGE determines the kernel for code cells (e.g. csharp, sql, python).
- For code cells, set type to "code" and specify the language.
- For non-code cells (markdown, html, mermaid, etc.), set type accordingly. No language is needed.

Parameters:
- Notebooks can define parameters: named, typed values that control notebook behavior.
- Supported parameter types: string, int, float, bool, date (yyyy-MM-dd), datetime (ISO 8601).
- Use verso_listParameters to see current parameters, and verso_addParameter / verso_updateParameter / verso_removeParameter to manage them.
- A parameters cell is automatically created when the first parameter is added.

Guidelines:
- Before modifying cells, call verso_listCells to understand the current notebook state.
- Cell numbers are 1-based (cell 1 is the first cell).
- After running cells, examine the output and help the user understand results or fix errors.`;

interface CellTypeInfo {
  id: string;
  displayName: string;
}

interface CellTypesResult {
  cellTypes: CellTypeInfo[];
}

/**
 * Build a system prompt enriched with the notebook's actual cell types
 * and languages, so the LLM always has the accurate list.
 */
async function buildSystemPrompt(
  ctx: NotebookContext
): Promise<string> {
  let cellTypesBlock = "";
  let languagesBlock = "";

  try {
    const [cellTypesResult, languagesResult] = await Promise.all([
      ctx.host.sendRequest<CellTypesResult>("notebook/getCellTypes", {
        notebookId: ctx.notebookId,
      }),
      ctx.host.sendRequest<LanguagesResult>("notebook/getLanguages", {
        notebookId: ctx.notebookId,
      }),
    ]);

    const typesList = cellTypesResult.cellTypes
      .map((t) => `"${t.id}" (${t.displayName})`)
      .join(", ");
    cellTypesBlock = `\n\nAvailable cell types in this notebook: ${typesList}`;

    const nonCodeTypes = cellTypesResult.cellTypes
      .filter((t) => t.id !== "code")
      .map((t) => `"${t.id}"`)
      .join(", ");
    if (nonCodeTypes) {
      cellTypesBlock += `\nNon-code types (${nonCodeTypes}) do not need a language.`;
    }

    const langsList = languagesResult.languages
      .map((l) => `"${l.id}" (${l.displayName})`)
      .join(", ");
    languagesBlock = `\nAvailable languages for code cells: ${langsList}`;
  } catch {
    // If the queries fail (e.g. host not ready), fall back to base prompt
  }

  return BASE_SYSTEM_PROMPT + cellTypesBlock + languagesBlock;
}

// ── Slash command handlers ──────────────────────────────────────────

async function handleCellsCommand(
  stream: vscode.ChatResponseStream,
  _token: vscode.CancellationToken
): Promise<vscode.ChatResult> {
  const ctx = await resolveNotebook();
  if (!ctx) {
    stream.markdown("No Verso notebook is currently open.");
    return {};
  }

  const result = await ctx.host.sendRequest<{ cells: CellDto[] }>(
    "cell/list",
    { notebookId: ctx.notebookId }
  );

  if (result.cells.length === 0) {
    stream.markdown("The notebook is empty.");
    return {};
  }

  stream.markdown(
    `**${path.basename(ctx.uri.fsPath)}** - ${result.cells.length} cell(s):\n\n`
  );

  for (let i = 0; i < result.cells.length; i++) {
    const cell = result.cells[i];
    const lang = cell.language ?? cell.type;
    stream.markdown(`**Cell ${i + 1}** [${lang}]\n`);
    stream.markdown(`\`\`\`${lang}\n${cell.source}\n\`\`\`\n\n`);
  }

  return {};
}

async function handleRunCommand(
  stream: vscode.ChatResponseStream,
  _token: vscode.CancellationToken
): Promise<vscode.ChatResult> {
  const ctx = await resolveNotebook();
  if (!ctx) {
    stream.markdown("No Verso notebook is currently open.");
    return {};
  }

  stream.progress("Running all cells...");
  const result = await ctx.host.sendRequest<ExecutionRunAllResult>(
    "execution/runAll",
    { notebookId: ctx.notebookId }
  );

  // Notify the webview to refresh cell outputs and mark dirty
  ctx.bridge.notify("output/update", { notebookId: ctx.notebookId });
  ctx.bridge.markDirty();

  for (const r of result.results) {
    const status = r.status === "completed" ? "completed" : `**${r.status}**`;
    stream.markdown(`Cell (${r.elapsedMs}ms): ${status}\n`);
    if (r.errorMessage) {
      stream.markdown(`> Error: ${r.errorMessage}\n`);
    }
  }

  return {};
}

async function handleVarsCommand(
  stream: vscode.ChatResponseStream,
  _token: vscode.CancellationToken
): Promise<vscode.ChatResult> {
  const ctx = await resolveNotebook();
  if (!ctx) {
    stream.markdown("No Verso notebook is currently open.");
    return {};
  }

  const result = await ctx.host.sendRequest<VariableListResult>(
    "variable/list",
    { notebookId: ctx.notebookId }
  );

  if (result.variables.length === 0) {
    stream.markdown("No variables in scope. Run some cells first.");
    return {};
  }

  stream.markdown("| Name | Type | Value |\n|---|---|---|\n");
  for (const v of result.variables) {
    stream.markdown(`| \`${v.name}\` | ${v.typeName} | ${v.valuePreview} |\n`);
  }

  return {};
}

// ── Main handler ────────────────────────────────────────────────────

const handler: vscode.ChatRequestHandler = async (
  request: vscode.ChatRequest,
  context: vscode.ChatContext,
  stream: vscode.ChatResponseStream,
  token: vscode.CancellationToken
): Promise<vscode.ChatResult> => {
  // Handle slash commands directly (no LLM needed)
  if (request.command === "cells") {
    return handleCellsCommand(stream, token);
  }
  if (request.command === "run") {
    return handleRunCommand(stream, token);
  }
  if (request.command === "vars") {
    return handleVarsCommand(stream, token);
  }

  // Check if any notebook is open
  if (hostRegistry.size === 0) {
    stream.markdown(
      "No Verso notebook is currently open. Open a `.verso`, `.ipynb`, or `.dib` file first."
    );
    return {};
  }

  // Resolve the active notebook and build an enriched system prompt
  const ctx = await resolveNotebook();
  if (!ctx) {
    stream.markdown("No Verso notebook is currently open.");
    return {};
  }

  const systemPrompt = await buildSystemPrompt(ctx);

  // Build the tool-calling loop
  const versoTools = vscode.lm.tools.filter((t) =>
    t.name.startsWith("verso_")
  );

  const messages: vscode.LanguageModelChatMessage[] = [
    vscode.LanguageModelChatMessage.User(systemPrompt),
  ];

  // Include relevant conversation history for multi-turn context
  for (const turn of context.history) {
    if (turn instanceof vscode.ChatRequestTurn) {
      messages.push(vscode.LanguageModelChatMessage.User(turn.prompt));
    } else if (turn instanceof vscode.ChatResponseTurn) {
      const text = turn.response
        .filter((part): part is vscode.ChatResponseMarkdownPart =>
          part instanceof vscode.ChatResponseMarkdownPart
        )
        .map((part) => part.value.value)
        .join("");
      if (text) {
        messages.push(vscode.LanguageModelChatMessage.Assistant(text));
      }
    }
  }

  // Add the current request
  messages.push(vscode.LanguageModelChatMessage.User(request.prompt));

  // Tool-calling loop
  let iterations = 0;
  const maxIterations = 10;

  while (iterations < maxIterations) {
    iterations++;

    let response: vscode.LanguageModelChatResponse;
    try {
      response = await request.model.sendRequest(
        messages,
        { tools: versoTools },
        token
      );
    } catch (err) {
      if (err instanceof vscode.LanguageModelError) {
        stream.markdown(`Model error: ${err.message}`);
      }
      return {};
    }

    const toolCalls: vscode.LanguageModelToolCallPart[] = [];
    const textParts: string[] = [];

    for await (const part of response.stream) {
      if (part instanceof vscode.LanguageModelTextPart) {
        stream.markdown(part.value);
        textParts.push(part.value);
      } else if (part instanceof vscode.LanguageModelToolCallPart) {
        toolCalls.push(part);
      }
    }

    // If no tool calls, the model is done
    if (toolCalls.length === 0) {
      break;
    }

    // Record the assistant's response (text + tool calls)
    const assistantParts: (
      | vscode.LanguageModelTextPart
      | vscode.LanguageModelToolCallPart
    )[] = [];
    if (textParts.length > 0) {
      assistantParts.push(
        new vscode.LanguageModelTextPart(textParts.join(""))
      );
    }
    assistantParts.push(...toolCalls);
    messages.push(
      vscode.LanguageModelChatMessage.Assistant(assistantParts)
    );

    // Execute the tool calls
    const toolResults: vscode.LanguageModelToolResultPart[] = [];
    for (const call of toolCalls) {
      try {
        const result = await vscode.lm.invokeTool(call.name, {
          input: call.input,
          toolInvocationToken: request.toolInvocationToken,
        }, token);
        toolResults.push(
          new vscode.LanguageModelToolResultPart(call.callId, result.content)
        );
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        toolResults.push(
          new vscode.LanguageModelToolResultPart(call.callId, [
            new vscode.LanguageModelTextPart(`Tool error: ${message}`),
          ])
        );
      }
    }

    messages.push(vscode.LanguageModelChatMessage.User(toolResults));
  }

  return {};
};

// ── Registration ────────────────────────────────────────────────────

export function registerParticipant(
  context: vscode.ExtensionContext
): void {
  const participant = vscode.chat.createChatParticipant(
    PARTICIPANT_ID,
    handler
  );

  participant.iconPath = vscode.Uri.joinPath(
    context.extensionUri,
    "icon.png"
  );

  context.subscriptions.push(participant);
}
