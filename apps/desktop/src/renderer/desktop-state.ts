import {
  type ArtifactMetadataData,
  type ChangesData,
  type ReportDetailData,
  type ReportMetadataData,
  type ThreadChangedParams,
  type ThreadDetailData,
  type ThreadSummaryData,
  type TimelineItemData,
  type WorkspaceSnapshotData,
} from "../generated/desktop-contracts";
import { createRuntimeStatus, type RuntimeStatus } from "../shared/bridge-contract";

export type QueryStatus = "idle" | "loading" | "ready" | "error";

export interface ReviewState {
  readonly activeTab: "changes" | "reports" | "artifacts" | "preview";
  readonly status: QueryStatus;
  readonly error: string | null;
  readonly changes: ChangesData | null;
  readonly reports: readonly ReportMetadataData[];
  readonly selectedReport: ReportDetailData | null;
  readonly artifacts: readonly ArtifactMetadataData[];
  readonly selectedArtifact: ArtifactMetadataData | null;
  readonly truncated: boolean;
}

export interface DesktopState {
  readonly runtime: RuntimeStatus;
  readonly workspace: WorkspaceSnapshotData | null;
  readonly contextEpoch: number;
  readonly selectionEpoch: number;
  readonly threadsStatus: QueryStatus;
  readonly threads: readonly ThreadSummaryData[];
  readonly threadsTruncated: boolean;
  readonly threadsError: string | null;
  readonly selectedThreadId: string | null;
  readonly detailStatus: QueryStatus;
  readonly detail: ThreadDetailData | null;
  readonly detailError: string | null;
  readonly lastEventSequence: number | null;
  readonly refreshing: boolean;
  readonly ignoredEvents: number;
  readonly review: ReviewState;
}

export const initialDesktopState: DesktopState = Object.freeze({
  runtime: createRuntimeStatus("runtime-starting"),
  workspace: null,
  contextEpoch: 0,
  selectionEpoch: 0,
  threadsStatus: "idle",
  threads: [],
  threadsTruncated: false,
  threadsError: null,
  selectedThreadId: null,
  detailStatus: "idle",
  detail: null,
  detailError: null,
  lastEventSequence: null,
  refreshing: false,
  ignoredEvents: 0,
  review: emptyReview(),
});

export type DesktopAction =
  | { type: "runtime"; status: RuntimeStatus }
  | { type: "workspace"; workspace: WorkspaceSnapshotData | null }
  | { type: "threads-loading"; epoch: number }
  | { type: "threads-ready"; epoch: number; threads: readonly ThreadSummaryData[]; truncated: boolean }
  | { type: "threads-error"; epoch: number; message: string }
  | { type: "select"; threadId: string | null }
  | { type: "detail-loading"; epoch: number; selectionEpoch: number; threadId: string }
  | { type: "detail-ready"; epoch: number; selectionEpoch: number; detail: ThreadDetailData; append: boolean }
  | { type: "detail-error"; epoch: number; selectionEpoch: number; message: string; notFound?: boolean }
  | { type: "event"; event: ThreadChangedParams }
  | { type: "refresh-complete" }
  | { type: "review-tab"; tab: ReviewState["activeTab"] }
  | { type: "review-loading"; epoch: number }
  | { type: "review-error"; epoch: number; message: string }
  | { type: "changes-ready"; epoch: number; value: ChangesData }
  | { type: "reports-ready"; epoch: number; value: readonly ReportMetadataData[]; truncated: boolean }
  | { type: "report-ready"; epoch: number; value: ReportDetailData }
  | { type: "artifacts-ready"; epoch: number; value: readonly ArtifactMetadataData[]; truncated: boolean }
  | { type: "artifact-ready"; epoch: number; value: ArtifactMetadataData };

