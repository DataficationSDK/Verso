import * as assert from "assert";
import * as vscode from "vscode";
import { hostRegistry } from "../../src/host/hostRegistry";
import { notebookRegistry } from "../../src/host/notebookRegistry";

suite("Host Registry — URI-based lookup (issue #37)", () => {
  const uriA = vscode.Uri.file("/tmp/notebookA.verso");
  const uriB = vscode.Uri.file("/tmp/notebookB.verso");

  // Minimal stubs — only the shape needed for registry operations
  const fakeHostA = { id: "hostA" } as any;
  const fakeHostB = { id: "hostB" } as any;
  const fakeBridgeA = { id: "bridgeA" } as any;
  const fakeBridgeB = { id: "bridgeB" } as any;

  setup(() => {
    // Clean slate
    hostRegistry.unregister(uriA);
    hostRegistry.unregister(uriB);
    notebookRegistry.unregister(uriA);
    notebookRegistry.unregister(uriB);
  });

  test("getByUri returns the correct session for each URI", () => {
    hostRegistry.register(uriA, { host: fakeHostA, bridge: fakeBridgeA });
    hostRegistry.register(uriB, { host: fakeHostB, bridge: fakeBridgeB });

    const sessionA = hostRegistry.getByUri(uriA);
    const sessionB = hostRegistry.getByUri(uriB);

    assert.strictEqual(sessionA?.host, fakeHostA, "URI A should resolve to host A");
    assert.strictEqual(sessionB?.host, fakeHostB, "URI B should resolve to host B");
  });

  test("getByUri does not return another document's session", () => {
    hostRegistry.register(uriA, { host: fakeHostA, bridge: fakeBridgeA });

    const sessionB = hostRegistry.getByUri(uriB);
    assert.strictEqual(sessionB, undefined, "Unregistered URI should return undefined");
  });

  test("notebookRegistry returns correct ID per URI", () => {
    notebookRegistry.register(uriA, "nb-111");
    notebookRegistry.register(uriB, "nb-222");

    assert.strictEqual(notebookRegistry.getByUri(uriA), "nb-111");
    assert.strictEqual(notebookRegistry.getByUri(uriB), "nb-222");
  });

  test("two-document save scenario resolves distinct hosts", () => {
    // Simulate two notebooks open simultaneously
    hostRegistry.register(uriA, { host: fakeHostA, bridge: fakeBridgeA });
    hostRegistry.register(uriB, { host: fakeHostB, bridge: fakeBridgeB });
    notebookRegistry.register(uriA, "nb-111");
    notebookRegistry.register(uriB, "nb-222");

    // Saving doc B must get host B and notebook ID "nb-222"
    const session = hostRegistry.getByUri(uriB);
    const notebookId = notebookRegistry.getByUri(uriB);

    assert.strictEqual(session?.host, fakeHostB,
      "Saving doc B must resolve to host B, not the first-registered host");
    assert.strictEqual(notebookId, "nb-222",
      "Saving doc B must use notebook ID nb-222");
  });
});
