import {
  SCHEMA_VERSION,
  isArtifactGetParams,
  isChangesGetParams,
  isCatalogListParams,
  isComposerClearParams,
  isComposerEnqueueParams,
  isComposerGetParams,
  isContextSearchParams,
  isContextResolveResult,
  isTurnStartParams,
  isTurnCancelParams,
  isApprovalResolveParams,
  isTurnResumeParams,
  isTurnRestartParams,
  isReportGetParams,
  isThreadArchiveParams,
  isThreadCreateParams,
  isThreadGetParams,
  isThreadRenameParams,
  type ArtifactGetResult,
  type ArtifactListResult,
  type ChangesGetResult,
  type CatalogListResult,
  type ComposerCatalogSelectionData,
  type ComposerStateResult,
  type ContextResolveResult,
  type ContextSearchResult,
  type ReportGetResult,
  type ReportListResult,
  type ThreadChangedParams,
  type ThreadGetResult,
  type ThreadListResult,
  type ThreadSummaryResult,
  type WorkspaceOpenResult,
  type WorkspaceSnapshotData,
  type TurnExecutionStateResult,
} from "../generated/desktop-contracts";

export const IPC_CHANNELS = Object.freeze({
  getRuntimeStatus: "runtime:get-status",
  restartRuntime: "runtime:restart",
  openWorkspace: "workspace:open",
  getWorkspaceSnapshot: "workspace:get-snapshot",
  listThreads: "thread:list",
  getThread: "thread:get",
  createThread: "thread:create",
  renameThread: "thread:rename",
  archiveThread: "thread:archive",
  threadChanged: "thread:changed",
  getChanges: "changes:get",
  listReports: "report:list",
  getReport: "report:get",
  listArtifacts: "artifact:list",
  getArtifact: "artifact:get",
  listCatalog: "catalog:list",
  searchContext: "context:search",
  pickFile: "context:pick-file",
  pickFolder: "context:pick-folder",
  getComposer: "composer:get",
  enqueueComposer: "composer:enqueue",
  clearComposer: "composer:clear",
  startTurn: "turn:start",
  cancelTurn: "turn:cancel",
  resolveApproval: "approval:resolve",
  resumeTurn: "turn:resume",
  restartTurn: "turn:restart",
  runtimeStatus: "runtime:status",
});

export interface GetThreadCommand { readonly threadId: string; readonly afterSequence: number; }
export interface CreateThreadCommand { readonly title: string; }
export interface RenameThreadCommand { readonly threadId: string; readonly expectedRevision: number; readonly title: string; }
export interface ArchiveThreadCommand { readonly threadId: string; readonly expectedRevision: number; }
export interface GetChangesCommand { readonly sessionName?: string; }
export interface GetReportCommand { readonly reportId: string; }
export interface GetArtifactCommand { readonly artifactId: string; }
export interface ListCatalogCommand { readonly kind: "skills" | "experts" | "automations"; }
export interface SearchContextCommand { readonly query: string; }
export interface GetComposerCommand { readonly threadId: string; }
export interface EnqueueComposerCommand {
  readonly threadId: string;
  readonly expectedThreadRevision: number;
  readonly expectedQueueRevision: number;
  readonly clientMutationId: string;
  readonly prompt: string;
  readonly contextSelectionIds: readonly string[];
  readonly catalogSelections: readonly ComposerCatalogSelectionData[];
}
export interface ClearComposerCommand {
  readonly threadId: string;
  readonly expectedQueueRevision: number;
  readonly clientMutationId: string;
}
export interface StartTurnCommand { readonly threadId: string; readonly expectedThreadRevision: number; readonly expectedQueueRevision: number; readonly clientMutationId: string; }
export interface CancelTurnCommand { readonly threadId: string; readonly turnId: string; readonly expectedThreadRevision: number; readonly expectedTurnRevision: number; readonly clientMutationId: string; }
export interface ResolveApprovalCommand { readonly threadId: string; readonly turnId: string; readonly requestId: string; readonly decision: "approve" | "deny"; readonly expectedThreadRevision: number; readonly expectedTurnRevision: number; readonly expectedApprovalRevision: number; readonly clientMutationId: string; }
export interface ResumeTurnCommand { readonly threadId: string; readonly turnId: string; readonly expectedThreadRevision: number; readonly expectedTurnRevision: number; readonly checkpointId: string; readonly clientMutationId: string; }
export interface RestartTurnCommand { readonly threadId: string; readonly sourceTurnId: string; readonly expectedThreadRevision: number; readonly expectedSourceTurnRevision: number; readonly confirmed: boolean; readonly clientMutationId: string; }
export interface ContextPickResult {
  readonly schemaVersion: 1;
  readonly canceled: boolean;
  readonly result: ContextResolveResult | null;
}

