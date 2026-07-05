import * as vscode from "vscode";
import * as path from "path";
import * as fs from "fs";
import * as os from "os";
import { HostProcess } from "../host/hostProcess";
import {
  resolveDotnetCommand,
  showHostStartError,
  invalidateDotnetResolution,
} from "../host/dotnetRuntime";
import { hostRegistry } from "../host/hostRegistry";
import { notebookRegistry } from "../host/notebookRegistry";
import { BlazorBridge } from "./blazorBridge";
import { log } from "../log";
import {
  CellAddParams,
  CellDto,
  NotebookCloseParams,
  NotebookOpenResult,
  NotebookSaveResult,
  NotebookSetFilePathParams,
} from "../host/protocol";

/**
 * A minimal CustomDocument that tracks its URI so the provider can
 * read / write the backing .verso file.
 */
class VersoDocument implements vscode.CustomDocument {
  constructor(public readonly uri: vscode.Uri) {}
  dispose(): void {}
}

/**
 * CustomEditorProvider that hosts the Blazor WASM app in a VS Code webview.
 * The webview loads the published WASM output and communicates with the
 * Verso.Host process through the BlazorBridge.
 *
 * Implements the full editable custom-editor contract so VS Code tracks
 * dirty state and routes Cmd/Ctrl+S through {@link saveCustomDocument}.
 */
