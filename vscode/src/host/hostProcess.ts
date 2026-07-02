import { ChildProcess, spawn } from "child_process";
import { createInterface, Interface as ReadlineInterface } from "readline";
import * as vscode from "vscode";
import {
  JsonRpcRequest,
  JsonRpcResponse,
  JsonRpcNotification,
} from "./protocol";
import { log } from "../log";

type NotificationHandler = (params: unknown) => void;

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

  /**
   * Fired when the process exits without dispose() having been called (a crash or
   * external kill, not a normal shutdown or provider-driven restart). Receives a
   * short human-readable exit description.
   */
  onUnexpectedExit: ((detail: string) => void) | undefined;

  constructor(private readonly hostDllPath: string) {}

  async start(): Promise<void> {
    if (this.process) {
      return;
    }

    this.readyPromise = new Promise<void>((resolve, reject) => {
      const timeout = setTimeout(
        () => reject(new Error("Host did not send ready signal within 30s")),
        30000
      );

      log.info(`Spawning Verso.Host: dotnet ${this.hostDllPath}`);
      this.process = spawn("dotnet", [this.hostDllPath], {
        stdio: ["pipe", "pipe", "pipe"],
      });

      this.process.on("error", (err) => {
        log.error(`Verso.Host failed to spawn: ${err.message}`);
        clearTimeout(timeout);
        reject(err);
      });

      this.process.on("exit", (code, signal) => {
        const detail = describeExit(code, signal);
        if (!this.disposed) {
          log.warn(`Verso.Host exited unexpectedly (${detail.log})`);
          if (signal) {
            log.warn(
              `Host termination by signal indicates a native crash. ` +
                `On macOS, see ~/Library/Logs/DiagnosticReports/dotnet-*.ips ` +
                `for the faulting stack; on Linux, see your distro's coredump location.`
            );
          }
          vscode.window.showWarningMessage(
            `Verso host process exited (${detail.toast})`
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
          this.handleLine(line, () => {
            clearTimeout(timeout);
            resolve();
          });
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
