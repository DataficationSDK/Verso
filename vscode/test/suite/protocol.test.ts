import * as assert from "assert";
import type {
  JsonRpcRequest,
  JsonRpcResponse,
  JsonRpcNotification,
  JsonRpcError,
  CellDto,
  ExecutionResultDto,
  NotebookOpenParams,
  NotebookOpenResult,
  CellAddParams,
  CellUpdateSourceParams,
  CompletionsParams,
  HoverParams,
  LayoutSwitchParams,
  ThemeSwitchParams,
  ExtensionToggleParams,
  SettingsUpdateParams,
  VariableInspectParams,
  ToolbarGetEnabledStatesParams,
  ExecutionRunParams,
} from "../../src/host/protocol";

suite("Protocol Message Types", () => {
  // ── JSON-RPC structure ─────────────────────────────────────────────

  test("Request has required fields", () => {
    const req: JsonRpcRequest = {
      jsonrpc: "2.0",
      id: 1,
      method: "notebook/open",
      params: { content: "{}" },
    };

    assert.strictEqual(req.jsonrpc, "2.0");
    assert.strictEqual(req.id, 1);
    assert.strictEqual(req.method, "notebook/open");
    assert.ok(req.params);
  });

  test("Response with result", () => {
    const res: JsonRpcResponse = {
      jsonrpc: "2.0",
      id: 1,
      result: { cells: [], title: "Test" },
    };

    assert.strictEqual(res.id, 1);
    assert.ok(res.result);
    assert.strictEqual(res.error, undefined);
  });

  test("Response with error", () => {
    const err: JsonRpcError = {
      code: -32600,
      message: "Invalid Request",
    };
    const res: JsonRpcResponse = {
      jsonrpc: "2.0",
      id: 2,
      error: err,
    };

    assert.strictEqual(res.id, 2);
    assert.ok(res.error);
    assert.strictEqual(res.error.code, -32600);
    assert.strictEqual(res.error.message, "Invalid Request");
  });

  test("Notification has no id", () => {
    const notif: JsonRpcNotification = {
      jsonrpc: "2.0",
      method: "cell/executionState",
      params: { cellId: "abc", state: "running" },
    };

    assert.strictEqual(notif.jsonrpc, "2.0");
    assert.strictEqual(notif.method, "cell/executionState");
    assert.ok(!("id" in notif));
  });

  // ── ID extraction ──────────────────────────────────────────────────

  test("Request ID is a number", () => {
    const req: JsonRpcRequest = { jsonrpc: "2.0", id: 42, method: "test" };
    assert.strictEqual(typeof req.id, "number");
  });

  test("Response ID matches request ID", () => {
    const reqId = 99;
    const req: JsonRpcRequest = { jsonrpc: "2.0", id: reqId, method: "test" };
    const res: JsonRpcResponse = { jsonrpc: "2.0", id: reqId, result: {} };
    assert.strictEqual(req.id, res.id);
  });

  // ── Method name validation ─────────────────────────────────────────

  test("Notebook methods follow namespace pattern", () => {
    const methods = [
      "notebook/open",
      "notebook/save",
      "notebook/setFilePath",
    ];
    for (const m of methods) {
      assert.ok(m.startsWith("notebook/"), `${m} should start with notebook/`);
    }
  });

  test("Cell methods follow namespace pattern", () => {
    const methods = [
      "cell/updateSource",
    ];
    for (const m of methods) {
      assert.ok(m.startsWith("cell/"), `${m} should start with cell/`);
    }
  });

  test("Execution methods follow namespace pattern", () => {
    const methods = [
      "execution/run",
      "execution/runAll",
    ];
    for (const m of methods) {
      assert.ok(m.startsWith("execution/"), `${m} should start with execution/`);
    }
  });

  test("Kernel methods follow namespace pattern", () => {
    const methods = [
      "kernel/getCompletions",
      "kernel/getDiagnostics",
      "kernel/getHoverInfo",
      "kernel/restart",
    ];
    for (const m of methods) {
      assert.ok(m.startsWith("kernel/"), `${m} should start with kernel/`);
    }
  });

  // ── DTO structure ──────────────────────────────────────────────────

  test("CellDto has required fields", () => {
    const cell: CellDto = {
      id: "abc123",
      type: "code",
      source: "Console.WriteLine();",
      outputs: [],
    };

    assert.ok(cell.id);
    assert.ok(cell.type);
    assert.strictEqual(typeof cell.source, "string");
    assert.ok(Array.isArray(cell.outputs));
  });

  test("ExecutionResultDto has timing info", () => {
    const result: ExecutionResultDto = {
      cellId: "abc",
      status: "completed",
      executionCount: 1,
      elapsedMs: 150,
      outputs: [],
    };

    assert.strictEqual(result.status, "completed");
    assert.strictEqual(result.elapsedMs, 150);
    assert.strictEqual(result.executionCount, 1);
  });

  test("NotebookOpenParams requires content", () => {
    const params: NotebookOpenParams = { content: '{"cells":[]}' };
    assert.strictEqual(typeof params.content, "string");
  });

  test("CellAddParams has type and source", () => {
    const params: CellAddParams = {
      type: "code",
      source: "var x = 1;",
      language: "csharp",
    };

    assert.strictEqual(params.type, "code");
    assert.strictEqual(params.language, "csharp");
  });

  test("CompletionsParams includes cursor position", () => {
    const params: CompletionsParams = {
      cellId: "cell-1",
      code: "Console.",
      cursorPosition: 8,
    };

    assert.strictEqual(params.cursorPosition, 8);
  });

  test("LayoutSwitchParams has layoutId", () => {
    const params: LayoutSwitchParams = { layoutId: "dashboard" };
    assert.strictEqual(params.layoutId, "dashboard");
  });

  test("ThemeSwitchParams has themeId", () => {
    const params: ThemeSwitchParams = { themeId: "dark" };
    assert.strictEqual(params.themeId, "dark");
  });

  test("ExtensionToggleParams has extensionId", () => {
    const params: ExtensionToggleParams = { extensionId: "ext.sql" };
    assert.strictEqual(params.extensionId, "ext.sql");
  });

  test("SettingsUpdateParams has all fields", () => {
    const params: SettingsUpdateParams = {
      extensionId: "ext.test",
      name: "timeout",
      value: 30,
    };

    assert.strictEqual(params.extensionId, "ext.test");
    assert.strictEqual(params.name, "timeout");
    assert.strictEqual(params.value, 30);
  });

  test("VariableInspectParams has name", () => {
    const params: VariableInspectParams = { name: "myVar" };
    assert.strictEqual(params.name, "myVar");
  });

  test("ToolbarGetEnabledStatesParams has placement and cell IDs", () => {
    const params: ToolbarGetEnabledStatesParams = {
      placement: "MainToolbar",
      selectedCellIds: ["cell-1", "cell-2"],
    };

    assert.strictEqual(params.placement, "MainToolbar");
    assert.strictEqual(params.selectedCellIds.length, 2);
  });
});
