import {
  SCHEMA_VERSION,
  type ArtifactGetResult,
  type ArtifactListResult,
  type ChangesGetResult,
  type CatalogListResult,
  type ComposerCatalogSelectionData,
  type ComposerStateResult,
  type ContextResolveResult,
  type ContextSearchResult,
  type InitializeResult,
  type ReportGetResult,
  type ReportListResult,
  type ThreadChangedParams,
  type ThreadGetResult,
  type ThreadListResult,
  type ThreadSummaryResult,
  type WorkspaceOpenResult,
  type WorkspaceSnapshotData,
} from "../generated/desktop-contracts";
import { createRuntimeStatus, type RuntimeCode, type RuntimeStatus } from "../shared/bridge-contract";
import type { AppHostLaunchSpec } from "./apphost-launch";
import {
  ARTIFACT_GET_REQUEST,
  ARTIFACT_LIST_REQUEST,
  CATALOG_LIST_REQUEST,
  CHANGES_GET_REQUEST,
  COMPOSER_CLEAR_REQUEST,
  COMPOSER_ENQUEUE_REQUEST,
  COMPOSER_GET_REQUEST,
  CONTEXT_RESOLVE_REQUEST,
  CONTEXT_SEARCH_REQUEST,
  REPORT_GET_REQUEST,
  REPORT_LIST_REQUEST,
  REVIEW_LIST_PAGE_SIZE,
  THREAD_ARCHIVE_REQUEST,
  THREAD_CREATE_REQUEST,
  THREAD_GET_REQUEST,
  THREAD_LIST_PAGE_SIZE,
  THREAD_LIST_REQUEST,
  THREAD_RENAME_REQUEST,
  TIMELINE_PAGE_SIZE,
  type DesktopRequestDescriptor,
} from "./desktop-requests";

export interface RuntimeClient {
  start(spec: AppHostLaunchSpec): Promise<InitializeResult>;
  stop(): Promise<void>;
  openWorkspace(path: string): Promise<WorkspaceOpenResult>;
  request<TParams extends object, TResult>(
    descriptor: DesktopRequestDescriptor<TParams, TResult>,
    parameters: TParams,
  ): Promise<TResult>;
  forceTerminateForTest(): void;
  on(event: "exit" | "protocol-error", listener: () => void): this;
  on(event: "thread-changed", listener: (event: ThreadChangedParams) => void): this;
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
  private workspace: WorkspaceSnapshotData | null = null;
  private readonly listeners = new Set<(status: RuntimeStatus) => void>();
  private readonly threadListeners = new Set<(event: ThreadChangedParams) => void>();

  constructor(private readonly options: AppHostRuntimeOptions) {}

  getStatus(): RuntimeStatus { return this.status; }
  getWorkspaceSnapshot(): WorkspaceSnapshotData | null { return this.workspace; }

  subscribe(listener: (status: RuntimeStatus) => void): () => void {
    this.listeners.add(listener);
    listener(this.status);
    return () => this.listeners.delete(listener);
  }

  subscribeThreadChanged(listener: (event: ThreadChangedParams) => void): () => void {
    this.threadListeners.add(listener);
    return () => this.threadListeners.delete(listener);
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
      this.workspace = null;
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
      this.workspace = null;
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
    const result = await this.client.openWorkspace(path);
    if (result.succeeded && result.data) this.workspace = result.data;
    return result;
  }

  listThreads(): Promise<ThreadListResult> {
    return this.query(THREAD_LIST_REQUEST, { schemaVersion: SCHEMA_VERSION, pageSize: THREAD_LIST_PAGE_SIZE });
  }

  getThread(threadId: string, afterSequence: number): Promise<ThreadGetResult> {
    return this.query(THREAD_GET_REQUEST, {
      schemaVersion: SCHEMA_VERSION,
      threadId,
      afterSequence,
      timelinePageSize: TIMELINE_PAGE_SIZE,
    });
  }

  createThread(title: string): Promise<ThreadSummaryResult> {
    return this.query(THREAD_CREATE_REQUEST, { schemaVersion: SCHEMA_VERSION, title });
  }

