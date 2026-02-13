import { ChildProcess, spawn } from "child_process";
import { createInterface, Interface as ReadlineInterface } from "readline";
import * as vscode from "vscode";
import {
  JsonRpcRequest,
  JsonRpcResponse,
  JsonRpcNotification,
} from "./protocol";

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

      this.process = spawn("dotnet", [this.hostDllPath], {
        stdio: ["pipe", "pipe", "pipe"],
      });

      this.process.on("error", (err) => {
        clearTimeout(timeout);
        reject(err);
      });

      this.process.on("exit", (code) => {
        if (!this.disposed) {
          vscode.window.showWarningMessage(
            `Verso host process exited with code ${code}`
          );
        }
        this.cleanup();
      });

      if (this.process.stderr) {
        this.process.stderr.on("data", (data: Buffer) => {
          const text = data.toString().trim();
          if (text) {
            console.error(`[Verso.Host stderr] ${text}`);
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
      console.error(`[Verso.Host] Failed to parse: ${line}`);
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
