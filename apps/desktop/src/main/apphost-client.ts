import { spawn, type ChildProcessWithoutNullStreams } from "node:child_process";
import { randomUUID } from "node:crypto";
import { EventEmitter } from "node:events";
import {
  CONTRACT_SHA256,
  DESKTOP_CAPABILITIES,
  DESKTOP_NOTIFICATIONS,
  PROTOCOL_LIMITS,
  PROTOCOL_VERSION,
  SCHEMA_VERSION,
  type InitializeResult,
  type ThreadChangedParams,
  type WorkspaceOpenResult,
} from "../generated/desktop-contracts";
import type { AppHostLaunchSpec } from "./apphost-launch";
import {
  INITIALIZE_REQUEST,
  SHUTDOWN_REQUEST,
  THREAD_CHANGED_NOTIFICATION,
  WORKSPACE_OPEN_REQUEST,
  type DesktopRequestDescriptor,
  type TimeoutClass,
} from "./desktop-requests";

interface PendingRequest {
  readonly descriptor: DesktopRequestDescriptor<object, unknown>;
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
      const separator = this.buffer.indexOf("\r\n\r\n");
      if (separator < 0) {
        if (this.buffer.length > PROTOCOL_LIMITS.maxHeaderBytes) {
          throw protocolError("frame-header-too-large");
        }
        return frames;
      }
      if (separator === 0) throw protocolError("frame-header-invalid");
      if (separator > PROTOCOL_LIMITS.maxHeaderBytes) throw protocolError("frame-header-too-large");
      const headerBytes = this.buffer.subarray(0, separator);
      if (headerBytes.some((byte) => byte > 0x7f)) throw protocolError("frame-header-invalid");
      const lines = headerBytes.toString("ascii").split("\r\n");
      const lengthValues: string[] = [];
      let contentTypeSeen = false;
      for (const line of lines) {
        const colon = line.indexOf(":");
        if (colon <= 0) throw protocolError("frame-header-invalid");
        const name = line.slice(0, colon).trim().toLowerCase();
        const value = line.slice(colon + 1).trim();
        if (name === "content-length") lengthValues.push(value);
        else if (name === "content-type") {
          if (contentTypeSeen || value.toLowerCase() !== "application/vscode-jsonrpc; charset=utf-8") {
            throw protocolError("frame-header-invalid");
          }
          contentTypeSeen = true;
        } else {
          throw protocolError("frame-header-invalid");
        }
      }
      if (lengthValues.length === 0) throw protocolError("frame-content-length-missing");
      if (lengthValues.length !== 1 || !/^\d+$/.test(lengthValues[0] ?? "")) {
        throw protocolError("frame-content-length-invalid");
      }
      const length = Number(lengthValues[0]);
      if (!Number.isSafeInteger(length)) throw protocolError("frame-content-length-invalid");
      if (length === 0) throw protocolError("frame-body-empty");
      if (length > PROTOCOL_LIMITS.maxBodyBytes) throw protocolError("frame-body-too-large");

      const bodyStart = separator + 4;
      if (this.buffer.length < bodyStart + length) return frames;
      frames.push(this.buffer.subarray(bodyStart, bodyStart + length));
      this.buffer = this.buffer.subarray(bodyStart + length);
    }
  }

  finish(): void {
    if (this.buffer.length === 0) return;
    if (this.buffer.indexOf("\r\n\r\n") < 0) throw protocolError("frame-header-incomplete");
    throw protocolError("frame-body-incomplete");
  }
}

export class AppHostClient extends EventEmitter {
  private decoder = new FrameDecoder();
  private readonly pending = new Map<number, PendingRequest>();
  private process: ChildProcessWithoutNullStreams | null = null;
  private nextId = 1;
  private diagnostics = "";
  private readonly clientInstanceId = randomUUID();
  private protocolFailed = false;

