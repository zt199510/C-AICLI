import type { InitializeResult, WorkspaceOpenResult } from "../generated/desktop-contracts";
import { createRuntimeStatus, type RuntimeCode, type RuntimeStatus } from "../shared/bridge-contract";
import type { AppHostLaunchSpec } from "./apphost-launch";

export interface RuntimeClient {
  start(spec: AppHostLaunchSpec): Promise<InitializeResult>;
  stop(): Promise<void>;
  openWorkspace(path: string): Promise<WorkspaceOpenResult>;
  forceTerminateForTest(): void;
  on(event: "exit" | "protocol-error", listener: () => void): this;
}

export interface AppHostRuntimeOptions {
  createClient(): RuntimeClient;
  resolveLaunch(): AppHostLaunchSpec;
}

export class AppHostRuntime {
  private client: RuntimeClient | null = null;
  private generation = 0;
  private status = createRuntimeStatus("runtime-stopped");
  private operation: Promise<RuntimeStatus> | null = null;
  private stopOperation: Promise<RuntimeStatus> | null = null;
  private stopping = false;
  private readonly listeners = new Set<(status: RuntimeStatus) => void>();

  constructor(private readonly options: AppHostRuntimeOptions) {}

  getStatus(): RuntimeStatus { return this.status; }

  subscribe(listener: (status: RuntimeStatus) => void): () => void {
    this.listeners.add(listener);
    listener(this.status);
    return () => this.listeners.delete(listener);
  }

  start(): Promise<RuntimeStatus> {
    if (this.stopping) return Promise.resolve(this.status);
    if (this.status.state === "ready") return Promise.resolve(this.status);
    if (this.operation) return this.operation;
    const operation = this.startGeneration("runtime-starting", "apphost-start-failed");
    this.operation = operation;
    void operation.finally(() => { if (this.operation === operation) this.operation = null; });
    return operation;
  }

  restart(): Promise<RuntimeStatus> {
    if (this.stopping || this.status.state === "stopped") return Promise.resolve(this.status);
    if (this.status.state !== "failed") return Promise.resolve(this.status);
    if (this.operation) return this.operation;
    const operation = (async () => {
      this.publish("runtime-restarting");
      const oldClient = this.client;
      this.client = null;
      this.generation++;
      await oldClient?.stop().catch(() => undefined);
      if (this.stopping) return this.status;
      return this.startGeneration(undefined, "restart-failed");
    })();
    this.operation = operation;
    void operation.finally(() => { if (this.operation === operation) this.operation = null; });
    return operation;
  }

  stop(): Promise<RuntimeStatus> {
    if (this.stopOperation) return this.stopOperation;
    this.stopping = true;
    this.publish("runtime-stopping");
    const operation = (async () => {
      const current = this.client;
      this.client = null;
      this.generation++;
      await current?.stop().catch(() => undefined);
      await this.operation?.catch(() => undefined);
      this.client = null;
      this.publish("runtime-stopped");
      return this.status;
    })();
    this.stopOperation = operation;
    return operation;
  }

  async openWorkspace(path: string): Promise<WorkspaceOpenResult> {
    if (this.status.state !== "ready" || !this.client || this.stopping) {
      throw new Error("AppHost is not ready.");
    }
    return this.client.openWorkspace(path);
  }

  forceTerminateForTest(): void {
    if (this.status.state !== "ready" || !this.client || this.stopping) {
      throw new Error("AppHost is not ready.");
    }
    this.client.forceTerminateForTest();
  }

  private async startGeneration(
    transitionalCode: "runtime-starting" | undefined,
    failureCode: "apphost-start-failed" | "restart-failed",
  ): Promise<RuntimeStatus> {
    if (transitionalCode) this.publish(transitionalCode);
    const generation = ++this.generation;
    const client = this.options.createClient();
    this.client = client;
    let protocolInvalid = false;
    client.on("protocol-error", () => {
      if (generation !== this.generation || this.stopping) return;
      protocolInvalid = true;
      this.publish("protocol-invalid");
    });
    client.on("exit", () => {
      if (generation !== this.generation || this.stopping || this.client !== client) return;
      this.client = null;
      if (protocolInvalid) return;
      this.publish("apphost-exited");
    });
    try {
      await client.start(this.options.resolveLaunch());
      if (generation !== this.generation || this.stopping) {
        await client.stop().catch(() => undefined);
        return this.status;
      }
      this.publish("runtime-ready");
    } catch (error) {
      if (generation !== this.generation || this.stopping) return this.status;
      this.client = null;
      const invalid = protocolInvalid || (error instanceof Error && error.message === "protocol-invalid");
      this.publish(invalid ? "protocol-invalid" : failureCode);
      await client.stop().catch(() => undefined);
    }
    return this.status;
  }

  private publish(code: RuntimeCode): void {
    this.status = createRuntimeStatus(code);
    for (const listener of this.listeners) listener(this.status);
  }
}
