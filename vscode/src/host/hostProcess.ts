import { ChildProcess, spawn } from "child_process";
import { createInterface, Interface as ReadlineInterface } from "readline";
import * as vscode from "vscode";
import {
  JsonRpcRequest,
  JsonRpcResponse,
  JsonRpcNotification,
} from "./protocol";
import { log } from "../log";
import { resolveLanguage } from "../localization";

type NotificationHandler = (params: unknown) => void;

/**
 * Why the host process never reached its ready signal. Lets callers show
 * tailored guidance (notably "install .NET") instead of a raw error string.
 */
export type HostStartFailureKind =
  | "dotnet-not-found"
  | "runtime-incompatible"
  | "timeout"
  | "crash";

/** A classified failure to start the host, thrown from {@link HostProcess.start}. */
export class HostStartError extends Error {
  constructor(
    readonly kind: HostStartFailureKind,
    message: string
  ) {
    super(message);
    this.name = "HostStartError";
  }
}

// Best-effort match against the .NET host's own "framework not found" diagnostic
// text, which it prints to stderr before exiting when no compatible shared
// runtime is installed. Wording has drifted across releases, so this stays broad
// and a miss only costs us a generic message rather than the tailored one.
const RUNTIME_MISSING_PATTERN =
  /You must install or update \.NET|It was not possible to find any compatible framework version|no frameworks were found|Microsoft\.NETCore\.App[^\n]*\bnot\b[^\n]*\bfound\b/i;

/**
 * The environment the host process runs with. Settings that select a tool the host
 * launches in turn are passed this way rather than through the notebook protocol,
 * because the host reads them where the tool is discovered rather than per notebook.
 * Changing one therefore applies on the next host start, matching how the extension
 * documents these settings.
 */
function buildHostEnvironment(): NodeJS.ProcessEnv {
  const env: NodeJS.ProcessEnv = { ...process.env };

  const python = vscode.workspace.getConfiguration("verso.python");

  const interpreterPath = python.get<string>("interpreterPath")?.trim();
  if (interpreterPath) {
    log.info(`Using verso.python.interpreterPath override: ${interpreterPath}`);
    env.VERSO_PYTHON = interpreterPath;
  }

  const autoInstall = python.get<string>("autoInstall")?.trim();
  if (autoInstall) {
    env.VERSO_PYTHON_AUTO_INSTALL = autoInstall;
  }

  // Only the non-default is sent, so an unset value leaves the kernel on its own default.
  if (python.get<boolean>("useUv") === false) {
    env.VERSO_PYTHON_UV = "off";
  }

  const widgetAssets = vscode.workspace
    .getConfiguration("verso.widgets")
    .get<string>("assetSource")
    ?.trim();
  if (widgetAssets) {
    env.VERSO_WIDGETS_ASSET_SOURCE = widgetAssets;
  }

  return env;
}

/**
 * The command line the host is started with. The language travels here rather than in the
 * environment because the environment is inherited by the tools the host launches in turn,
 * and a Python interpreter has no business being told what language the notebook chrome is in.
 */
function buildHostArguments(hostDllPath: string): string[] {
  const args = [hostDllPath];

  const language = resolveLanguage();
  if (language) {
    args.push("--language", language);
  }

  return args;
}

export class HostProcess implements vscode.Disposable {
  private process: ChildProcess | undefined;
  private readline: ReadlineInterface | undefined;
  private nextId = 1;
  private readonly pending = new Map<
    number,
    {
      resolve: (value: unknown) => void;
      reject: (error: Error) => void;
    }
  >();
  private readonly notificationHandlers = new Map<string, NotificationHandler>();
  private readyPromise: Promise<void> | undefined;
  private disposed = false;
  /** Recent stderr lines, kept so a startup failure can be classified. */
  private readonly stderrTail: string[] = [];

  /**
   * Fired when the process exits without dispose() having been called (a crash or
   * external kill, not a normal shutdown or provider-driven restart). Receives a
   * short human-readable exit description.
   */
  onUnexpectedExit: ((detail: string) => void) | undefined;

  constructor(
    private readonly hostDllPath: string,
    private readonly dotnetCommand: string = "dotnet"
  ) {}

