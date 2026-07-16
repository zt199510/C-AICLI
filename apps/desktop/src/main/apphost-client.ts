import { spawn, type ChildProcessWithoutNullStreams } from "node:child_process";
import { EventEmitter } from "node:events";
import {
  DESKTOP_METHODS,
  PROTOCOL_LIMITS,
  PROTOCOL_VERSION,
  type InitializeResult,
  type ShutdownResult,
} from "../generated/desktop-contracts";
import type { AppHostLaunchSpec } from "./apphost-launch";

interface JsonRpcResponse<T> {
  jsonrpc: "2.0";
  id: number;
  result?: T;
  error?: {
    code: number;
    message: string;
    data?: { errorCode?: string; safeMessage?: string };
  };
}

interface PendingRequest {
  resolve(value: unknown): void;
  reject(reason: Error): void;
  timeout: NodeJS.Timeout;
}

export class FrameDecoder {
  private buffer = Buffer.alloc(0);

  push(chunk: Buffer): Buffer[] {
    this.buffer = Buffer.concat([this.buffer, chunk]);
    const frames: Buffer[] = [];
    while (true) {
      const separator = this.buffer.indexOf("\r\n\r\n", 0, "ascii");
      if (separator < 0) {
        if (this.buffer.length > PROTOCOL_LIMITS.maxHeaderBytes) {
          throw new Error("frame-header-too-large");
        }
        return frames;
      }

      if (separator > PROTOCOL_LIMITS.maxHeaderBytes) throw new Error("frame-header-too-large");
      const header = this.buffer.subarray(0, separator).toString("ascii");
      const lengthHeaders = header
        .split("\r\n")
        .filter((line) => line.toLowerCase().startsWith("content-length:"));
      if (lengthHeaders.length !== 1) throw new Error("frame-content-length-invalid");
      const rawLength = lengthHeaders[0]?.slice(lengthHeaders[0].indexOf(":") + 1).trim() ?? "";
      if (!/^\d+$/.test(rawLength)) throw new Error("frame-content-length-invalid");
      const length = Number(rawLength);
      if (!Number.isSafeInteger(length) || length > PROTOCOL_LIMITS.maxBodyBytes) {
        throw new Error("frame-body-too-large");
      }

      const bodyStart = separator + 4;
      if (this.buffer.length < bodyStart + length) return frames;
      frames.push(this.buffer.subarray(bodyStart, bodyStart + length));
      this.buffer = this.buffer.subarray(bodyStart + length);
    }
  }
}

export class AppHostClient extends EventEmitter {
  private readonly decoder = new FrameDecoder();
  private readonly pending = new Map<number, PendingRequest>();
  private process: ChildProcessWithoutNullStreams | null = null;
  private nextId = 1;
  private diagnostics = "";

  async start(spec: AppHostLaunchSpec): Promise<InitializeResult> {
    if (this.process) throw new Error("AppHost is already running.");
    const child = spawn(spec.command, spec.args, {
      cwd: spec.cwd,
      stdio: ["pipe", "pipe", "pipe"],
      windowsHide: true,
      shell: false,
    });
    this.process = child;
    child.stdout.on("data", (chunk: Buffer) => this.handleStdout(chunk));
    child.stderr.on("data", (chunk: Buffer) => {
      this.diagnostics = (this.diagnostics + chunk.toString("utf8")).slice(
        -PROTOCOL_LIMITS.maxDiagnosticBytes,
      );
    });
    child.on("error", (error) => this.failAll(error));
    child.on("exit", (code) => {
      this.process = null;
      this.failAll(new Error(`AppHost exited (${code ?? "unknown"}).`));
      this.emit("exit", code);
    });

    return this.request<InitializeResult>(DESKTOP_METHODS.InitializeMethod, {
      protocolVersion: PROTOCOL_VERSION,
      clientName: "caicli-desktop",
      clientVersion: "0.6.0",
    });
  }

  request<T>(method: string, parameters: object, timeoutMs = 5000): Promise<T> {
    const child = this.process;
    if (!child) return Promise.reject(new Error("AppHost is not running."));
    const id = this.nextId++;
    const payload = Buffer.from(
      JSON.stringify({ jsonrpc: "2.0", id, method, params: parameters }),
      "utf8",
    );
    if (payload.length > PROTOCOL_LIMITS.maxBodyBytes) {
      return Promise.reject(new Error("Protocol request is too large."));
    }
    const frame = Buffer.concat([
      Buffer.from(`Content-Length: ${payload.length}\r\n\r\n`, "ascii"),
      payload,
    ]);

    return new Promise<T>((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`AppHost request timed out: ${method}`));
      }, timeoutMs);
      this.pending.set(id, {
        resolve: (value) => resolve(value as T),
        reject,
        timeout,
      });
      child.stdin.write(frame, (error) => {
        if (error) {
          clearTimeout(timeout);
          this.pending.delete(id);
          reject(error);
        }
      });
    });
  }

  async stop(): Promise<void> {
    const child = this.process;
    if (!child) return;
    try {
      await this.request<ShutdownResult>(DESKTOP_METHODS.ShutdownMethod, {
        reason: "desktop-exit",
      }, 1500);
      await new Promise<void>((resolve) => {
        if (child.exitCode !== null) resolve();
        else child.once("exit", () => resolve());
      });
    } catch {
      if (child.exitCode === null) child.kill();
    }
  }

  getDiagnostics(): string {
    return this.diagnostics;
  }

  isRunning(): boolean {
    return this.process !== null;
  }

  private handleStdout(chunk: Buffer): void {
    try {
      for (const frame of this.decoder.push(chunk)) {
        const response = JSON.parse(frame.toString("utf8")) as JsonRpcResponse<unknown>;
        const pending = this.pending.get(response.id);
        if (!pending) continue;
        clearTimeout(pending.timeout);
        this.pending.delete(response.id);
        if (response.error) {
          pending.reject(new Error(response.error.data?.safeMessage ?? response.error.message));
        } else {
          pending.resolve(response.result);
        }
      }
    } catch (error) {
      this.failAll(error instanceof Error ? error : new Error("Invalid AppHost response."));
      this.process?.kill();
    }
  }

  private failAll(error: Error): void {
    for (const pending of this.pending.values()) {
      clearTimeout(pending.timeout);
      pending.reject(error);
    }
    this.pending.clear();
  }
}
