import * as assert from "assert";
import * as vscode from "vscode";
import { BlazorBridge } from "../../src/blazor/blazorBridge";
import { HostProcess } from "../../src/host/hostProcess";

/**
 * A live output talks to the view drawing it in both directions, and the two directions take
 * different routes through this bridge. What the view says travels as an ordinary request and is
 * forwarded by the generic path; what the host says travels as a notification, which the bridge
 * only forwards for methods it was told about. These cover both, because the second one fails
 * silently when it is wrong.
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

/**
 * Records what the bridge subscribed to and lets a test raise one, standing in for the host
 * process writing a notification down its pipe.
 */
function createRecordingHost(): {
  host: HostProcess;
  raise: (method: string, params: unknown) => void;
  subscribed: string[];
  forwarded: Array<{ method: string; params: unknown }>;
} {
  const handlers = new Map<string, (params: unknown) => void>();
  const subscribed: string[] = [];
  const forwarded: Array<{ method: string; params: unknown }> = [];

  const host = {
    onNotification: (method: string, handler: (params: unknown) => void) => {
      subscribed.push(method);
      handlers.set(method, handler);
    },
    sendRequest: async (method: string, params: unknown) => {
      forwarded.push({ method, params });
      return null;
    },
  } as unknown as HostProcess;

  return {
    host,
    raise: (method, params) => handlers.get(method)?.(params),
    subscribed,
    forwarded,
  };
}

function notificationsTo(posted: unknown[], method: string): unknown[] {
  return posted.filter(
    (m) =>
      (m as { type?: string }).type === "jsonrpc-notification" &&
      (m as { method?: string }).method === method
  );
}

suite("BlazorBridge output channels", () => {
  test("a message for a view is forwarded to the webview", () => {
    const { webview, posted } = createFakeWebview();
    const recording = createRecordingHost();
    new BlazorBridge(webview, recording.host);

    assert.ok(
      recording.subscribed.includes("channel/post"),
      "channel/post must be subscribed or the host's half of every live output is dropped"
    );

    recording.raise("channel/post", {
      channelId: "chan-1",
      messageType: "ext/counter",
      payload: { value: 7 },
    });

    const sent = notificationsTo(posted, "channel/post");
    assert.strictEqual(sent.length, 1);
    assert.deepStrictEqual((sent[0] as { params: unknown }).params, {
      channelId: "chan-1",
      messageType: "ext/counter",
      payload: { value: 7 },
    });
  });

  test("a channel closing is forwarded to the webview", () => {
    const { webview, posted } = createFakeWebview();
    const recording = createRecordingHost();
    new BlazorBridge(webview, recording.host);

    assert.ok(
      recording.subscribed.includes("channel/closed"),
      "channel/closed must be subscribed or a view never learns its kernel let go of it"
    );

    recording.raise("channel/closed", {
      channelId: "chan-1",
      reason: "the cell was re-run",
    });

    const sent = notificationsTo(posted, "channel/closed");
    assert.strictEqual(sent.length, 1);
    assert.deepStrictEqual((sent[0] as { params: unknown }).params, {
      channelId: "chan-1",
      reason: "the cell was re-run",
    });
  });

  test("a view's message reaches the host and never dirties the document", async () => {
    const { webview, emit } = createFakeWebview();
    const recording = createRecordingHost();
    const bridge = new BlazorBridge(webview, recording.host);
    let edits = 0;
    bridge.onDidEdit = () => {
      edits++;
    };

    await emit({
      type: "jsonrpc-request",
      id: 1,
      method: "channel/ready",
      params: { channelId: "chan-1", protocolVersion: "1.0" },
    });
    await emit({
      type: "jsonrpc-request",
      id: 2,
      method: "channel/message",
      params: { channelId: "chan-1", messageType: "slider", payload: '{"value":3}' },
    });

    assert.deepStrictEqual(
      recording.forwarded.map((f) => f.method),
      ["channel/ready", "channel/message"],
      "both legs go through the generic forward; neither needs a case of its own here"
    );

    // Moving a slider changes what a kernel holds, not what the file holds. A widget
    // interaction that marked the tab edited would ask the user to save on every drag.
    assert.strictEqual(edits, 0);
  });
});