export class BlazorEditorProvider
  implements vscode.CustomEditorProvider<VersoDocument>
{
  public static readonly viewType = "verso.blazorNotebook";

  private readonly bridges = new Map<vscode.WebviewPanel, BlazorBridge>();
  private readonly hosts = new Map<vscode.WebviewPanel, HostProcess>();
  private readonly documents = new Map<vscode.WebviewPanel, VersoDocument>();
  /** Per-panel mutex serializing host restarts so concurrent clicks coalesce. */
  private readonly restartLocks = new Map<vscode.WebviewPanel, Promise<void>>();

  /**
   * `uri.toString()` of scratch notebooks created via the "New Notebook"
   * command that have not yet been kept (saved to a real location). The backing
   * temp file is deleted when such a document is closed or promoted.
   */
  private readonly scratchUris = new Set<string>();
  private scratchCounter = 0;

  // --- Edit tracking ---
  private readonly _onDidChangeCustomDocument =
    new vscode.EventEmitter<vscode.CustomDocumentContentChangeEvent<VersoDocument>>();
  readonly onDidChangeCustomDocument = this._onDidChangeCustomDocument.event;

  constructor(
    private readonly context: vscode.ExtensionContext,
    private readonly hostDllPath: string
  ) {
    // Watch for VS Code editor setting changes and push to all open webviews
    context.subscriptions.push(
      vscode.workspace.onDidChangeConfiguration((e) => {
        if (
          e.affectsConfiguration("editor.fontFamily") ||
          e.affectsConfiguration("editor.fontSize") ||
          e.affectsConfiguration("editor.fontLigatures")
        ) {
          const settings = BlazorEditorProvider.getEditorSettings();
          for (const [, bridge] of this.bridges) {
            bridge.postEditorSettings(settings);
          }
        }
      })
    );

    // Sync Monaco editor theme when the VS Code color theme changes
    context.subscriptions.push(
      vscode.window.onDidChangeActiveColorTheme((theme) => {
        const kind =
          theme.kind === vscode.ColorThemeKind.Dark ||
          theme.kind === vscode.ColorThemeKind.HighContrast
            ? "dark"
            : "light";
        for (const [, bridge] of this.bridges) {
          bridge.postThemeKind(kind);
        }
      })
    );
  }

  /**
   * Reads the user's VS Code editor font settings and prepends
   * ligature-capable fonts so ligatures work out of the box.
   */
  private static readonly ligatureFonts = "'Cascadia Code', 'Fira Code'";

  private static getEditorSettings(): {
    fontSize: number;
    fontFamily: string;
    fontLigatures: boolean | string;
  } {
    const config = vscode.workspace.getConfiguration("editor");
    const vscodeFontFamily = config.get<string>("fontFamily", "monospace");
    const fontFamily = `${BlazorEditorProvider.ligatureFonts}, ${vscodeFontFamily}`;
    return {
      fontSize: config.get<number>("fontSize", 14),
      fontFamily,
      fontLigatures: config.get<boolean | string>("fontLigatures", true),
    };
  }

  // --- CustomEditorProvider lifecycle ---

  /**
   * Creates a blank scratch notebook and opens it, backing the command-palette
   * "Verso: New Notebook" entry. The notebook is written to a temp file so it
   * can flow through the normal custom-editor open path; it is tracked in
   * {@link scratchUris} so it is deleted on close unless the user keeps it via
   * Save (which promotes it to a real location — see {@link promoteScratch}).
   */
  async createScratchNotebook(): Promise<void> {
    const dir = path.join(os.tmpdir(), "verso-scratch");
    try {
      fs.mkdirSync(dir, { recursive: true });
    } catch (err) {
      vscode.window.showErrorMessage(
        `Verso: Could not create a scratch notebook: ${
          err instanceof Error ? err.message : err
        }`
      );
      return;
    }

    // Pick an Untitled-N name that doesn't collide with an existing scratch file.
    let tempPath: string;
    do {
      tempPath = path.join(dir, `Untitled-${++this.scratchCounter}.verso`);
    } while (fs.existsSync(tempPath));

    // Empty content is the already-supported new-notebook path: the host creates
    // a blank notebook and resolveCustomEditor seeds a default C# cell.
    try {
      fs.writeFileSync(tempPath, "");
    } catch (err) {
      vscode.window.showErrorMessage(
        `Verso: Could not create a scratch notebook: ${
          err instanceof Error ? err.message : err
        }`
      );
      return;
    }

    const uri = vscode.Uri.file(tempPath);
    this.scratchUris.add(uri.toString());
    await vscode.commands.executeCommand(
      "vscode.openWith",
      uri,
      BlazorEditorProvider.viewType
    );
  }

  async openCustomDocument(
    uri: vscode.Uri,
    _openContext: vscode.CustomDocumentOpenContext,
    _token: vscode.CancellationToken
  ): Promise<VersoDocument> {
    return new VersoDocument(uri);
  }

  async resolveCustomEditor(
    document: VersoDocument,
    webviewPanel: vscode.WebviewPanel,
    _token: vscode.CancellationToken
  ): Promise<void> {
    const webview = webviewPanel.webview;

    webview.options = {
      enableScripts: true,
      localResourceRoots: [this.getWasmRoot()],
    };

    // Set the webview HTML loading the WASM app
    webview.html = this.getWebviewHtml(webview);

    // The bundled host DLL could not be resolved at activation (a distinct,
    // already-diagnosed cause). Report that plainly instead of spawning a doomed
    // `dotnet ""` and mislabeling it as a runtime problem.
    if (!this.hostDllPath) {
      vscode.window.showErrorMessage(
        'Verso: Could not find Verso.Host.dll. Set "verso.hostPath" in settings to the path of your built Verso.Host.dll.'
      );
      return;
    }

    // Spawn a dedicated host process for this notebook
    const dotnetCommand = await resolveDotnetCommand(this.context, this.hostDllPath);
    const host = new HostProcess(this.hostDllPath, dotnetCommand);
    this.hosts.set(webviewPanel, host);

    try {
      await host.start();
    } catch (err) {
      // Re-resolve on the next open in case the user installs a runtime or edits
      // verso.dotnetPath. A classified failure (missing/incompatible .NET) shows
      // tailored guidance; anything else falls back to the generic message.
      invalidateDotnetResolution(this.hostDllPath);
      await showHostStartError(err, this.context, this.hostDllPath);
      return;
    }

    // Create bridge for this webview
    const bridge = new BlazorBridge(webview, host, this.context.globalState);
    bridge.setDocumentUri(document.uri);

    // Tell the webview when the host dies unexpectedly so its kernel status can
    // show "Disconnected" instead of a stale "Kernel idle". Provider-driven
    // restarts dispose the old host first, so they do not fire this.
    host.onUnexpectedExit = (detail) => {
      bridge.notify("host/exited", { detail });
    };

    // Mark the document dirty whenever the WASM app mutates the notebook.
    bridge.onDidEdit = () => {
      this._onDidChangeCustomDocument.fire({ document });
    };

    // Wire the kernel-restart entrypoint so the toolbar action and the
    // #!restart magic command both flow through restartHost.
    bridge.onRestartRequested = (kernelId) =>
      this.restartHost(webviewPanel, kernelId);

    this.bridges.set(webviewPanel, bridge);
    this.documents.set(webviewPanel, document);

    webviewPanel.onDidDispose(() => {
      const b = this.bridges.get(webviewPanel);
      b?.dispose();
      this.bridges.delete(webviewPanel);
      this.documents.delete(webviewPanel);
      this.restartLocks.delete(webviewPanel);

      notebookRegistry.unregister(document.uri);
      hostRegistry.unregister(document.uri);

      // Dispose the per-notebook host process
      const h = this.hosts.get(webviewPanel);
      h?.dispose();
      this.hosts.delete(webviewPanel);

      // An un-promoted scratch notebook was closed without being kept. Dispose
      // fires after VS Code's save prompt resolves, so reaching here with the
      // URI still tracked means the user discarded it — delete the temp file.
      // (Keeping it via Save promotes it first, removing it from the set.)
      this.deleteScratch(document.uri);
    });

    // Open the notebook in the host process and notify the webview
    try {
      const fileContent = await vscode.workspace.fs.readFile(document.uri);
      const content = new TextDecoder().decode(fileContent);
      const filePath = document.uri.fsPath;
      const workingDir = path.dirname(filePath);

      const result = await this.openNotebookInHost(host, content, filePath, workingDir, {
        addDefaultCellIfEmpty: true,
      });

      const notebookId = result.notebookId;
      notebookRegistry.register(document.uri, notebookId);
      hostRegistry.register(document.uri, { host, bridge });
      bridge.setNotebookId(notebookId);

      // Notify the WASM app that the notebook is ready
      bridge.notify("notebook/opened", { filePath, ...result });
    } catch (err) {
      vscode.window.showErrorMessage(
        `Verso: Failed to open notebook: ${
          err instanceof Error ? err.message : err
        }`
      );
    }
  }

  /**
   * Sends notebook/open to the host, optionally seeds a default cell when the
   * resulting notebook is empty, and finally sets the file path. Used both for
   * the initial editor open and for the restart flow where the in-memory
   * snapshot replaces the on-disk content.
   */
  private async openNotebookInHost(
    host: HostProcess,
    content: string,
    filePath: string,
    workingDir: string,
    options: { addDefaultCellIfEmpty: boolean }
  ): Promise<NotebookOpenResult> {
    // verso.extensionsPath accepts either a single string (legacy) or an array of paths.
    const rawExtensionsPath = vscode.workspace
      .getConfiguration("verso")
      .get<string | string[]>("extensionsPath");

    let extensionsDirectory: string | undefined;
    let extensionsDirectories: string[] | undefined;
    if (Array.isArray(rawExtensionsPath)) {
      const dirs = rawExtensionsPath.filter(
        (d) => typeof d === "string" && d.trim().length > 0
      );
      extensionsDirectories = dirs.length > 0 ? dirs : undefined;
    } else if (typeof rawExtensionsPath === "string" && rawExtensionsPath.trim().length > 0) {
      extensionsDirectory = rawExtensionsPath;
    }

    const result = await host.sendRequest<NotebookOpenResult>(
      "notebook/open",
      { content, filePath, workingDir, extensionsDirectory, extensionsDirectories }
    );

    const notebookId = result.notebookId;

    // On first open of a brand-new file, seed a default code cell so the WASM
    // app renders something. On restart we skip this because the snapshot
    // already mirrors what the webview is showing — adding a cell here would
    // desynchronize the two.
    if (options.addDefaultCellIfEmpty && result.cells.length === 0) {
      const added = await host.sendRequest<CellDto>("cell/add", {
        type: "code",
        language: "csharp",
        source: "",
        notebookId,
      } as CellAddParams & { notebookId: string });
      result.cells.push(added);
    }

    await host.sendRequest("notebook/setFilePath", {
      filePath,
      notebookId,
    } satisfies NotebookSetFilePathParams & { notebookId: string });

    return result;
  }

  /**
   * Kills the panel's Verso.Host process and spawns a fresh one, transplanting
   * the in-memory notebook into the new process via a verso-format snapshot
   * (which preserves cell GUIDs across the Jupyter and verso serializers alike).
   *
   * Concurrent calls coalesce on the per-panel mutex so spam-clicking Restart
   * Kernel produces exactly one restart at a time.
   */
  private async restartHost(
    panel: vscode.WebviewPanel,
    kernelId: string | undefined
  ): Promise<void> {
    const inFlight = this.restartLocks.get(panel);
    if (inFlight !== undefined) {
      log.info("Restart already in progress for panel; awaiting existing");
      return inFlight;
    }

    const promise = this.restartHostInner(panel, kernelId);
    this.restartLocks.set(panel, promise);
    try {
      await promise;
    } finally {
      this.restartLocks.delete(panel);
    }
  }

  private async restartHostInner(
    panel: vscode.WebviewPanel,
    kernelId: string | undefined
  ): Promise<void> {
    const oldHost = this.hosts.get(panel);
    const bridge = this.bridges.get(panel);
    const document = this.documents.get(panel);
    if (!oldHost || !bridge || !document) {
      log.warn("restartHost: missing host, bridge, or document for panel");
      return;
    }

    log.info(`Kernel restart requested (kernelId=${kernelId ?? "default"})`);

    bridge.beginRestart();
    bridge.notifyRestarting(kernelId);

    let snapshot = "";
    try {
      const saveResult = await oldHost.sendRequest<NotebookSaveResult>(
        "notebook/save",
        { notebookId: bridge.getNotebookId() }
      );
      snapshot = saveResult.content;
      log.info(`Captured notebook snapshot (${snapshot.length} bytes)`);
    } catch (err) {
      log.error(
        `Failed to capture notebook snapshot: ${
          err instanceof Error ? err.message : String(err)
        }`
      );
      bridge.endRestart();
      bridge.notifyFaulted(
        `Restart aborted: the notebook snapshot could not be captured (${
          err instanceof Error ? err.message : String(err)
        }).`
      );
      vscode.window.showErrorMessage(
        "Verso: kernel restart aborted because the notebook snapshot could not be captured. Save and reopen the file."
      );
      return;
    }

    log.info("Disposing old host");
    oldHost.dispose();
    this.hosts.delete(panel);

    log.info("Spawning new host");
    const dotnetCommand = await resolveDotnetCommand(this.context, this.hostDllPath);
    const newHost = new HostProcess(this.hostDllPath, dotnetCommand);
    try {
      await newHost.start();
    } catch (err) {
      log.error(
        `New host failed to start: ${err instanceof Error ? err.message : String(err)}`
      );
      invalidateDotnetResolution(this.hostDllPath);
      bridge.endRestart();
      bridge.notifyFaulted(
        `Kernel restart failed: the host process did not start (${
          err instanceof Error ? err.message : String(err)
        }). Close and reopen the notebook.`
      );
      // Route through the shared handler so a missing/incompatible .NET runtime
      // gets the same actionable "Install .NET Runtime" guidance as a fresh open.
      await showHostStartError(err, this.context, this.hostDllPath);
      return;
    }
    this.hosts.set(panel, newHost);

    const filePath = document.uri.fsPath;
    const workingDir = path.dirname(filePath);
    let result: NotebookOpenResult;
    try {
      result = await this.openNotebookInHost(newHost, snapshot, filePath, workingDir, {
        addDefaultCellIfEmpty: false,
      });
    } catch (err) {
      log.error(
        `New host failed to reopen notebook: ${
          err instanceof Error ? err.message : String(err)
        }`
      );
      bridge.endRestart();
      bridge.notifyFaulted(
        `Kernel restart failed: the notebook did not reopen (${
          err instanceof Error ? err.message : String(err)
        }).`
      );
      vscode.window.showErrorMessage(
        `Verso: kernel restart failed (notebook did not reopen): ${
          err instanceof Error ? err.message : String(err)
        }. Close and reopen the notebook.`
      );
      return;
    }

    bridge.setHost(newHost);
    bridge.setNotebookId(result.notebookId);
    newHost.onUnexpectedExit = (detail) => {
      bridge.notify("host/exited", { detail });
    };
    notebookRegistry.register(document.uri, result.notebookId);
    hostRegistry.register(document.uri, { host: newHost, bridge });

    bridge.endRestart();
    bridge.notifyRestarted(kernelId);
    log.info(`Kernel restart complete (notebookId=${result.notebookId}, ${result.cells.length} cells)`);
  }

  // --- Save / Revert ---

  // Extensions Verso can write back in their original format. Anything not listed
  // here falls through to the convert-to-.verso path even when the
  // verso.preserveOriginalFormat setting is on.
  private static readonly preservableFormats: Record<string, string> = {
    ".ipynb": "jupyter",
  };

  async saveCustomDocument(
    document: VersoDocument,
    _cancellation: vscode.CancellationToken
  ): Promise<void> {
    // A scratch notebook lives in a temp dir, so a plain save would silently
    // write back there. Instead prompt for a real location and promote it.
    if (this.scratchUris.has(document.uri.toString())) {
      await this.promoteScratch(document);
      return;
    }

    const notebookId = notebookRegistry.getByUri(document.uri);
    const session = hostRegistry.getByUri(document.uri);
    if (!session || !notebookId) return;
    const host = session.host;

    const fsPath = document.uri.fsPath;
    const ext = path.extname(fsPath).toLowerCase();
    const preserveSetting = vscode.workspace.getConfiguration("verso")
      .get<boolean>("preserveOriginalFormat", false);
    const preservableFormat = BlazorEditorProvider.preservableFormats[ext];
    const shouldPreserve = ext !== ".verso" && preserveSetting && preservableFormat !== undefined;

    // Source isn't .verso and the user hasn't opted in to preservation (or the
    // format isn't writable back): redirect to a sibling .verso file.
    if (ext !== ".verso" && !shouldPreserve) {
      const versoPath = fsPath.replace(/\.[^.]+$/, ".verso");
      const versoUri = vscode.Uri.file(versoPath);

      const result = await host.sendRequest<NotebookSaveResult>(
        "notebook/save",
        { notebookId }
      );
      const data = new TextEncoder().encode(result.content);
      await vscode.workspace.fs.writeFile(versoUri, data);

      // Update the host's file path to the new .verso location
      await host.sendRequest("notebook/setFilePath", {
        filePath: versoUri.fsPath,
        notebookId,
      });

      // Open the new .verso file and close the imported document
      await vscode.commands.executeCommand("vscode.openWith", versoUri, BlazorEditorProvider.viewType);
      await vscode.commands.executeCommand("workbench.action.closeActiveEditor");
      return;
    }

    const format = ext === ".verso" ? "verso" : preservableFormat;
    const result = await host.sendRequest<NotebookSaveResult>(
      "notebook/save",
      { notebookId, format }
    );
    const data = new TextEncoder().encode(result.content);
    await vscode.workspace.fs.writeFile(document.uri, data);

    // Let the webview clear its unsaved-changes indicator; this fires for both the
    // webview Save button (which routes through workbench save) and Cmd/Ctrl+S.
    session.bridge.notify("document/saved");
  }

  async saveCustomDocumentAs(
    document: VersoDocument,
    destination: vscode.Uri,
    _cancellation: vscode.CancellationToken
  ): Promise<void> {
    const notebookId = notebookRegistry.getByUri(document.uri);
    const session = hostRegistry.getByUri(document.uri);
    if (!session || !notebookId) return;
    const host = session.host;

    const destExt = path.extname(destination.fsPath).toLowerCase();
    const preserveSetting = vscode.workspace.getConfiguration("verso")
      .get<boolean>("preserveOriginalFormat", false);
    const preservableFormat = BlazorEditorProvider.preservableFormats[destExt];
    const shouldPreserve = destExt !== ".verso" && preserveSetting && preservableFormat !== undefined;

    let targetUri = destination;
    let format = "verso";
    if (destExt === ".verso") {
      format = "verso";
    } else if (shouldPreserve) {
      format = preservableFormat;
    } else {
      // Destination isn't writable in its requested format: coerce to .verso.
      const versoPath = destination.fsPath.replace(/\.[^.]+$/, ".verso");
      targetUri = vscode.Uri.file(versoPath);
    }

    const result = await host.sendRequest<NotebookSaveResult>(
      "notebook/save",
      { notebookId, format }
    );
    const data = new TextEncoder().encode(result.content);
    await vscode.workspace.fs.writeFile(targetUri, data);

    session.bridge.notify("document/saved");

    // Save As on a scratch keeps it at the chosen location: rebind the editor to
    // the real file (once this save completes) so later saves write in place.
    if (this.scratchUris.has(document.uri.toString())) {
      this.deleteScratch(document.uri);
      this.scheduleScratchSwap(document.uri, targetUri);
    }
  }

  async revertCustomDocument(
    _document: VersoDocument,
    _cancellation: vscode.CancellationToken
  ): Promise<void> {
    // Full revert would require re-opening the notebook in the host.
    // For now this is a no-op; the user can close and re-open the file.
  }

  async backupCustomDocument(
    document: VersoDocument,
    context: vscode.CustomDocumentBackupContext,
    _cancellation: vscode.CancellationToken
  ): Promise<vscode.CustomDocumentBackup> {
    const notebookId = notebookRegistry.getByUri(document.uri);
    const session = hostRegistry.getByUri(document.uri);
    if (!session || !notebookId) {
      return { id: context.destination.toString(), delete: () => {} };
    }
    const host = session.host;
    const result = await host.sendRequest<NotebookSaveResult>(
      "notebook/save",
      { notebookId }
    );
    const data = new TextEncoder().encode(result.content);
    await vscode.workspace.fs.writeFile(context.destination, data);
    return { id: context.destination.toString(), delete: () => {} };
  }

  // --- Private helpers ---

  /**
   * Saves a scratch notebook to a user-chosen location, then deletes the temp
   * file and reopens the editor on the real file. If the user cancels the save
   * dialog the document stays a scratch (still dirty, still cleaned on close).
   */
  private async promoteScratch(document: VersoDocument): Promise<void> {
    const notebookId = notebookRegistry.getByUri(document.uri);
    const session = hostRegistry.getByUri(document.uri);
    if (!session || !notebookId) return;

    const defaultDir =
      vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? os.homedir();
    const target = await vscode.window.showSaveDialog({
      defaultUri: vscode.Uri.file(path.join(defaultDir, "Untitled.verso")),
      filters: { "Verso Notebook": ["verso"] },
    });
    if (!target) return;

    const result = await session.host.sendRequest<NotebookSaveResult>(
      "notebook/save",
      { notebookId, format: "verso" }
    );
    const data = new TextEncoder().encode(result.content);
    await vscode.workspace.fs.writeFile(target, data);

    this.deleteScratch(document.uri);

    // Swap the editor over to the kept file once this save completes (deferred —
    // see scheduleScratchSwap). The reopened editor is a normal saved .verso, so
    // subsequent saves write in place with no further prompt.
    this.scheduleScratchSwap(document.uri, target);
  }

  /**
   * Drops a scratch notebook from tracking and deletes its temp file
   * best-effort. No-op if the URI was never a scratch.
   */
  private deleteScratch(uri: vscode.Uri): void {
    if (!this.scratchUris.delete(uri.toString())) return;
    fs.promises.rm(uri.fsPath, { force: true }).catch(() => {
      /* best-effort; the temp file may already be gone */
    });
  }

  /**
   * Replaces the scratch editor with one bound to the kept file. Deferred to a
   * macrotask so it runs *after* VS Code finishes the in-progress save and marks
   * the scratch document clean — otherwise closing it would raise a save prompt.
   * The scratch tab is closed by its URI (not "active editor"), since opening the
   * kept file first makes that the active one.
   */
  private scheduleScratchSwap(scratchUri: vscode.Uri, target: vscode.Uri): void {
    setTimeout(async () => {
      try {
        await vscode.commands.executeCommand(
          "vscode.openWith",
          target,
          BlazorEditorProvider.viewType
        );
        await this.closeEditorFor(scratchUri);
      } catch (err) {
        log.warn(
          `Scratch promote swap failed: ${
            err instanceof Error ? err.message : String(err)
          }`
        );
      }
    }, 0);
  }

  /** Closes the custom-editor tab backed by the given URI, if one is open. */
  private async closeEditorFor(uri: vscode.Uri): Promise<void> {
    const key = uri.toString();
    for (const group of vscode.window.tabGroups.all) {
      for (const tab of group.tabs) {
        const input = tab.input;
        if (
          input instanceof vscode.TabInputCustom &&
          input.viewType === BlazorEditorProvider.viewType &&
          input.uri.toString() === key
        ) {
          await vscode.window.tabGroups.close(tab);
          return;
        }
      }
    }
  }

  /**
   * Returns the URI to the blazor-wasm static files directory.
   */
  private getWasmRoot(): vscode.Uri {
    return vscode.Uri.joinPath(this.context.extensionUri, "blazor-wasm", "wwwroot");
  }

  /**
   * Returns a cache-buster string for webview resource URLs.
   * Published builds use the extension version. Local dev builds append
   * the WASM root's mtime so every recompile forces a fresh fetch.
   */
  private getCacheBuster(): string {
    const version: string = this.context.extension.packageJSON.version ?? "0";
    try {
      const wasmDir = path.join(this.context.extensionPath, "blazor-wasm", "wwwroot");
      const stat = fs.statSync(wasmDir);
      return `${version}-${stat.mtimeMs.toFixed(0)}`;
    } catch {
      return version;
    }
  }

  /**
   * Generates the webview HTML that loads the Blazor WASM app.
   */
  private getWebviewHtml(webview: vscode.Webview): string {
    const wasmRoot = this.getWasmRoot();
    const version = this.getCacheBuster();

    const toUri = (relativePath: string) =>
      webview.asWebviewUri(vscode.Uri.joinPath(wasmRoot, relativePath)).toString() + `?v=${version}`;

    // Core WASM framework files
    const frameworkJs = toUri("_framework/blazor.webassembly.js");
    const frameworkBase = webview.asWebviewUri(
      vscode.Uri.joinPath(wasmRoot, "_framework")
    ).toString();

    // Shared component static files
    const appCss = toUri("_content/Verso.Blazor.Shared/app.css");
    const monacoInterop = toUri(
      "_content/Verso.Blazor.Shared/js/monaco-interop.js"
    );
    const dashboardInterop = toUri(
      "_content/Verso.Blazor.Shared/js/dashboard-interop.js"
    );
    const customLayoutInterop = toUri(
      "_content/Verso.Blazor.Shared/js/custom-layout-interop.js"
    );
    const versoLayoutAssets = toUri(
      "_content/Verso.Blazor.Shared/js/verso-layout-assets.js"
    );
    const versoLayoutFrame = toUri(
      "_content/Verso.Blazor.Shared/js/verso-layout-frame.js"
    );
    const layoutInteractInterop = toUri(
      "_content/Verso.Blazor.Shared/js/layout-interact-interop.js"
    );
    const versoInlineLayoutBridge = toUri(
      "_content/Verso.Blazor.Shared/js/verso-inline-layout-bridge.js"
    );
    const panelResizeInterop = toUri(
      "_content/Verso.Blazor.Shared/js/panel-resize-interop.js"
    );
    const fileDownloadInterop = toUri(
      "_content/Verso.Blazor.Shared/js/file-download-interop.js"
    );
    const mermaidInterop = toUri(
      "_content/Verso.Blazor.Shared/js/mermaid-interop.js"
    );
    const userPrefsInterop = toUri(
      "_content/Verso.Blazor.Shared/js/user-prefs-interop.js"
    );
    const cellDragInterop = toUri(
      "_content/Verso.Blazor.Shared/js/cell-drag-interop.js"
    );
    const cellInteractInterop = toUri(
      "_content/Verso.Blazor.Shared/js/cell-interact-interop.js"
    );
    const parametersInterop = toUri(
      "_content/Verso.Blazor.Shared/js/parameters-interop.js"
    );
    const tagInputInterop = toUri(
      "_content/Verso.Blazor.Shared/js/tag-input-interop.js"
    );

    // WASM-specific files
    const vscodeBridgeJs = toUri("js/vscode-bridge.js");

    // Content Security Policy: allow scripts/styles from the webview origin,
    // the Monaco editor CDN, and any HTTPS source so HTML cells can load
    // external libraries (charting, visualization, etc.).
    const cspSource = webview.cspSource;
    const monacoCdn = "https://cdn.jsdelivr.net";

    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src ${cspSource} ${monacoCdn} https: blob: 'unsafe-eval' 'wasm-unsafe-eval' 'unsafe-inline'; style-src ${cspSource} ${monacoCdn} https: blob: 'unsafe-inline'; font-src ${cspSource} ${monacoCdn} https:; img-src ${cspSource} https: data:; connect-src ${cspSource} ${monacoCdn} https: data:; worker-src ${cspSource} ${monacoCdn} blob:;" />
    <base id="blazor-base" href="/" />
    <script>
    // Set base href to match the webview origin so Blazor's NavigationManager
    // sees location.href as contained within the base URI.
    // The webview origin (vscode-webview://[id]) is parseable by .NET's Uri class.
    document.getElementById('blazor-base').href = location.origin + '/';
    </script>
    <link rel="stylesheet" href="${appCss}" />
    <style>
        html, body, #app { height: 100%; margin: 0; padding: 0; }
        /* Let cell-type dropdown paint above Monaco in webview:
           1. overflow:visible prevents the popup from being clipped
           2. position:relative + z-index on the visible toolbar creates a stacking
              context so the popup paints above the Monaco editor (which comes
              later in DOM order and would otherwise paint on top). */
        .verso-cell-content { overflow: visible !important; }
        .verso-cell-toolbar {
            position: relative;
            z-index: 10;
        }
        /* Contain Monaco's internal z-index values so they don't leak
           into the parent stacking context and paint over the toolbar popup */
        .verso-cell-editor { position: relative; z-index: 1; }
        /* Elevate the selected cell so Monaco's suggest widget paints above
           sibling cells below it in DOM order */
        .verso-cell--selected { position: relative; z-index: 2; }
        /* Map VS Code theme to Verso CSS variables */
        :root {
            --verso-editor-background: var(--vscode-editor-background);
            --verso-editor-foreground: var(--vscode-editor-foreground);
            --verso-editor-line-number: var(--vscode-editorLineNumber-foreground, #858585);
            --verso-editor-cursor: var(--vscode-editorCursor-foreground);
            --verso-editor-selection: var(--vscode-editor-selectionBackground);
            --verso-editor-gutter: var(--vscode-editorGutter-background, var(--vscode-editor-background));
            --verso-cell-background: var(--vscode-editor-background);
            --verso-cell-border: var(--vscode-panel-border, #E0E0E0);
            --verso-cell-active-border: var(--vscode-focusBorder, #0078D4);
            --verso-cell-hover-background: var(--vscode-list-hoverBackground);
            --verso-cell-output-background: var(--vscode-textBlockQuote-background, #F5F5F5);
            --verso-cell-output-foreground: var(--vscode-editor-foreground);
            --verso-cell-error-background: var(--vscode-inputValidation-errorBackground, #5A1D1D);
            --verso-cell-error-foreground: var(--vscode-inputValidation-errorForeground, var(--vscode-errorForeground, #F48771));
            --verso-cell-running-indicator: var(--vscode-progressBar-background, #0078D4);
            /* Toolbar and the right panel-strip rail share this token. Use the workbench side
               panel background (Explorer / Source Control) so Verso's chrome frames the notebook
               canvas like a native VS Code panel, falling back to the tab header then editor bg. */
            --verso-toolbar-background: var(--vscode-sideBar-background, var(--vscode-editorGroupHeader-tabsBackground, var(--vscode-editor-background)));
            --verso-toolbar-foreground: var(--vscode-foreground);
            --verso-toolbar-button-hover: var(--vscode-toolbar-hoverBackground);
            --verso-toolbar-separator: var(--vscode-panel-border, #E0E0E0);
            --verso-toolbar-disabled-foreground: var(--vscode-disabledForeground);
            /* Filled Stop button. statusBarItem error colors are a designed bg/fg pair,
               unlike errorForeground (a text color that can wash out as a button fill). */
            --verso-toolbar-stop-background: var(--vscode-statusBarItem-errorBackground, #B52E31);
            --verso-toolbar-stop-foreground: var(--vscode-statusBarItem-errorForeground, #FFFFFF);
            --verso-sidebar-background: var(--vscode-sideBar-background);
            --verso-sidebar-foreground: var(--vscode-sideBar-foreground, var(--vscode-foreground));
            --verso-sidebar-item-hover: var(--vscode-list-hoverBackground);
            --verso-sidebar-item-active: var(--vscode-list-activeSelectionBackground);
            --verso-border-default: var(--vscode-panel-border, #E0E0E0);
            --verso-border-focused: var(--vscode-focusBorder, #0078D4);
            --verso-accent-primary: var(--vscode-focusBorder, #0078D4);
            --verso-accent-secondary: var(--vscode-button-background, #0078D4);
            --verso-highlight-background: var(--vscode-editor-findMatchHighlightBackground, #EA5C0055);
            --verso-highlight-foreground: var(--vscode-editor-foreground);
            --verso-status-success: var(--vscode-testing-iconPassed, #73C991);
            --verso-status-warning: var(--vscode-editorWarning-foreground, #CCA700);
            --verso-status-error: var(--vscode-errorForeground, #F48771);
            --verso-status-info: var(--vscode-editorInfo-foreground, #3794FF);
            --verso-scrollbar-thumb: var(--vscode-scrollbarSlider-background);
            --verso-scrollbar-track: transparent;
            --verso-scrollbar-thumb-hover: var(--vscode-scrollbarSlider-hoverBackground);
            --verso-dropdown-background: var(--vscode-dropdown-background);
            --verso-dropdown-hover: var(--vscode-list-hoverBackground);
            --verso-ui-font-family: var(--vscode-font-family, 'Segoe UI', sans-serif);
            --verso-ui-font-size: var(--vscode-font-size, 13px);
            --verso-accent: var(--vscode-focusBorder, #0078D4);
            --verso-border: var(--vscode-panel-border, #E0E0E0);
            --verso-border-light: var(--vscode-editorWidget-border, #F0F0F0);
            --verso-input-border: var(--vscode-input-border, #CCC);
            --verso-input-background: var(--vscode-input-background, #FFF);
            --verso-input-foreground: var(--vscode-input-foreground, #1E1E1E);
            --verso-badge-background: var(--vscode-badge-background, #E8E8E8);
            --verso-badge-foreground: var(--vscode-badge-foreground, #555);
            --verso-error: var(--vscode-errorForeground, #F48771);
            --verso-success: var(--vscode-testing-iconPassed, #73C991);
            --verso-foreground-muted: var(--vscode-disabledForeground, #999);
            --verso-hover-background: var(--vscode-list-hoverBackground, #F5F5F5);
            /* Coarse semantic palette consumed by custom layouts. Mapped from the editor
               surfaces so layouts that derive their look from these tokens blend into the
               active VS Code theme. */
            --verso-bg-default: var(--vscode-editor-background);
            --verso-bg-elevated: var(--vscode-editorGroupHeader-tabsBackground, var(--vscode-editor-background));
            --verso-fg-default: var(--vscode-editor-foreground);
            --verso-fg-muted: var(--vscode-disabledForeground, var(--vscode-editorLineNumber-foreground, #858585));
            --verso-font-family-mono: var(--vscode-editor-font-family, 'Cascadia Mono', Consolas, monospace);
        }
    </style>
</head>
<body>
    <div id="app">
        <div id="loading" style="display:flex;align-items:center;justify-content:center;height:100vh;font-family:var(--vscode-font-family,sans-serif);color:var(--vscode-foreground,#ccc);">
            <div style="text-align:center;">
                <div style="border:2px solid var(--vscode-foreground,#ccc);border-top-color:transparent;border-radius:50%;width:24px;height:24px;animation:spin 0.8s linear infinite;margin:0 auto 12px;"></div>
                <div id="loading-status">Loading Verso...</div>
            </div>
        </div>
    </div>
    <style>@keyframes spin { to { transform: rotate(360deg); } }</style>

    <script>
    // Error reporting — display errors in the loading screen
    function showError(msg) {
        var el = document.getElementById('loading-status');
        if (el) el.innerHTML += '<br/><span style="color:#f44;font-size:12px;word-break:break-all;">' + msg + '</span>';
    }
    window.addEventListener('error', function(e) {
        showError('Error: ' + (e.message || e) + (e.filename ? ' (' + e.filename + ':' + e.lineno + ')' : ''));
    });
    window.addEventListener('unhandledrejection', function(e) {
        showError('Rejection: ' + (e.reason && e.reason.message ? e.reason.message : e.reason));
    });
    </script>

    <script src="${vscodeBridgeJs}"></script>
    <script>window.__versoEditorSettings = ${JSON.stringify(BlazorEditorProvider.getEditorSettings())};</script>
    <script src="${monacoCdn}/npm/monaco-editor@0.45.0/min/vs/loader.js"></script>
    <script src="${monacoInterop}"></script>
    <script src="${dashboardInterop}"></script>
    <script src="${customLayoutInterop}"></script>
    <script src="${versoLayoutAssets}"></script>
    <script src="${versoLayoutFrame}"></script>
    <script src="${panelResizeInterop}"></script>
    <script src="${fileDownloadInterop}"></script>
    <script src="${mermaidInterop}"></script>
    <script src="${cellInteractInterop}"></script>
    <script src="${layoutInteractInterop}"></script>
    <script src="${versoInlineLayoutBridge}"></script>
    <script src="${parametersInterop}"></script>
    <script src="${userPrefsInterop}"></script>
    <script src="${cellDragInterop}"></script>
    <script src="${tagInputInterop}"></script>
    <script src="${frameworkJs}" autostart="false"></script>
    <script>
    // Manually start Blazor with error handling.
    // The real webview resource base (used by loadBootResource to remap framework fetches).
    var frameworkBase = '${frameworkBase}/';
    var wasmVersion = '${version}';
    document.addEventListener('DOMContentLoaded', function() {
        if (typeof Blazor !== 'undefined') {
            var status = document.getElementById('loading-status');
            if (status) status.textContent = 'Starting Blazor runtime...';
            Blazor.start({
                loadBootResource: function(type, name, defaultUri, integrity) {
                    // Remap all framework resource URIs to real webview URIs
                    // since <base href> is a synthetic localhost URI.
                    // Append version query param to bust stale caches on extension update.
                    return frameworkBase + name + '?v=' + wasmVersion;
                }
            }).then(function() {
                if (status) status.textContent = 'Blazor started.';
            }).catch(function(err) {
                showError('Blazor.start() failed: ' + (err.message || err));
            });
        } else {
            showError('Blazor global not found — blazor.webassembly.js may not have loaded.');
        }
    });
    </script>
</body>
</html>`;
  }
}
