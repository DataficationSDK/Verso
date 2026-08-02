import * as vscode from "vscode";
import * as fs from "fs";
import { log } from "../log";
import { HostStartError } from "./hostProcess";

/**
 * Resolves which `dotnet` command the host process is launched with, and turns a
 * failed host start into actionable guidance when the cause is a missing or
 * incompatible .NET runtime.
 *
 * The host is a framework-dependent .NET assembly launched as `dotnet
 * Verso.Host.dll`, so it needs the shared .NET runtime (not the SDK) on the
 * machine. Normal resolution stays deliberately simple and touches no other
 * extension: an explicit override, a runtime we previously acquired for the
 * user, or `dotnet` on PATH. The official ".NET Install Tool"
 * (ms-dotnettools.vscode-dotnet-runtime) is used ONLY when the user opts in from
 * the setup dialog. It is intentionally NOT an extension dependency: a hard
 * dependency would block Verso from activating at all on forks that lack it or
 * on offline/sideloaded installs, and calling its commands eagerly surfaces its
 * own error notifications we cannot suppress. A launch that fails surfaces the
 * actionable message below, which can acquire a runtime on demand.
 */

const INSTALL_TOOL_ID = "ms-dotnettools.vscode-dotnet-runtime";
const INSTALL_TOOL_ACQUIRE = "dotnet.acquire";
// globalState key holding the path to a runtime we acquired for the user. It is
// a private location off PATH, so we remember it to reuse across sessions.
const ACQUIRED_DOTNET_KEY = "verso.acquiredDotnetPath";
const DOTNET_DOWNLOAD_BASE = "https://dotnet.microsoft.com/download/dotnet";
const SETUP_DOCS_URL =
  "https://github.com/DataficationSDK/Verso/blob/main/vscode/README.md#getting-started";

// The lowest runtime the host is known to support; used only when the host's
// runtimeconfig.json cannot be read. The real requirement is read dynamically
// so it tracks the host's target framework across upgrades without edits here.
const DEFAULT_RUNTIME_VERSION = "8.0";

// A missing runtime otherwise prompts once per notebook opened, and VS Code
// restores several notebook tabs at once on startup. Debounce so a burst of
// failures shows one dialog, while a genuine reopen minutes later re-prompts
// (never permanently silent).
const SETUP_PROMPT_COOLDOWN_MS = 10_000;

type AcquireResult = { dotnetPath?: string } | undefined;

// One resolution per host path for the life of the session. Memoizing the
// promise (not just the value) also coalesces the concurrent first-open of
// several notebooks into a single resolution.
const resolutions = new Map<string, Promise<string>>();

let lastSetupPromptAt = 0;

/**
 * Returns the command used to launch the host: an explicit override, a runtime
 * located via the .NET Install Tool, or `dotnet` from PATH as a last resort.
 * Never rejects; a genuinely missing runtime surfaces later when the host fails
 * to start, via {@link showHostStartError}.
 */
export function resolveDotnetCommand(
  context: vscode.ExtensionContext,
  hostDllPath: string
): Promise<string> {
  let pending = resolutions.get(hostDllPath);
  if (!pending) {
    // The .catch guarantees the memo never stores a rejected promise, which
    // would otherwise poison every future notebook open in the session.
    pending = resolveDotnetCommandUncached(context, hostDllPath).catch((err) => {
      log.warn(`Resolving dotnet command failed, using PATH: ${describeError(err)}`);
      return "dotnet";
    });
    resolutions.set(hostDllPath, pending);
  }
  return pending;
}

/**
 * Drops any cached resolution for a host path so the next launch re-resolves.
 * Called after a failed start so a newly-installed runtime, an edited
 * `verso.dotnetPath`, or a reconnected network is picked up on reopen.
 */
export function invalidateDotnetResolution(hostDllPath: string): void {
  resolutions.delete(hostDllPath);
}

async function resolveDotnetCommandUncached(
  context: vscode.ExtensionContext,
  hostDllPath: string
): Promise<string> {
  // 1. An explicit user override always wins.
  const configured = vscode.workspace
    .getConfiguration("verso")
    .get<string>("dotnetPath")
    ?.trim();
  if (configured) {
    log.info(`Using verso.dotnetPath override: ${configured}`);
    return configured;
  }

  // 2. A runtime we previously acquired for the user lives at a private path off
  //    PATH; reuse it while it still exists. (Self-heals if it was removed: we
  //    fall through and the next failed start re-offers acquisition.)
  const acquired = context.globalState.get<string>(ACQUIRED_DOTNET_KEY);
  if (acquired && fs.existsSync(acquired)) {
    log.info(`Using previously acquired .NET runtime: ${acquired}`);
    return acquired;
  }

  // 3. Fall back to a system 'dotnet'. If absent, the host start surfaces the
  //    actionable "install .NET" guidance, which can acquire a runtime on demand.
  return "dotnet";
}

/**
 * Reads the required shared-runtime version (major.minor, e.g. "8.0") from the
 * host's runtimeconfig.json so acquisition and messaging track the host's target
 * framework automatically. Falls back to {@link DEFAULT_RUNTIME_VERSION} if
 * unreadable.
 */