export type RuntimeState =
  | "starting"
  | "ready"
  | "restarting"
  | "stopping"
  | "stopped"
  | "failed";

export type RuntimeCode =
  | "runtime-starting"
  | "runtime-ready"
  | "runtime-restarting"
  | "runtime-stopping"
  | "runtime-stopped"
  | "apphost-start-failed"
  | "apphost-exited"
  | "protocol-invalid"
  | "restart-failed";

export interface RuntimeStatus {
  schemaVersion: 1;
  state: RuntimeState;
  code: RuntimeCode;
  message: string;
  canRestart: boolean;
  protocolVersion: "desktop-v1" | null;
}

export interface DesktopBridge {
  getRuntimeStatus(): Promise<RuntimeStatus>;
  restartRuntime(): Promise<RuntimeStatus>;
  openWorkspace(): Promise<WorkspaceOpenResult | null>;
  getWorkspaceSnapshot(): Promise<WorkspaceSnapshotData | null>;
  listThreads(): Promise<ThreadListResult>;
  getThread(command: GetThreadCommand): Promise<ThreadGetResult>;
  createThread(command: CreateThreadCommand): Promise<ThreadSummaryResult>;
  renameThread(command: RenameThreadCommand): Promise<ThreadSummaryResult>;
  archiveThread(command: ArchiveThreadCommand): Promise<ThreadSummaryResult>;
  getChanges(command?: GetChangesCommand): Promise<ChangesGetResult>;
  listReports(): Promise<ReportListResult>;
  getReport(command: GetReportCommand): Promise<ReportGetResult>;
  listArtifacts(): Promise<ArtifactListResult>;
  getArtifact(command: GetArtifactCommand): Promise<ArtifactGetResult>;
  listCatalog(command: ListCatalogCommand): Promise<CatalogListResult>;
  searchContext(command: SearchContextCommand): Promise<ContextSearchResult>;
  pickFile(): Promise<ContextPickResult>;
  pickFolder(): Promise<ContextPickResult>;
  getComposer(command: GetComposerCommand): Promise<ComposerStateResult>;
  enqueueComposer(command: EnqueueComposerCommand): Promise<ComposerStateResult>;
  clearComposer(command: ClearComposerCommand): Promise<ComposerStateResult>;
  startTurn(command: StartTurnCommand): Promise<TurnExecutionStateResult>;
  cancelTurn(command: CancelTurnCommand): Promise<TurnExecutionStateResult>;
  resolveApproval(command: ResolveApprovalCommand): Promise<TurnExecutionStateResult>;
  resumeTurn(command: ResumeTurnCommand): Promise<TurnExecutionStateResult>;
  restartTurn(command: RestartTurnCommand): Promise<TurnExecutionStateResult>;
  onRuntimeStatus(listener: (status: RuntimeStatus) => void): () => void;
  onThreadChanged(listener: (event: ThreadChangedParams) => void): () => void;
}