  renameThread(threadId: string, expectedRevision: number, title: string): Promise<ThreadSummaryResult> {
    return this.query(THREAD_RENAME_REQUEST, { schemaVersion: SCHEMA_VERSION, threadId, expectedRevision, title });
  }

  archiveThread(threadId: string, expectedRevision: number): Promise<ThreadSummaryResult> {
    return this.query(THREAD_ARCHIVE_REQUEST, { schemaVersion: SCHEMA_VERSION, threadId, expectedRevision });
  }

  listCatalog(kind: "skills" | "experts" | "automations" | "project-packs"): Promise<CatalogListResult> {
    return this.query(CATALOG_LIST_REQUEST, { schemaVersion: SCHEMA_VERSION, kind, pageSize: 200 });
  }

  searchContext(query: string): Promise<ContextSearchResult> {
    return this.query(CONTEXT_SEARCH_REQUEST, { schemaVersion: SCHEMA_VERSION, query });
  }

  resolveContext(nativePath: string, kind: "file" | "folder"): Promise<ContextResolveResult> {
    return this.query(CONTEXT_RESOLVE_REQUEST, { schemaVersion: SCHEMA_VERSION, nativePath, kind });
  }

  getComposer(threadId: string): Promise<ComposerStateResult> {
    return this.query(COMPOSER_GET_REQUEST, { schemaVersion: SCHEMA_VERSION, threadId });
  }

  enqueueComposer(command: {
    threadId: string;
    expectedThreadRevision: number;
    expectedQueueRevision: number;
    clientMutationId: string;
    prompt: string;
    contextSelectionIds: readonly string[];
    catalogSelections: readonly ComposerCatalogSelectionData[];
  }): Promise<ComposerStateResult> {
    return this.query(COMPOSER_ENQUEUE_REQUEST, { schemaVersion: SCHEMA_VERSION, ...command });
  }

  clearComposer(threadId: string, expectedQueueRevision: number, clientMutationId: string): Promise<ComposerStateResult> {
    return this.query(COMPOSER_CLEAR_REQUEST, { schemaVersion: SCHEMA_VERSION, threadId, expectedQueueRevision, clientMutationId });
  }

  getChanges(sessionName?: string): Promise<ChangesGetResult> {
    return this.query(CHANGES_GET_REQUEST, sessionName === undefined
      ? { schemaVersion: SCHEMA_VERSION }
      : { schemaVersion: SCHEMA_VERSION, sessionName });
  }

  listReports(): Promise<ReportListResult> {
    return this.query(REPORT_LIST_REQUEST, { schemaVersion: SCHEMA_VERSION, pageSize: REVIEW_LIST_PAGE_SIZE });
  }

  getReport(reportId: string): Promise<ReportGetResult> {
    return this.query(REPORT_GET_REQUEST, { schemaVersion: SCHEMA_VERSION, reportId });
  }

  listArtifacts(): Promise<ArtifactListResult> {
    return this.query(ARTIFACT_LIST_REQUEST, { schemaVersion: SCHEMA_VERSION, pageSize: REVIEW_LIST_PAGE_SIZE });
  }

  getArtifact(artifactId: string): Promise<ArtifactGetResult> {
    return this.query(ARTIFACT_GET_REQUEST, { schemaVersion: SCHEMA_VERSION, artifactId });
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
      this.workspace = null;
      this.publish("protocol-invalid");
    });
    client.on("exit", () => {
      if (generation !== this.generation || this.stopping || this.client !== client) return;
      this.client = null;
      this.workspace = null;
      if (protocolInvalid) return;
      this.publish("apphost-exited");
    });
    client.on("thread-changed", (event) => {
      if (generation !== this.generation || this.stopping || this.client !== client || !this.workspace) return;
      if (event.workspaceId !== this.workspace.workspaceId) return;
      for (const listener of this.threadListeners) listener(event);
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

  private query<TParams extends object, TResult>(
    descriptor: DesktopRequestDescriptor<TParams, TResult>,
    params: TParams,
  ): Promise<TResult> {
    if (this.status.state !== "ready" || !this.client || this.stopping || !this.workspace) {
      return Promise.reject(new Error("Workspace is not ready."));
    }
    return this.client.request(descriptor, params);
  }
}