export function desktopReducer(state: DesktopState, action: DesktopAction): DesktopState {
  switch (action.type) {
    case "runtime":
      if (action.status.state === "ready") return { ...state, runtime: action.status };
      return resetContext({ ...state, runtime: action.status }, null);
    case "workspace":
      if (action.workspace?.workspaceId === state.workspace?.workspaceId) {
        return { ...state, workspace: action.workspace };
      }
      return resetContext(state, action.workspace);
    case "threads-loading":
      return action.epoch === state.contextEpoch ? { ...state, threadsStatus: "loading", threadsError: null } : state;
    case "threads-ready":
      return action.epoch === state.contextEpoch ? {
        ...state,
        threadsStatus: "ready",
        threads: action.threads,
        threadsTruncated: action.truncated,
        threadsError: null,
      } : state;
    case "threads-error":
      return action.epoch === state.contextEpoch ? { ...state, threadsStatus: "error", threadsError: action.message } : state;
    case "select":
      if (action.threadId === state.selectedThreadId) return state;
      return {
        ...state,
        selectedThreadId: action.threadId,
        selectionEpoch: state.selectionEpoch + 1,
        detailStatus: action.threadId ? "loading" : "idle",
        detail: null,
        detailError: null,
      };
    case "detail-loading":
      return matchesDetail(state, action) ? { ...state, detailStatus: "loading", detailError: null } : state;
    case "detail-ready": {
      if (!matchesDetail(state, action) || action.detail.thread.threadId !== state.selectedThreadId) return state;
      const detail = action.append && state.detail ? mergeDetail(state.detail, action.detail) : action.detail;
      return { ...state, detailStatus: "ready", detail, detailError: null };
    }
    case "detail-error":
      if (!matchesDetail(state, action)) return state;
      return action.notFound ? {
        ...state,
        selectedThreadId: null,
        selectionEpoch: state.selectionEpoch + 1,
        detailStatus: "idle",
        detail: null,
        detailError: null,
      } : { ...state, detailStatus: "error", detailError: action.message };
    case "event": {
      if (!state.workspace || action.event.workspaceId !== state.workspace.workspaceId) {
        return { ...state, ignoredEvents: state.ignoredEvents + 1 };
      }
      if (state.lastEventSequence !== null && action.event.eventSequence <= state.lastEventSequence) {
        return { ...state, ignoredEvents: state.ignoredEvents + 1 };
      }
      return { ...state, lastEventSequence: action.event.eventSequence, refreshing: true };
    }
    case "refresh-complete": return { ...state, refreshing: false };
    case "review-tab": return { ...state, review: { ...state.review, activeTab: action.tab, status: "idle", error: null } };
    case "review-loading": return action.epoch === state.contextEpoch ? { ...state, review: { ...state.review, status: "loading", error: null } } : state;
    case "review-error": return action.epoch === state.contextEpoch ? { ...state, review: { ...state.review, status: "error", error: action.message } } : state;
    case "changes-ready": return reviewReady(state, action.epoch, { changes: action.value });
    case "reports-ready": return reviewReady(state, action.epoch, { reports: action.value, truncated: action.truncated });
    case "report-ready": return reviewReady(state, action.epoch, { selectedReport: action.value });
    case "artifacts-ready": return reviewReady(state, action.epoch, { artifacts: action.value, truncated: action.truncated });
    case "artifact-ready": return reviewReady(state, action.epoch, { selectedArtifact: action.value });
  }
}

function matchesDetail(state: DesktopState, action: { epoch: number; selectionEpoch: number }): boolean {
  return action.epoch === state.contextEpoch && action.selectionEpoch === state.selectionEpoch;
}

function resetContext(state: DesktopState, workspace: WorkspaceSnapshotData | null): DesktopState {
  return {
    ...state,
    workspace,
    contextEpoch: state.contextEpoch + 1,
    selectionEpoch: state.selectionEpoch + 1,
    threadsStatus: workspace ? "loading" : "idle",
    threads: [],
    threadsTruncated: false,
    threadsError: null,
    selectedThreadId: null,
    detailStatus: "idle",
    detail: null,
    detailError: null,
    lastEventSequence: null,
    refreshing: false,
    review: emptyReview(),
  };
}

function emptyReview(): ReviewState {
  return {
    activeTab: "changes",
    status: "idle",
    error: null,
    changes: null,
    reports: [],
    selectedReport: null,
    artifacts: [],
    selectedArtifact: null,
    truncated: false,
  };
}

function reviewReady(state: DesktopState, epoch: number, patch: Partial<ReviewState>): DesktopState {
  return epoch === state.contextEpoch ? {
    ...state,
    review: { ...state.review, status: "ready", error: null, ...patch },
  } : state;
}

export function mergeDetail(current: ThreadDetailData, incoming: ThreadDetailData): ThreadDetailData {
  const bySequence = new Map<number, TimelineItemData>();
  const byId = new Map<string, TimelineItemData>();
  let conflict = false;
  for (const item of [...current.timeline, ...incoming.timeline]) {
    const sequenceMatch = bySequence.get(item.sequence);
    const idMatch = byId.get(item.itemId);
    if ((sequenceMatch && sequenceMatch.itemId !== item.itemId) || (idMatch && idMatch.sequence !== item.sequence)) {
      conflict = true;
      continue;
    }
    bySequence.set(item.sequence, item);
    byId.set(item.itemId, item);
  }
  return {
    ...incoming,
    timeline: [...bySequence.values()].sort((left, right) => left.sequence - right.sequence),
    recoveryRequired: current.recoveryRequired || incoming.recoveryRequired || conflict,
  };
}