const allowedCombinations = Object.freeze({
  "runtime-starting": ["starting", "Starting AppHost", false, null],
  "runtime-ready": ["ready", "AppHost ready", false, "desktop-v1"],
  "runtime-restarting": ["restarting", "Restarting AppHost", false, null],
  "runtime-stopping": ["stopping", "Stopping AppHost", false, null],
  "runtime-stopped": ["stopped", "AppHost stopped", false, null],
  "apphost-start-failed": ["failed", "AppHost failed to start", true, null],
  "apphost-exited": ["failed", "AppHost stopped unexpectedly", true, null],
  "protocol-invalid": ["failed", "AppHost protocol validation failed", true, null],
  "restart-failed": ["failed", "AppHost restart failed", true, null],
} as const satisfies Record<RuntimeCode, readonly [RuntimeState, string, boolean, "desktop-v1" | null]>);

const statusKeys = Object.freeze([
  "schemaVersion",
  "state",
  "code",
  "message",
  "canRestart",
  "protocolVersion",
] as const);

export function createRuntimeStatus(code: RuntimeCode): RuntimeStatus {
  const [state, message, canRestart, protocolVersion] = allowedCombinations[code];
  return Object.freeze({
    schemaVersion: 1,
    state,
    code,
    message,
    canRestart,
    protocolVersion,
  });
}

export function isRuntimeStatus(value: unknown): value is RuntimeStatus {
  if (!isRecord(value)) return false;
  const keys = Object.keys(value).sort();
  if (keys.length !== statusKeys.length || !statusKeys.every((key) => keys.includes(key))) {
    return false;
  }
  if (value.schemaVersion !== 1 || typeof value.code !== "string") return false;
  if (!Object.hasOwn(allowedCombinations, value.code)) return false;
  if (typeof value.message !== "string" || new TextEncoder().encode(value.message).length > 256) {
    return false;
  }
  const [state, message, canRestart, protocolVersion] =
    allowedCombinations[value.code as RuntimeCode];
  return (
    value.state === state &&
    value.message === message &&
    value.canRestart === canRestart &&
    value.protocolVersion === protocolVersion
  );
}

export function assertRuntimeStatus(value: unknown): RuntimeStatus {
  if (!isRuntimeStatus(value)) throw new Error("Invalid runtime status.");
  return value;
}

export function isGetThreadCommand(value: unknown): value is GetThreadCommand {
  return hasExactKeys(value, ["threadId", "afterSequence"]) && isThreadGetParams({
    schemaVersion: SCHEMA_VERSION,
    threadId: value.threadId,
    afterSequence: value.afterSequence,
    timelinePageSize: 100,
  });
}

export function isCreateThreadCommand(value: unknown): value is CreateThreadCommand {
  return hasExactKeys(value, ["title"]) && isThreadCreateParams({ schemaVersion: SCHEMA_VERSION, title: value.title });
}

export function isRenameThreadCommand(value: unknown): value is RenameThreadCommand {
  return hasExactKeys(value, ["threadId", "expectedRevision", "title"]) &&
    isThreadRenameParams({ schemaVersion: SCHEMA_VERSION, threadId: value.threadId, expectedRevision: value.expectedRevision, title: value.title });
}

export function isArchiveThreadCommand(value: unknown): value is ArchiveThreadCommand {
  return hasExactKeys(value, ["threadId", "expectedRevision"]) &&
    isThreadArchiveParams({ schemaVersion: SCHEMA_VERSION, threadId: value.threadId, expectedRevision: value.expectedRevision });
}

export function isGetChangesCommand(value: unknown): value is GetChangesCommand {
  if (!isRecord(value)) return false;
  const keys = Object.keys(value);
  return keys.every((key) => key === "sessionName") && isChangesGetParams({ schemaVersion: SCHEMA_VERSION, ...value });
}

export function isGetReportCommand(value: unknown): value is GetReportCommand {
  return hasExactKeys(value, ["reportId"]) && isReportGetParams({ schemaVersion: SCHEMA_VERSION, reportId: value.reportId });
}