  async start(spec: AppHostLaunchSpec): Promise<InitializeResult> {
    if (this.process) throw new Error("AppHost is already running.");
    this.decoder = new FrameDecoder();
    this.protocolFailed = false;
    const child = spawn(spec.command, spec.args, {
      cwd: spec.cwd,
      stdio: ["pipe", "pipe", "pipe"],
      windowsHide: true,
      shell: false,
    });
    this.process = child;
    child.stdout.on("data", (chunk: Buffer) => this.handleStdout(chunk));
    child.stdout.on("end", () => this.handleStdoutEnd());
    child.stderr.on("data", (chunk: Buffer) => {
      this.diagnostics = (this.diagnostics + chunk.toString("utf8")).slice(
        -PROTOCOL_LIMITS.maxRetainedStderrBytes,
      );
    });
    child.on("error", (error) => this.failAll(error));
    child.on("exit", (code) => {
      if (this.process === child) this.process = null;
      this.failAll(new Error("AppHost exited unexpectedly."));
      this.emit("exit", code);
    });

    const initialized = await this.request(INITIALIZE_REQUEST, {
      schemaVersion: SCHEMA_VERSION,
      protocolVersion: PROTOCOL_VERSION,
      contractSha256: CONTRACT_SHA256,
      clientName: "caicli-desktop",
      clientVersion: "0.6.0",
      clientInstanceId: this.clientInstanceId,
      requestedCapabilities: [
        DESKTOP_CAPABILITIES.FramedJsonRpc,
        DESKTOP_CAPABILITIES.WorkspaceSession,
        DESKTOP_CAPABILITIES.ApplicationOutcome,
        DESKTOP_CAPABILITIES.ComposerControlledContext,
        DESKTOP_CAPABILITIES.TurnWritePath,
        DESKTOP_CAPABILITIES.ThreadChanged,
        DESKTOP_CAPABILITIES.TerminalUserSession,
        DESKTOP_CAPABILITIES.ArtifactReview,
        DESKTOP_CAPABILITIES.GerberReview,
      ],
    });
    if (!isExactHandshake(initialized)) {
      this.fatalProtocolFailure();
      throw protocolError("protocol-invalid");
    }
    return initialized;
  }