  async start(): Promise<void> {
    if (this.process) {
      return;
    }

    this.readyPromise = new Promise<void>((resolve, reject) => {
      // Whichever of ready / spawn-error / early-exit / timeout happens first
      // decides the start() promise; the rest are ignored via this guard.
      // `ready` additionally records whether the host ever signalled ready, so a
      // later exit can be told apart from a startup failure that already settled.
      let settled = false;
      let ready = false;
      const settle = (finish: () => void) => {
        if (settled) {
          return;
        }
        settled = true;
        clearTimeout(timeout);
        finish();
      };

      const timeout = setTimeout(
        () =>
          settle(() =>
            reject(
              new HostStartError(
                "timeout",
                "Host did not send ready signal within 30s. This often means " +
                  "the .NET runtime is missing or incompatible."
              )
            )
          ),
        30000
      );

      const hostArgs = buildHostArguments(this.hostDllPath);
      log.info(`Spawning Verso.Host: ${this.dotnetCommand} ${hostArgs.join(" ")}`);
      this.process = spawn(this.dotnetCommand, hostArgs, {
        stdio: ["pipe", "pipe", "pipe"],
        env: buildHostEnvironment(),
      });

      this.process.on("error", (err) => {
        log.error(`Verso.Host failed to spawn: ${err.message}`);
        settle(() => reject(classifySpawnError(err)));
      });

      // Classify on `close`, not `exit`: `close` fires only after the stdio
      // streams are fully flushed, so the .NET "framework not found" text is
      // guaranteed present in stderrTail for classification. A spawn `error`
      // (e.g. ENOENT) does not reach here — it settles above — so its later
      // `close`, if any, is a no-op via the `settled`/`ready` guards.
      this.process.on("close", (code, signal) => {
        const detail = describeExit(code, signal);
        if (!settled) {
          // Never became ready: a startup failure, very often a missing or
          // too-old .NET runtime. Reject with a classified error so the caller
          // can offer actionable guidance, and skip the generic "exited"
          // warning that belongs to post-ready crashes.
          log.warn(`Verso.Host exited before ready (${detail.log})`);
          settle(() =>
            reject(classifyExit(detail.toast, this.stderrTail.join("\n")))
          );
          this.cleanup();
          return;
        }
        if (ready && !this.disposed) {
          log.warn(`Verso.Host exited unexpectedly (${detail.log})`);
          if (signal) {
            log.warn(
              `Host termination by signal indicates a native crash. ` +
                `On macOS, see ~/Library/Logs/DiagnosticReports/dotnet-*.ips ` +
                `for the faulting stack; on Linux, see your distro's coredump location.`
            );
          }
          // The detail is a signal name or an exit code, which reads the same
          // whatever language the sentence around it is in.
          vscode.window.showWarningMessage(
            vscode.l10n.t("Verso host process exited ({0})", detail.toast)
          );
          this.onUnexpectedExit?.(detail.toast);
        } else {
          log.info(`Verso.Host exited (${detail.log})`);
        }
        this.cleanup();
      });

      if (this.process.stderr) {
        this.process.stderr.on("data", (data: Buffer) => {
          const text = data.toString().trimEnd();
          if (text) {
            for (const line of text.split(/\r?\n/)) {
              // Keep a bounded tail so a startup failure can be classified
              // (e.g. the .NET "framework not found" message) after the fact.
              this.stderrTail.push(line);
              if (this.stderrTail.length > 50) {
                this.stderrTail.shift();
              }
              // The host writes JSON-RPC on stdout, so stderr carries everything
              // else — diagnostics, warnings, and uncaught errors. Lines tagged
              // with the `[Verso] ` prefix are intentional, structured logs from
              // the host; treat them as informational. Untagged stderr is most
              // likely an unhandled exception or runtime panic, so raise it as
              // a warning rather than the default `error` channel that fired on
              // every notebook open.
              if (line.startsWith("[Verso] ")) {
                log.info(`[Verso.Host] ${line.substring("[Verso] ".length)}`);
              } else {
                log.warn(`[Verso.Host] ${line}`);
              }
            }
          }
        });
      }

      if (this.process.stdout) {
        this.readline = createInterface({ input: this.process.stdout });
        this.readline.on("line", (line: string) => {
          this.handleLine(line, () =>
            settle(() => {
              ready = true;
              resolve();
            })
          );
        });
      }
    });

    return this.readyPromise;
  }

  onNotification(method: string, handler: NotificationHandler): void {
    this.notificationHandlers.set(method, handler);
  }

