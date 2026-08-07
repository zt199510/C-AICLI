import {
  SCHEMA_VERSION,
  isArtifactGetParams,
  isTerminalOpenParams, isTerminalInputParams, isTerminalResizeParams, isTerminalMutationParams, isTerminalGetParams,
  isArtifactReviewParams, isGerberReviewParams, isGerberDecisionParams,
  isChangesGetParams, isChangesMutateParams,
  isCatalogListParams,
  isComposerClearParams,
  isComposerEnqueueParams,
  isComposerGetParams,
  isContextSearchParams,
  isContextResolveResult,
  isTurnStartParams,
  isTurnCancelParams,
  isApprovalResolveParams,
  isSubagentListParams, isSubagentStartParams, isSubagentMutationParams, isSubagentApprovalResolveParams,
  isTurnResumeParams,
  isTurnRestartParams,
  isReportGetParams,
  isThreadArchiveParams,
  isThreadCreateParams,
  isThreadGetParams,
  isThreadRenameParams,
  type ArtifactGetResult,
  type ArtifactListResult,
  type TerminalStateResult, type TerminalProfileListResult, type ArtifactReviewResult, type ArtifactExportResult, type GerberReviewResult,
  type ChangesGetResult, type ChangesMutateParams, type ChangesMutateResult,
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
  type SubagentListParams, type SubagentStartParams, type SubagentMutationParams, type SubagentApprovalResolveParams, type SubagentResult,
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
  mutateChanges: "changes:mutate",
  listReports: "report:list",
  getReport: "report:get",
  listArtifacts: "artifact:list",
  getArtifact: "artifact:get",
  openTerminal: "terminal:open",
  inputTerminal: "terminal:input",
  resizeTerminal: "terminal:resize",
  cancelTerminal: "terminal:cancel",
  closeTerminal: "terminal:close",
  getTerminal: "terminal:get",
  listTerminalProfiles: "terminal:profiles:get",
  previewArtifact: "artifact:preview",
  exportArtifact: "artifact:export",
  verifyArtifact: "artifact:verify",
  getGerberReview: "gerber:review:get",
  getGerberPreview: "gerber:preview",
  acceptGerber: "gerber:accept",
  rejectGerber: "gerber:reject",
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
  listSubagents: "subagent:list",
  startSubagent: "subagent:start",
  cancelSubagent: "subagent:cancel",
  takeoverSubagent: "subagent:takeover",
  resolveSubagentApproval: "subagent:approval:resolve",
  resumeTurn: "turn:resume",
  restartTurn: "turn:restart",
  runtimeStatus: "runtime:status",
  getSettings: "settings:get",
  setSettings: "settings:set",
});

export interface GetThreadCommand { readonly threadId: string; readonly afterSequence: number; }
export interface CreateThreadCommand { readonly title: string; }
export interface RenameThreadCommand { readonly threadId: string; readonly expectedRevision: number; readonly title: string; }
export interface ArchiveThreadCommand { readonly threadId: string; readonly expectedRevision: number; }
export interface GetChangesCommand { readonly sessionName?: string; }
export type MutateChangesCommand = Omit<ChangesMutateParams, "schemaVersion">;
export interface GetReportCommand { readonly reportId: string; }
export interface GetArtifactCommand { readonly artifactId: string; }
export type TerminalProfileId = "system-default" | "powershell" | "cmd" | "wsl" | "git-bash";
export interface OpenTerminalCommand { readonly shellProfile: TerminalProfileId; readonly clientMutationId: string; }
export interface InputTerminalCommand { readonly sessionId: string; readonly text: string; readonly clientMutationId: string; }
export interface ResizeTerminalCommand { readonly sessionId: string; readonly cols: number; readonly rows: number; readonly clientMutationId: string; }
export interface TerminalMutationCommand { readonly sessionId: string; readonly clientMutationId: string; }
export interface GetTerminalCommand { readonly sessionId: string; readonly afterCursor: number; }
export interface ArtifactReviewCommand { readonly artifactId: string; }
export interface GerberReviewCommand { readonly runId: string; }
export interface GerberDecisionCommand { readonly runId: string; readonly expectedRevision: number; readonly reason: string; readonly clientMutationId: string; }
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
  readonly modelOverride?: string;
  readonly approvalPreference?: ApprovalPreference;
  readonly disabledTools?: readonly string[];
  readonly sourceThreadId?: string;
  readonly sourceItemId?: string;
  readonly sourceAction?: "edit" | "branch";
}
export interface ClearComposerCommand {
  readonly threadId: string;
  readonly expectedQueueRevision: number;
  readonly clientMutationId: string;
}
export interface StartTurnCommand { readonly threadId: string; readonly expectedThreadRevision: number; readonly expectedQueueRevision: number; readonly clientMutationId: string; }
export interface CancelTurnCommand { readonly threadId: string; readonly turnId: string; readonly expectedThreadRevision: number; readonly expectedTurnRevision: number; readonly clientMutationId: string; }
export interface ResolveApprovalCommand { readonly threadId: string; readonly turnId: string; readonly requestId: string; readonly decision: "approve" | "deny"; readonly expectedThreadRevision: number; readonly expectedTurnRevision: number; readonly expectedApprovalRevision: number; readonly clientMutationId: string; }
export type ListSubagentsCommand = Omit<SubagentListParams, "schemaVersion">;
export type StartSubagentCommand = Omit<SubagentStartParams, "schemaVersion">;
export type SubagentMutationCommand = Omit<SubagentMutationParams, "schemaVersion">;
export type ResolveSubagentApprovalCommand = Omit<SubagentApprovalResolveParams, "schemaVersion">;
export interface ResumeTurnCommand { readonly threadId: string; readonly turnId: string; readonly expectedThreadRevision: number; readonly expectedTurnRevision: number; readonly checkpointId: string; readonly clientMutationId: string; }
export interface RestartTurnCommand { readonly threadId: string; readonly sourceTurnId: string; readonly expectedThreadRevision: number; readonly expectedSourceTurnRevision: number; readonly confirmed: boolean; readonly clientMutationId: string; }
export interface ContextPickResult {
  readonly schemaVersion: 1;
  readonly canceled: boolean;
  readonly result: ContextResolveResult | null;
}
export interface ContextPickCommand { readonly threadId?: string; }

