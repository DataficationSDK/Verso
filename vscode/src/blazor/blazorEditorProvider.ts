import * as vscode from "vscode";
import * as path from "path";
import * as fs from "fs";
import { HostProcess } from "../host/hostProcess";
import { hostRegistry } from "../host/hostRegistry";
import { notebookRegistry } from "../host/notebookRegistry";
import { BlazorBridge } from "./blazorBridge";
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

    // Spawn a dedicated host process for this notebook
    const host = new HostProcess(this.hostDllPath);
    this.hosts.set(webviewPanel, host);

    try {
      await host.start();
    } catch (err) {
      vscode.window.showErrorMessage(
        `Verso: Failed to start host process: ${
          err instanceof Error ? err.message : err
        }`
      );
      return;
    }

    // Create bridge for this webview
    const bridge = new BlazorBridge(webview, host, this.context.globalState);
    bridge.setDocumentUri(document.uri);

    // Mark the document dirty whenever the WASM app mutates the notebook.
    bridge.onDidEdit = () => {
      this._onDidChangeCustomDocument.fire({ document });
    };

    this.bridges.set(webviewPanel, bridge);

    webviewPanel.onDidDispose(() => {
      const b = this.bridges.get(webviewPanel);
      b?.dispose();
      this.bridges.delete(webviewPanel);

      notebookRegistry.unregister(document.uri);
      hostRegistry.unregister(document.uri);

      // Dispose the per-notebook host process
      const h = this.hosts.get(webviewPanel);
      h?.dispose();
      this.hosts.delete(webviewPanel);
    });

    // Open the notebook in the host process and notify the webview
    try {
      const fileContent = await vscode.workspace.fs.readFile(document.uri);
      const content = new TextDecoder().decode(fileContent);
      const filePath = document.uri.fsPath;
      const workingDir = path.dirname(filePath);

      // Open the notebook via the host (must happen before setFilePath)
      const result = await host.sendRequest<NotebookOpenResult>(
        "notebook/open",
        { content, filePath, workingDir }
      );

      const notebookId = result.notebookId;
      notebookRegistry.register(document.uri, notebookId);
      hostRegistry.register(document.uri, { host, bridge });
      bridge.setNotebookId(notebookId);

      // If the notebook has no cells (new/empty file), register a default
      // code cell with the host so the WASM app has something to render.
      if (result.cells.length === 0) {
        const added = await host.sendRequest<CellDto>("cell/add", {
          type: "code",
          language: "csharp",
          source: "",
          notebookId,
        } as CellAddParams & { notebookId: string });
        result.cells.push(added);
      }

      // Set file path on the host (requires notebook to be open)
      await host.sendRequest("notebook/setFilePath", {
        filePath,
        notebookId,
      } satisfies NotebookSetFilePathParams & { notebookId: string });

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

  // --- Save / Revert ---

  private getHostForDocument(uri: vscode.Uri): HostProcess | undefined {
    for (const [panel, host] of this.hosts) {
      const bridge = this.bridges.get(panel);
      if (bridge && notebookRegistry.getByUri(uri) !== undefined) {
        return host;
      }
    }
    return undefined;
  }

  async saveCustomDocument(
    document: VersoDocument,
    _cancellation: vscode.CancellationToken
  ): Promise<void> {
    const notebookId = notebookRegistry.getByUri(document.uri);
    const host = this.getHostForDocument(document.uri);
    if (!host || !notebookId) return;

    // If the source file is not a .verso file (e.g. imported .dib or .ipynb),
    // redirect the save to a .verso file to avoid overwriting the original format.
    const fsPath = document.uri.fsPath;
    if (!fsPath.endsWith(".verso")) {
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

    const result = await host.sendRequest<NotebookSaveResult>(
      "notebook/save",
      { notebookId }
    );
    const data = new TextEncoder().encode(result.content);
    await vscode.workspace.fs.writeFile(document.uri, data);
  }

  async saveCustomDocumentAs(
    document: VersoDocument,
    destination: vscode.Uri,
    _cancellation: vscode.CancellationToken
  ): Promise<void> {
    const notebookId = notebookRegistry.getByUri(document.uri);
    const host = this.getHostForDocument(document.uri);
    if (!host || !notebookId) return;

    // If the destination is not a .verso file, adjust the extension
    let targetUri = destination;
    if (!destination.fsPath.endsWith(".verso")) {
      const versoPath = destination.fsPath.replace(/\.[^.]+$/, ".verso");
      targetUri = vscode.Uri.file(versoPath);
    }

    const result = await host.sendRequest<NotebookSaveResult>(
      "notebook/save",
      { notebookId }
    );
    const data = new TextEncoder().encode(result.content);
    await vscode.workspace.fs.writeFile(targetUri, data);
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
    const host = this.getHostForDocument(document.uri);
    if (!host || !notebookId) {
      return { id: context.destination.toString(), delete: () => {} };
    }
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

    // Shared component static files
    const appCss = toUri("_content/Verso.Blazor.Shared/app.css");
    const monacoInterop = toUri(
      "_content/Verso.Blazor.Shared/js/monaco-interop.js"
    );
    const dashboardInterop = toUri(
      "_content/Verso.Blazor.Shared/js/dashboard-interop.js"
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
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src ${cspSource} ${monacoCdn} https: 'unsafe-eval' 'wasm-unsafe-eval' 'unsafe-inline'; style-src ${cspSource} ${monacoCdn} https: 'unsafe-inline'; font-src ${cspSource} ${monacoCdn} https:; img-src ${cspSource} https: data:; connect-src ${cspSource} ${monacoCdn} https: data:; worker-src ${cspSource} ${monacoCdn} blob:;" />
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
            --verso-cell-error-foreground: var(--vscode-errorForeground, #F48771);
            --verso-cell-running-indicator: var(--vscode-progressBar-background, #0078D4);
            --verso-toolbar-background: var(--vscode-editorGroupHeader-tabsBackground, var(--vscode-editor-background));
            --verso-toolbar-foreground: var(--vscode-foreground);
            --verso-toolbar-button-hover: var(--vscode-toolbar-hoverBackground);
            --verso-toolbar-separator: var(--vscode-panel-border, #E0E0E0);
            --verso-toolbar-disabled-foreground: var(--vscode-disabledForeground);
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
    <script src="${panelResizeInterop}"></script>
    <script src="${fileDownloadInterop}"></script>
    <script src="${mermaidInterop}"></script>
    <script src="${cellInteractInterop}"></script>
    <script src="${parametersInterop}"></script>
    <script src="${userPrefsInterop}"></script>
    <script src="${cellDragInterop}"></script>
    <script src="${tagInputInterop}"></script>
    <script src="${frameworkJs}" autostart="false"></script>
    <script>
    // Manually start Blazor with error handling.
    // The real webview resource base (used by loadBootResource to remap framework fetches).
    var wasmBase = '${webview.asWebviewUri(wasmRoot)}/';
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
                    return wasmBase + '_framework/' + name + '?v=' + wasmVersion;
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
