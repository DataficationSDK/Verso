import * as assert from "assert";
import * as vscode from "vscode";
import { BlazorBridge } from "../../src/blazor/blazorBridge";
import { HostProcess } from "../../src/host/hostProcess";
import { GitBaselineProvider } from "../../src/git/gitBaselineProvider";

/**
 * Test doubles: the bridge only needs a message channel (webview) and a notification
 * registry (host), so both are minimal fakes. No host process is spawned.
 */
function createFakeWebview(): {
  webview: vscode.Webview;
  emit: (msg: unknown) => Promise<void>;
  posted: unknown[];
} {
  let handler: ((msg: unknown) => void | Promise<void>) | undefined;
  const posted: unknown[] = [];
  const webview = {
    onDidReceiveMessage: (cb: (msg: unknown) => void) => {
      handler = cb;
      return { dispose() {} };
    },
    postMessage: (msg: unknown) => {
      posted.push(msg);
      return Promise.resolve(true);
    },
    asWebviewUri: (uri: vscode.Uri) => uri,
    options: {},
    html: "",
    cspSource: "",
  } as unknown as vscode.Webview;

  return {
    webview,
    emit: async (msg) => {
      await handler?.(msg);
    },
    posted,
  };
}

function createFakeHost(): HostProcess {
  return {
    onNotification: (_method: string, _handler: unknown) => {},
    sendRequest: async (_method: string, _params: unknown) => {
      throw new Error("Unexpected forward to host in this test");
    },
  } as unknown as HostProcess;
}

function lastResponse(posted: unknown[]): {
  result?: unknown;
  error?: { message: string };
} {
  const responses = posted.filter(
    (m) => (m as { type?: string }).type === "jsonrpc-response"
  );
  assert.ok(responses.length > 0, "expected a jsonrpc-response to be posted");
  return responses[responses.length - 1] as {
    result?: unknown;
    error?: { message: string };
  };
}

type DiffSource = {
  id: string;
  label: string;
  kind: string;
  available: boolean;
};

suite("BlazorBridge diff endpoints", () => {
  test("diff/sources with no document URI: only the file source is available", async () => {
    const { webview, emit, posted } = createFakeWebview();
    new BlazorBridge(webview, createFakeHost());

    await emit({ type: "jsonrpc-request", id: 1, method: "diff/sources" });

    const { result } = lastResponse(posted);
    const sources = (result as { sources: DiffSource[] }).sources;
    assert.strictEqual(sources.length, 4);
    const byId = new Map(sources.map((s) => [s.id, s]));
    assert.strictEqual(byId.get("lastSaved")?.available, false);
    assert.strictEqual(byId.get("gitHead")?.available, false);
    assert.strictEqual(byId.get("gitRef")?.available, false);
    assert.strictEqual(byId.get("file")?.available, true);
  });

  test("diff/sources and diff/baseline never mark the document dirty", async () => {
    const { webview, emit } = createFakeWebview();
    const bridge = new BlazorBridge(webview, createFakeHost());
    let edits = 0;
    bridge.onDidEdit = () => {
      edits++;
    };

    await emit({ type: "jsonrpc-request", id: 1, method: "diff/sources" });
    await emit({
      type: "jsonrpc-request",
      id: 2,
      method: "diff/baseline",
      params: { sourceId: "unknown-source" },
    });

    assert.strictEqual(
      edits,
      0,
      "read-only diff endpoints must not fire onDidEdit; comparing must never dirty the notebook"
    );
  });

  test("diff/baseline lastSaved without a document URI reports an error", async () => {
    const { webview, emit, posted } = createFakeWebview();
    new BlazorBridge(webview, createFakeHost());

    await emit({
      type: "jsonrpc-request",
      id: 3,
      method: "diff/baseline",
      params: { sourceId: "lastSaved" },
    });

    const { error } = lastResponse(posted);
    assert.ok(error, "expected an error response");
    assert.ok(
      error.message.includes("no file on disk"),
      `unexpected message: ${error.message}`
    );
  });

  test("diff/baseline with an unknown source id reports an error", async () => {
    const { webview, emit, posted } = createFakeWebview();
    new BlazorBridge(webview, createFakeHost());

    await emit({
      type: "jsonrpc-request",
      id: 4,
      method: "diff/baseline",
      params: { sourceId: "nonsense" },
    });

    const { error } = lastResponse(posted);
    assert.ok(error, "expected an error response");
    assert.ok(error.message.includes("Unknown comparison source"));
  });

  test("diff/baseline lastSaved reads the document bytes from disk", async () => {
    const { webview, emit, posted } = createFakeWebview();
    const bridge = new BlazorBridge(webview, createFakeHost());

    const tempFile = vscode.Uri.joinPath(
      vscode.Uri.file(require("os").tmpdir()),
      `verso-bridge-diff-${Date.now()}.verso`
    );
    const payload = '{"verso":"1.1","cells":[]}';
    await vscode.workspace.fs.writeFile(
      tempFile,
      new TextEncoder().encode(payload)
    );
    try {
      bridge.setDocumentUri(tempFile);

      await emit({
        type: "jsonrpc-request",
        id: 5,
        method: "diff/baseline",
        params: { sourceId: "lastSaved" },
      });

      const { result } = lastResponse(posted);
      const baseline = result as {
        content: string;
        filePath?: string;
        label: string;
      };
      assert.strictEqual(baseline.content, payload);
      assert.strictEqual(baseline.label, "Last Saved");
      assert.strictEqual(baseline.filePath, tempFile.fsPath);
    } finally {
      await vscode.workspace.fs.delete(tempFile);
    }
  });
});

suite("GitBaselineProvider", () => {
  test("unactivated provider reports unavailable and empty refs", () => {
    const provider = new GitBaselineProvider();
    const uri = vscode.Uri.file("/nonexistent/notebook.verso");

    assert.strictEqual(provider.isAvailableFor(uri), false);
    assert.strictEqual(provider.isAvailableFor(undefined), false);
    assert.deepStrictEqual(provider.listRefsForQuickPick(uri), []);
  });

  test("activate never throws, even when the git extension is unusable", async () => {
    const provider = new GitBaselineProvider();
    await provider.activate();
    await provider.activate(); // idempotent second call

    // Whatever the test host's git state, a path outside any repo stays unavailable.
    assert.strictEqual(
      provider.isAvailableFor(vscode.Uri.file("/nonexistent/notebook.verso")),
      false
    );
  });

  test("showAtRef outside a repository reports a friendly error", async () => {
    const provider = new GitBaselineProvider();
    await provider.activate();

    await assert.rejects(
      provider.showAtRef(vscode.Uri.file("/nonexistent/notebook.verso"), "HEAD"),
      /not inside a git repository/
    );
  });
});