  request<TParams extends object, TResult>(
    descriptor: DesktopRequestDescriptor<TParams, TResult>,
    parameters: TParams,
  ): Promise<TResult> {
    const child = this.process;
    if (!child) return Promise.reject(new Error("AppHost is not running."));
    const id = this.nextId++;
    if (!Number.isSafeInteger(id) || id > PROTOCOL_LIMITS.maxRequestId) {
      return Promise.reject(protocolError("protocol-invalid"));
    }
    const payload = Buffer.from(
      JSON.stringify({ jsonrpc: "2.0", id, method: descriptor.method, params: parameters }),
      "utf8",
    );
    if (payload.length > PROTOCOL_LIMITS.maxBodyBytes) {
      return Promise.reject(new Error("Protocol request is too large."));
    }
    const frame = Buffer.concat([
      Buffer.from(`Content-Length: ${payload.length}\r\n\r\n`, "ascii"),
      payload,
    ]);
    const timeoutMs = timeoutFor(descriptor.timeoutClass);

    if (!descriptor.isParams(parameters)) return Promise.reject(protocolError("protocol-invalid"));
    return new Promise<TResult>((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error("AppHost request timed out."));
      }, timeoutMs);
      this.pending.set(id, {
        descriptor: descriptor as DesktopRequestDescriptor<object, unknown>,
        resolve,
        reject,
        timeout,
      });
      child.stdin.write(frame, (error) => {
        if (!error) return;
        clearTimeout(timeout);
        this.pending.delete(id);
        reject(error);
      });
    });
  }

  openWorkspace(workspacePath: string): Promise<WorkspaceOpenResult> {
    return this.request(WORKSPACE_OPEN_REQUEST, { schemaVersion: SCHEMA_VERSION, path: workspacePath });
  }

  async stop(): Promise<void> {
    const child = this.process;
    if (!child) return;
    const shutdownTimeoutMs = PROTOCOL_LIMITS.shutdownDrainMs + 500;
    const deadline = Date.now() + shutdownTimeoutMs;
    try {
      await this.request(SHUTDOWN_REQUEST, { schemaVersion: SCHEMA_VERSION, reason: "desktop-exit" });
      await new Promise<void>((resolve, reject) => {
        if (child.exitCode !== null) return resolve();
        const timeout = setTimeout(
          () => reject(new Error("AppHost shutdown timed out.")),
          Math.max(1, deadline - Date.now()),
        );
        child.once("exit", () => { clearTimeout(timeout); resolve(); });
      });
    } catch {
      if (child.exitCode === null) child.kill();
    }
  }

  forceTerminateForTest(): void {
    this.process?.kill();
  }

  getDiagnostics(): string { return this.diagnostics; }
  isRunning(): boolean { return this.process !== null; }

  private handleStdout(chunk: Buffer): void {
    try {
      for (const frame of this.decoder.push(chunk)) this.handleFrame(frame);
    } catch {
      this.fatalProtocolFailure();
    }
  }

  private handleStdoutEnd(): void {
    try { this.decoder.finish(); }
    catch { this.fatalProtocolFailure(); }
  }

  private handleFrame(frame: Buffer): void {
    const message = decodeJsonRpcMessage(frame);
    if ("method" in message) {
      this.emit("thread-changed", message.params);
      return;
    }
    const pending = this.pending.get(message.id);
    if (!pending) throw protocolError("protocol-invalid");
    clearTimeout(pending.timeout);
    this.pending.delete(message.id);
    if ("error" in message) {
      pending.reject(new Error(safeErrorMessage(message.error)));
      return;
    }
    if (!pending.descriptor.isResult(message.result)) {
      pending.reject(protocolError("protocol-invalid"));
      throw protocolError("protocol-invalid");
    }
    this.emit("response", pending.descriptor.method, message.result);
    pending.resolve(message.result);
  }

  private fatalProtocolFailure(): void {
    if (this.protocolFailed) return;
    this.protocolFailed = true;
    const error = protocolError("protocol-invalid");
    this.failAll(error);
    this.emit("protocol-error");
    this.process?.kill();
  }

  private failAll(error: Error): void {
    for (const pending of this.pending.values()) {
      clearTimeout(pending.timeout);
      pending.reject(error);
    }
    this.pending.clear();
  }
}

export type ParsedResponse =
  | { jsonrpc: "2.0"; id: number; result: unknown }
  | { jsonrpc: "2.0"; id: number; error: { code: number; message: string; data?: unknown } };

export type ParsedNotification = {
  jsonrpc: "2.0";
  method: typeof DESKTOP_NOTIFICATIONS.ThreadChangedNotification;
  params: ThreadChangedParams;
};

export type ParsedServerMessage = ParsedResponse | ParsedNotification;

export function decodeJsonRpcResponse(frame: Buffer): ParsedResponse {
  const message = decodeJsonRpcMessage(frame);
  if ("method" in message) throw protocolError("protocol-invalid");
  return message;
}

export function decodeJsonRpcMessage(frame: Buffer): ParsedServerMessage {
  let decoded: string;
  try { decoded = new TextDecoder("utf-8", { fatal: true }).decode(frame); }
  catch { throw protocolError("frame-utf8-invalid"); }
  let value: unknown;
  try { value = JSON.parse(decoded); }
  catch { throw protocolError("protocol-invalid"); }
  return parseServerMessage(value);
}

export function parseServerMessage(value: unknown): ParsedServerMessage {
  if (!isRecord(value) || value.jsonrpc !== "2.0") throw protocolError("protocol-invalid");
  if (Object.hasOwn(value, "method")) {
    if (!hasExactKeys(value, ["jsonrpc", "method", "params"]) ||
        value.method !== THREAD_CHANGED_NOTIFICATION.method ||
        !THREAD_CHANGED_NOTIFICATION.isParams(value.params)) {
      throw protocolError("protocol-invalid");
    }
    return value as unknown as ParsedNotification;
  }
  return parseResponse(value);
}

