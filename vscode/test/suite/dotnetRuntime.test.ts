import * as assert from "assert";
import * as fs from "fs";
import * as os from "os";
import * as path from "path";
import * as vscode from "vscode";
import { hasRuntimeAtLeast, resolveDotnetCommand } from "../../src/host/dotnetRuntime";

const ACQUIRED_DOTNET_KEY = "verso.acquiredDotnetPath";

suite("dotnetRuntime: previously acquired runtime", () => {
  let root: string;

  setup(() => {
    root = fs.mkdtempSync(path.join(os.tmpdir(), "verso-dotnet-"));
  });

  teardown(() => {
    fs.rmSync(root, { recursive: true, force: true });
  });

  // Lays out a private .NET install the way the .NET Install Tool does: the
  // dotnet executable next to shared/Microsoft.NETCore.App/<version> folders.
  function fakeInstall(name: string, runtimeVersions: string[]): string {
    const dir = path.join(root, name);
    for (const version of runtimeVersions) {
      fs.mkdirSync(path.join(dir, "shared", "Microsoft.NETCore.App", version), { recursive: true });
    }
    fs.mkdirSync(dir, { recursive: true });
    const dotnet = path.join(dir, process.platform === "win32" ? "dotnet.exe" : "dotnet");
    fs.writeFileSync(dotnet, "");
    return dotnet;
  }

  function fakeHost(name: string, frameworkVersion: string): string {
    const dir = path.join(root, name);
    fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(
      path.join(dir, "Verso.Host.runtimeconfig.json"),
      JSON.stringify({
        runtimeOptions: { framework: { name: "Microsoft.NETCore.App", version: frameworkVersion } },
      })
    );
    return path.join(dir, "Verso.Host.dll");
  }

  function fakeContext(acquired: string | undefined): vscode.ExtensionContext {
    const state = new Map<string, unknown>();
    if (acquired) {
      state.set(ACQUIRED_DOTNET_KEY, acquired);
    }
    return {
      globalState: {
        get: (key: string) => state.get(key),
        update: async (key: string, value: unknown) => {
          if (value === undefined) {
            state.delete(key);
          } else {
            state.set(key, value);
          }
        },
      },
    } as unknown as vscode.ExtensionContext;
  }

  test("hasRuntimeAtLeast accepts the same or a later major", () => {
    assert.strictEqual(hasRuntimeAtLeast(fakeInstall("ten", ["10.0.3"]), "10.0"), true);
    assert.strictEqual(hasRuntimeAtLeast(fakeInstall("eleven", ["11.0.0"]), "10.0"), true);
  });

  test("hasRuntimeAtLeast rejects an older major or a missing shared folder", () => {
    assert.strictEqual(hasRuntimeAtLeast(fakeInstall("eight", ["8.0.16"]), "10.0"), false);
    assert.strictEqual(hasRuntimeAtLeast(fakeInstall("empty", []), "10.0"), false);
  });

  test("an acquired runtime that is too old is forgotten in favor of PATH", async () => {
    const dotnet = fakeInstall("old", ["8.0.16"]);
    const context = fakeContext(dotnet);

    const command = await resolveDotnetCommand(context, fakeHost("host-old", "10.0.0"));

    assert.strictEqual(command, "dotnet");
    assert.strictEqual(context.globalState.get(ACQUIRED_DOTNET_KEY), undefined);
  });

  test("an acquired runtime that is new enough is still used", async () => {
    const dotnet = fakeInstall("new", ["8.0.16", "10.0.3"]);
    const context = fakeContext(dotnet);

    const command = await resolveDotnetCommand(context, fakeHost("host-new", "10.0.0"));

    assert.strictEqual(command, dotnet);
    assert.strictEqual(context.globalState.get(ACQUIRED_DOTNET_KEY), dotnet);
  });
});