  async sendRequest<T>(method: string, params?: unknown): Promise<T> {
    if (!this.process?.stdin) {
      throw new Error("Host process is not running");
    }

    const id = this.nextId++;
    const request: JsonRpcRequest = {
      jsonrpc: "2.0",
      id,
      method,
      params,
    };

    return new Promise<T>((resolve, reject) => {
      this.pending.set(id, {
        resolve: resolve as (value: unknown) => void,
        reject,
      });

      const json = JSON.stringify(request);
      this.process!.stdin!.write(json + "\n", (err) => {
        if (err) {
          this.pending.delete(id);
          reject(err);
        }
      });
    });
  }

  private handleLine(line: string, onReady: () => void): void {
    if (!line.trim()) {
      return;
    }

    let msg: JsonRpcResponse | JsonRpcNotification;
    try {
      msg = JSON.parse(line);
    } catch {
      log.error(`Failed to parse host message: ${line}`);
      return;
    }

    // Check if it's a notification (no id)
    if (!("id" in msg)) {
      const notification = msg as JsonRpcNotification;
      if (notification.method === "host/ready") {
        onReady();
        return;
      }
      const handler = this.notificationHandlers.get(notification.method);
      if (handler) {
        handler(notification.params);
      }
      return;
    }

    // It's a response
    const response = msg as JsonRpcResponse;
    const pending = this.pending.get(response.id);
    if (!pending) {
      return;
    }
    this.pending.delete(response.id);

    if (response.error) {
      pending.reject(
        new Error(`${response.error.message} (code ${response.error.code})`)
      );
    } else {
      pending.resolve(response.result);
    }
  }

  private cleanup(): void {
    for (const [, { reject }] of this.pending) {
      reject(new Error("Host process exited"));
    }
    this.pending.clear();
    this.readline?.close();
    this.readline = undefined;
    this.process = undefined;
  }

  dispose(): void {
    this.disposed = true;
    if (this.process) {
      // Try graceful shutdown first
      try {
        const shutdownReq: JsonRpcRequest = {
          jsonrpc: "2.0",
          id: this.nextId++,
          method: "host/shutdown",
        };
        this.process.stdin?.write(JSON.stringify(shutdownReq) + "\n");
      } catch {
        // Ignore write errors during shutdown
      }

      // Force kill after brief delay
      const proc = this.process;
      setTimeout(() => {
        try {
          proc.kill();
        } catch {
          // Already exited
        }
      }, 1000);
    }
    this.cleanup();
  }
}

// A spawn `error` means the child never launched. ENOENT specifically means the
// `dotnet` command could not be found, i.e. no .NET on PATH; anything else is an
// unexpected launch failure.
function classifySpawnError(err: Error): HostStartError {
  if ((err as NodeJS.ErrnoException).code === "ENOENT") {
    return new HostStartError(
      "dotnet-not-found",
      "The .NET runtime ('dotnet') was not found."
    );
  }
  return new HostStartError(
    "crash",
    `Host process failed to spawn: ${err.message}`
  );
}

// The child launched but exited before signalling ready. If its stderr carries
// the .NET "framework not found" diagnostic, the installed runtime is too old or
// absent; otherwise treat it as an ordinary early crash.
function classifyExit(exitDetail: string, stderr: string): HostStartError {
  if (RUNTIME_MISSING_PATTERN.test(stderr)) {
    return new HostStartError(
      "runtime-incompatible",
      "A compatible .NET runtime was not found for the host."
    );
  }
  return new HostStartError(
    "crash",
    `Host process exited before ready (${exitDetail}).`
  );
}

// Node fires the `exit` event with `(code, signal)`. When a child is killed by
// a signal, `code` is `null` and `signal` carries the name ("SIGSEGV" etc.);
// when it returns from `main`, `signal` is `null` and `code` is the integer
// exit status. Surfacing both in the log makes it possible to tell a managed
// exit apart from a native crash without re-reading the source.
function describeExit(
  code: number | null,
  signal: NodeJS.Signals | null
): { log: string; toast: string } {
  if (signal) {
    return {
      log: `killed by signal ${signal}`,
      toast: `signal ${signal} — likely a native crash`,
    };
  }
  if (code === null) {
    return { log: "code null, no signal", toast: "code null" };
  }
  return { log: `code ${code}`, toast: `code ${code}` };
}