export function parseResponse(value: unknown): ParsedResponse {
  if (!isRecord(value) || value.jsonrpc !== "2.0") throw protocolError("protocol-invalid");
  if (!Number.isSafeInteger(value.id) || (value.id as number) < 1 || (value.id as number) > PROTOCOL_LIMITS.maxRequestId) {
    throw protocolError("protocol-invalid");
  }
  const hasResult = Object.hasOwn(value, "result");
  const hasError = Object.hasOwn(value, "error");
  if (hasResult === hasError) throw protocolError("protocol-invalid");
  const expectedKeys = hasResult ? ["id", "jsonrpc", "result"] : ["error", "id", "jsonrpc"];
  if (!hasExactKeys(value, expectedKeys)) throw protocolError("protocol-invalid");
  if (hasResult) return value as ParsedResponse;
  if (!isRecord(value.error) || typeof value.error.code !== "number" || !Number.isSafeInteger(value.error.code) || typeof value.error.message !== "string") {
    throw protocolError("protocol-invalid");
  }
  if (!hasOnlyKeys(value.error, ["code", "message", "data"])) throw protocolError("protocol-invalid");
  return value as ParsedResponse;
}

function safeErrorMessage(error: { message: string; data?: unknown }): string {
  if (isRecord(error.data) && typeof error.data.safeMessage === "string" && error.data.safeMessage.length <= 4096) {
    return error.data.safeMessage;
  }
  return error.message.length <= 4096 ? error.message : "AppHost request failed.";
}

function isExactHandshake(result: InitializeResult): boolean {
  const requested = [
    DESKTOP_CAPABILITIES.FramedJsonRpc,
    DESKTOP_CAPABILITIES.WorkspaceSession,
    DESKTOP_CAPABILITIES.ApplicationOutcome,
    DESKTOP_CAPABILITIES.ComposerControlledContext,
    DESKTOP_CAPABILITIES.TurnWritePath,
    DESKTOP_CAPABILITIES.ThreadChanged,
    DESKTOP_CAPABILITIES.TerminalUserSession,
    DESKTOP_CAPABILITIES.ArtifactReview,
    DESKTOP_CAPABILITIES.GerberReview,
  ];
  return result.schemaVersion === SCHEMA_VERSION &&
    result.protocolVersion === PROTOCOL_VERSION &&
    result.contractSha256 === CONTRACT_SHA256 &&
    result.negotiatedCapabilities.length === requested.length &&
    requested.every((capability) => result.negotiatedCapabilities.includes(capability)) &&
    result.notifications.length === 1 &&
    result.notifications[0] === DESKTOP_NOTIFICATIONS.ThreadChangedNotification &&
    result.security.transport === "framed-json-rpc-stdio" &&
    result.security.workspaceAuthority === "server" &&
    result.security.rendererNodeAccess === false &&
    result.security.arbitraryFileAccess === false &&
    result.security.arbitraryProcessAccess === false &&
    Object.entries(PROTOCOL_LIMITS).every(([key, expected]) =>
      result.limits[key as keyof typeof PROTOCOL_LIMITS] === expected,
    );
}

function timeoutFor(timeoutClass: TimeoutClass): number {
  switch (timeoutClass) {
    case "initialize": return PROTOCOL_LIMITS.initializeTimeoutMs;
    case "query": return PROTOCOL_LIMITS.defaultQueryTimeoutMs;
    case "mutation": return PROTOCOL_LIMITS.mutationTimeoutMs;
    case "shutdown": return PROTOCOL_LIMITS.shutdownDrainMs + 500;
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function hasOnlyKeys(value: Record<string, unknown>, allowed: readonly string[]): boolean {
  return Object.keys(value).every((key) => allowed.includes(key));
}

function hasExactKeys(value: Record<string, unknown>, expected: readonly string[]): boolean {
  return Object.keys(value).length === expected.length && expected.every((key) => Object.hasOwn(value, key));
}

function protocolError(code: string): Error {
  return new Error(code);
}