export type ThemePreference = "system" | "light" | "dark";
export type ApprovalPreference = "read-only" | "on-request" | "trusted-local";
export interface DesktopLocalSettings {
  readonly language: "zh-CN" | "en-US";
  readonly theme: ThemePreference;
  readonly defaultShell: TerminalProfileId;
  readonly model: string;
  readonly approval: ApprovalPreference;
  readonly shortcuts: boolean;
  readonly summaryDefault: boolean;
  readonly bottomDefault: boolean;
  readonly toolsDefault: boolean;
  readonly gitBase: string;
  readonly navigationWidth: number;
  readonly inspectorWidth: number;
  readonly disabledTools: readonly string[];
}
export interface DesktopSettingsSnapshot {
  readonly schemaVersion: 1;
  readonly user: DesktopLocalSettings;
  readonly workspace: Partial<DesktopLocalSettings>;
}
export interface GetSettingsCommand { readonly workspaceId: string | null; }
export interface SetSettingsCommand {
  readonly scope: "user" | "workspace";
  readonly workspaceId: string | null;
  readonly value: DesktopLocalSettings | Partial<DesktopLocalSettings>;
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
  mutateChanges(command: MutateChangesCommand): Promise<ChangesMutateResult>;
  listReports(): Promise<ReportListResult>;
  getReport(command: GetReportCommand): Promise<ReportGetResult>;
  listArtifacts(): Promise<ArtifactListResult>;
  getArtifact(command: GetArtifactCommand): Promise<ArtifactGetResult>;
  openTerminal(command: OpenTerminalCommand): Promise<TerminalStateResult>;
  inputTerminal(command: InputTerminalCommand): Promise<TerminalStateResult>;
  resizeTerminal(command: ResizeTerminalCommand): Promise<TerminalStateResult>;
  cancelTerminal(command: TerminalMutationCommand): Promise<TerminalStateResult>;
  closeTerminal(command: TerminalMutationCommand): Promise<TerminalStateResult>;
  getTerminal(command: GetTerminalCommand): Promise<TerminalStateResult>;
  listTerminalProfiles(): Promise<TerminalProfileListResult>;
  previewArtifact(command: ArtifactReviewCommand): Promise<ArtifactReviewResult>;
  exportArtifact(command: ArtifactReviewCommand): Promise<ArtifactExportResult | null>;
  verifyArtifact(command: ArtifactReviewCommand): Promise<ArtifactReviewResult>;
  getGerberReview(command: GerberReviewCommand): Promise<GerberReviewResult>;
  getGerberPreview(command: GerberReviewCommand): Promise<GerberReviewResult>;
  acceptGerber(command: GerberDecisionCommand): Promise<GerberReviewResult>;
  rejectGerber(command: GerberDecisionCommand): Promise<GerberReviewResult>;
  listCatalog(command: ListCatalogCommand): Promise<CatalogListResult>;
  searchContext(command: SearchContextCommand): Promise<ContextSearchResult>;
  pickFile(command?: ContextPickCommand): Promise<ContextPickResult>;
  pickFolder(command?: ContextPickCommand): Promise<ContextPickResult>;
  getComposer(command: GetComposerCommand): Promise<ComposerStateResult>;
  enqueueComposer(command: EnqueueComposerCommand): Promise<ComposerStateResult>;
  clearComposer(command: ClearComposerCommand): Promise<ComposerStateResult>;
  startTurn(command: StartTurnCommand): Promise<TurnExecutionStateResult>;
  cancelTurn(command: CancelTurnCommand): Promise<TurnExecutionStateResult>;
  resolveApproval(command: ResolveApprovalCommand): Promise<TurnExecutionStateResult>;
  listSubagents(command: ListSubagentsCommand): Promise<SubagentResult>;
  startSubagent(command: StartSubagentCommand): Promise<SubagentResult>;
  cancelSubagent(command: SubagentMutationCommand): Promise<SubagentResult>;
  takeoverSubagent(command: SubagentMutationCommand): Promise<SubagentResult>;
  resolveSubagentApproval(command: ResolveSubagentApprovalCommand): Promise<SubagentResult>;
  resumeTurn(command: ResumeTurnCommand): Promise<TurnExecutionStateResult>;
  restartTurn(command: RestartTurnCommand): Promise<TurnExecutionStateResult>;
  getSettings(command: GetSettingsCommand): Promise<DesktopSettingsSnapshot>;
  setSettings(command: SetSettingsCommand): Promise<DesktopSettingsSnapshot>;
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

export function isMutateChangesCommand(value: unknown): value is MutateChangesCommand {
  return isRecord(value) && isChangesMutateParams({ schemaVersion: SCHEMA_VERSION, ...value });
}

export function isGetReportCommand(value: unknown): value is GetReportCommand {
  return hasExactKeys(value, ["reportId"]) && isReportGetParams({ schemaVersion: SCHEMA_VERSION, reportId: value.reportId });
}

export function isGetArtifactCommand(value: unknown): value is GetArtifactCommand {
  return hasExactKeys(value, ["artifactId"]) && isArtifactGetParams({ schemaVersion: SCHEMA_VERSION, artifactId: value.artifactId });
}

export function isOpenTerminalCommand(value: unknown): value is OpenTerminalCommand {
  return hasExactKeys(value, ["shellProfile", "clientMutationId"]) && isTerminalOpenParams({ schemaVersion: SCHEMA_VERSION, ...value });
}
export function isInputTerminalCommand(value: unknown): value is InputTerminalCommand {
  return hasExactKeys(value, ["sessionId", "text", "clientMutationId"]) && isTerminalInputParams({ schemaVersion: SCHEMA_VERSION, ...value });
}
export function isResizeTerminalCommand(value: unknown): value is ResizeTerminalCommand {
  return hasExactKeys(value, ["sessionId", "cols", "rows", "clientMutationId"]) && isTerminalResizeParams({ schemaVersion: SCHEMA_VERSION, ...value });
}
export function isTerminalMutationCommand(value: unknown): value is TerminalMutationCommand {
  return hasExactKeys(value, ["sessionId", "clientMutationId"]) && isTerminalMutationParams({ schemaVersion: SCHEMA_VERSION, ...value });
}
export function isGetTerminalCommand(value: unknown): value is GetTerminalCommand {
  return hasExactKeys(value, ["sessionId", "afterCursor"]) && isTerminalGetParams({ schemaVersion: SCHEMA_VERSION, ...value });
}
export function isArtifactReviewCommand(value: unknown): value is ArtifactReviewCommand {
  return hasExactKeys(value, ["artifactId"]) && isArtifactReviewParams({ schemaVersion: SCHEMA_VERSION, ...value });
}
export function isGerberReviewCommand(value: unknown): value is GerberReviewCommand {
  return hasExactKeys(value, ["runId"]) && isGerberReviewParams({ schemaVersion: SCHEMA_VERSION, ...value });
}
export function isGerberDecisionCommand(value: unknown): value is GerberDecisionCommand {
  return hasExactKeys(value, ["runId", "expectedRevision", "reason", "clientMutationId"]) && isGerberDecisionParams({ schemaVersion: SCHEMA_VERSION, ...value });
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
  if (!isRecord(value)) return false;
  const required = ["threadId", "expectedThreadRevision", "expectedQueueRevision", "clientMutationId", "prompt", "contextSelectionIds", "catalogSelections"];
  const allowed = [...required, "modelOverride", "approvalPreference", "disabledTools", "sourceThreadId", "sourceItemId", "sourceAction"];
  return required.every((key) => Object.hasOwn(value, key)) && Object.keys(value).every((key) => allowed.includes(key)) &&
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
export function isListSubagentsCommand(value: unknown): value is ListSubagentsCommand {
  return isRecord(value) && hasExactKeys(value, ["parentThreadId"]) && isSubagentListParams({ schemaVersion: SCHEMA_VERSION, ...value });
}
export function isStartSubagentCommand(value: unknown): value is StartSubagentCommand {
  return isRecord(value) && hasExactKeys(value, ["parentThreadId", "prompt", "mode", "confirmed", "clientMutationId"]) && isSubagentStartParams({ schemaVersion: SCHEMA_VERSION, ...value });
}
export function isSubagentMutationCommand(value: unknown): value is SubagentMutationCommand {
  return isRecord(value) && hasExactKeys(value, ["agentId", "confirmed", "clientMutationId"]) && isSubagentMutationParams({ schemaVersion: SCHEMA_VERSION, ...value });
}
export function isResolveSubagentApprovalCommand(value: unknown): value is ResolveSubagentApprovalCommand {
  return isRecord(value) && hasExactKeys(value, ["agentId", "decision", "clientMutationId"]) && isSubagentApprovalResolveParams({ schemaVersion: SCHEMA_VERSION, ...value });
}
export function isContextPickCommand(value: unknown): value is ContextPickCommand {
  return isRecord(value) && Object.keys(value).every((key) => key === "threadId") &&
    (!Object.hasOwn(value, "threadId") || isBoundedString(value.threadId, 64));
}

export function isGetSettingsCommand(value: unknown): value is GetSettingsCommand {
  return hasExactKeys(value, ["workspaceId"]) && (value.workspaceId === null || isBoundedString(value.workspaceId, 128));
}

export function isSetSettingsCommand(value: unknown): value is SetSettingsCommand {
  if (!hasExactKeys(value, ["scope", "workspaceId", "value"]) || (value.scope !== "user" && value.scope !== "workspace")) return false;
  if (value.workspaceId !== null && !isBoundedString(value.workspaceId, 128)) return false;
  return !(value.scope === "workspace" && value.workspaceId === null) && isLocalSettings(value.value, value.scope === "workspace");
}

export function isDesktopSettingsSnapshot(value: unknown): value is DesktopSettingsSnapshot {
  return hasExactKeys(value, ["schemaVersion", "user", "workspace"]) && value.schemaVersion === 1 &&
    isLocalSettings(value.user, false) && isLocalSettings(value.workspace, true);
}

export function isLocalSettings(value: unknown, partial: boolean): value is DesktopLocalSettings | Partial<DesktopLocalSettings> {
  if (!isRecord(value)) return false;
  const expected = ["language", "theme", "defaultShell", "model", "approval", "shortcuts", "summaryDefault", "bottomDefault", "toolsDefault", "gitBase", "navigationWidth", "inspectorWidth", "disabledTools"] as const;
  if (Object.keys(value).some((key) => !expected.includes(key as typeof expected[number]))) return false;
  if (!partial && Object.keys(value).length !== expected.length) return false;
  const valid = (key: typeof expected[number], predicate: (candidate: unknown) => boolean) =>
    (partial && !Object.hasOwn(value, key)) || predicate(value[key]);
  return valid("language", (item) => item === "zh-CN" || item === "en-US") &&
    valid("theme", (item) => item === "system" || item === "light" || item === "dark") &&
    valid("defaultShell", (item) => typeof item === "string" && ["system-default", "powershell", "cmd", "wsl", "git-bash"].includes(item)) &&
    valid("model", (item) => isBoundedString(item, 128)) &&
    valid("approval", (item) => item === "read-only" || item === "on-request" || item === "trusted-local") &&
    valid("shortcuts", (item) => typeof item === "boolean") && valid("summaryDefault", (item) => typeof item === "boolean") &&
    valid("bottomDefault", (item) => typeof item === "boolean") && valid("toolsDefault", (item) => typeof item === "boolean") &&
    valid("gitBase", (item) => isBoundedString(item, 200)) &&
    valid("navigationWidth", (item) => typeof item === "number" && Number.isSafeInteger(item) && item >= 180 && item <= 520) &&
    valid("inspectorWidth", (item) => typeof item === "number" && Number.isSafeInteger(item) && item >= 240 && item <= 720) &&
    valid("disabledTools", (item) => Array.isArray(item) && item.length <= 128 && item.every((entry) => isBoundedString(entry, 256)));
}

function isBoundedString(value: unknown, maxBytes: number): value is string {
  return typeof value === "string" && new TextEncoder().encode(value).length <= maxBytes;
}

function hasExactKeys(value: unknown, expected: readonly string[]): value is Record<string, unknown> {
  return isRecord(value) && Object.keys(value).length === expected.length && expected.every((key) => Object.hasOwn(value, key));
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