function getRequiredRuntimeVersion(hostDllPath: string): string {
  try {
    const configPath = hostDllPath.replace(/\.dll$/i, ".runtimeconfig.json");
    const raw = fs.readFileSync(configPath, "utf8");
    const config = JSON.parse(raw);
    const options = config?.runtimeOptions;
    const version: string | undefined =
      options?.framework?.version ?? options?.frameworks?.[0]?.version;
    const match = version ? /^(\d+)\.(\d+)/.exec(version) : null;
    if (match) {
      return `${match[1]}.${match[2]}`;
    }
  } catch (err) {
    log.warn(
      `Could not read host runtimeconfig; defaulting required .NET to ` +
        `${DEFAULT_RUNTIME_VERSION}: ${describeError(err)}`
    );
  }
  return DEFAULT_RUNTIME_VERSION;
}

/**
 * Presents a failed host start to the user. When the cause is a missing or
 * incompatible .NET runtime, shows a tailored message offering to install the
 * runtime or open setup docs. Anything else keeps the generic message so
 * unrelated crashes are not mislabeled as a .NET problem.
 */
export async function showHostStartError(
  err: unknown,
  context: vscode.ExtensionContext,
  hostDllPath: string
): Promise<void> {
  const kind = err instanceof HostStartError ? err.kind : "crash";

  if (kind === "dotnet-not-found" || kind === "runtime-incompatible") {
    await showDotnetSetupError(context, hostDllPath);
    return;
  }

  vscode.window.showErrorMessage(
    vscode.l10n.t("Verso: Failed to start host process: {0}", describeError(err))
  );
}

async function showDotnetSetupError(
  context: vscode.ExtensionContext,
  hostDllPath: string
): Promise<void> {
  const now = Date.now();
  if (now - lastSetupPromptAt < SETUP_PROMPT_COOLDOWN_MS) {
    return;
  }
  lastSetupPromptAt = now;

  const version = getRequiredRuntimeVersion(hostDllPath);
  const message = vscode.l10n.t(
    "Verso needs the .NET runtime (version {0} or later) to run notebooks, but a compatible installation was not found.",
    version
  );

  const install = vscode.l10n.t({
    message: "Install .NET Runtime",
    comment: ["A button. .NET is a product name and stays as written."],
  });
  const help = vscode.l10n.t({
    message: "Setup Help",
    comment: ["A button. It opens the page describing how to set Verso up."],
  });
  const choice = await vscode.window.showErrorMessage(message, install, help);

  if (choice === install) {
    const installed = await attemptRuntimeAcquisition(context, hostDllPath);
    if (installed) {
      vscode.window.showInformationMessage(
        vscode.l10n.t(
          "Verso: the .NET runtime is installed. Reopen the notebook to continue."
        )
      );
    } else {
      // On-demand install did not complete (offline, blocked, or declined);
      // send the user to the manual download for the exact version needed.
      vscode.env.openExternal(vscode.Uri.parse(downloadUrlFor(version)));
    }
  } else if (choice === help) {
    vscode.env.openExternal(vscode.Uri.parse(SETUP_DOCS_URL));
  }
}

/**
 * Acquires a .NET runtime via the .NET Install Tool, installing that tool on
 * demand first if it is not present. Kept out of the eager resolution path so
 * Verso never triggers an unprompted download; it runs only when the user opts
 * in from the setup dialog. Returns true if a runtime was acquired, and caches
 * it as the resolution for the host so the next open uses it immediately.
 */
async function attemptRuntimeAcquisition(
  context: vscode.ExtensionContext,
  hostDllPath: string
): Promise<boolean> {
  const commands = await vscode.commands.getCommands(true);
  if (!commands.includes(INSTALL_TOOL_ACQUIRE)) {
    try {
      await vscode.commands.executeCommand(
        "workbench.extensions.installExtension",
        INSTALL_TOOL_ID
      );
    } catch (err) {
      log.warn(`Could not install the .NET Install Tool: ${describeError(err)}`);
      return false;
    }
  }

  try {
    const acquired = await vscode.window.withProgress<AcquireResult>(
      {
        location: vscode.ProgressLocation.Notification,
        title: vscode.l10n.t("Verso: installing the .NET runtime..."),
      },
      () =>
        vscode.commands.executeCommand<AcquireResult>(INSTALL_TOOL_ACQUIRE, {
          version: getRequiredRuntimeVersion(hostDllPath),
          requestingExtensionId: context.extension.id,
          mode: "runtime",
          architecture: dotnetArchitecture(),
        })
    );
    if (acquired?.dotnetPath) {
      log.info(`Acquired .NET runtime for host: ${acquired.dotnetPath}`);
      // Persist so future sessions reuse it (it is off PATH) and invalidate the
      // in-memory resolution so the next open picks it up immediately.
      await context.globalState.update(ACQUIRED_DOTNET_KEY, acquired.dotnetPath);
      resolutions.delete(hostDllPath);
      return true;
    }
  } catch (err) {
    log.warn(`dotnet.acquire failed: ${describeError(err)}`);
  }
  return false;
}

// Maps Node's process.arch to the architecture identifiers the .NET Install Tool
// expects, which it requires (not just defaults) on some request shapes.
function dotnetArchitecture(): string {
  switch (process.arch) {
    case "x64":
      return "x64";
    case "arm64":
      return "arm64";
    case "arm":
      return "arm";
    case "ia32":
      return "x86";
    default:
      return process.arch;
  }
}

function downloadUrlFor(version: string): string {
  return `${DOTNET_DOWNLOAD_BASE}/${version}`;
}

function describeError(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}