export function isGetArtifactCommand(value: unknown): value is GetArtifactCommand {
  return hasExactKeys(value, ["artifactId"]) && isArtifactGetParams({ schemaVersion: SCHEMA_VERSION, artifactId: value.artifactId });
}

export function isListCatalogCommand(value: unknown): value is ListCatalogCommand {
  return hasExactKeys(value, ["kind"]) && isCatalogListParams({ schemaVersion: SCHEMA_VERSION, kind: value.kind, pageSize: 200 }) && value.kind !== "project-packs";
}

export function isSearchContextCommand(value: unknown): value is SearchContextCommand {
  return hasExactKeys(value, ["query"]) && isContextSearchParams({ schemaVersion: SCHEMA_VERSION, query: value.query });
}

export function isGetComposerCommand(value: unknown): value is GetComposerCommand {
  return hasExactKeys(value, ["threadId"]) && isComposerGetParams({ schemaVersion: SCHEMA_VERSION, threadId: value.threadId });
}

export function isEnqueueComposerCommand(value: unknown): value is EnqueueComposerCommand {
  return hasExactKeys(value, ["threadId", "expectedThreadRevision", "expectedQueueRevision", "clientMutationId", "prompt", "contextSelectionIds", "catalogSelections"]) &&
    isComposerEnqueueParams({ schemaVersion: SCHEMA_VERSION, ...value });
}

export function isClearComposerCommand(value: unknown): value is ClearComposerCommand {
  return hasExactKeys(value, ["threadId", "expectedQueueRevision", "clientMutationId"]) &&
    isComposerClearParams({ schemaVersion: SCHEMA_VERSION, ...value });
}

export function isStartTurnCommand(value: unknown): value is StartTurnCommand {
  return hasExactKeys(value, ["threadId", "expectedThreadRevision", "expectedQueueRevision", "clientMutationId"]) &&
    isTurnStartParams({ schemaVersion: SCHEMA_VERSION, ...value });
}
export function isCancelTurnCommand(value: unknown): value is CancelTurnCommand {
  return hasExactKeys(value, ["threadId", "turnId", "expectedThreadRevision", "expectedTurnRevision", "clientMutationId"]) &&
    isTurnCancelParams({ schemaVersion: SCHEMA_VERSION, ...value });
}
export function isResolveApprovalCommand(value: unknown): value is ResolveApprovalCommand {
  return hasExactKeys(value, ["threadId", "turnId", "requestId", "decision", "expectedThreadRevision", "expectedTurnRevision", "expectedApprovalRevision", "clientMutationId"]) &&
    isApprovalResolveParams({ schemaVersion: SCHEMA_VERSION, ...value });
}
export function isResumeTurnCommand(value: unknown): value is ResumeTurnCommand {
  return hasExactKeys(value, ["threadId", "turnId", "expectedThreadRevision", "expectedTurnRevision", "checkpointId", "clientMutationId"]) &&
    isTurnResumeParams({ schemaVersion: SCHEMA_VERSION, ...value });
}
export function isRestartTurnCommand(value: unknown): value is RestartTurnCommand {
  return hasExactKeys(value, ["threadId", "sourceTurnId", "expectedThreadRevision", "expectedSourceTurnRevision", "confirmed", "clientMutationId"]) &&
    isTurnRestartParams({ schemaVersion: SCHEMA_VERSION, ...value });
}

export function isContextPickResult(value: unknown): value is ContextPickResult {
  if (!hasExactKeys(value, ["schemaVersion", "canceled", "result"]) || value.schemaVersion !== 1 || typeof value.canceled !== "boolean") return false;
  return value.canceled ? value.result === null : isContextResolveResult(value.result);
}

function hasExactKeys(value: unknown, expected: readonly string[]): value is Record<string, unknown> {
  return isRecord(value) && Object.keys(value).length === expected.length && expected.every((key) => Object.hasOwn(value, key));
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
