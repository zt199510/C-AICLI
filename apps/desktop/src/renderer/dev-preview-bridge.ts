import type {
  ChangesData,
  ComposerStateData,
  ThreadDetailData,
  ThreadSummaryData,
  TimelineItemData,
  WorkspaceSnapshotData,
} from "../generated/desktop-contracts";
import { createRuntimeStatus, type DesktopBridge } from "../shared/bridge-contract";

const timestamp = "2026-07-30T09:00:00.000Z";

const workspace: WorkspaceSnapshotData = {
  workspaceId: "preview-workspace",
  rootPath: "D:\\AI\\C-AICLI",
  status: "ready",
  capabilities: {
    readOnlyQueries: true,
    gitQueries: true,
    localCatalogs: true,
    managedArtifacts: true,
    controlledContext: true,
  },
  configuration: {
    hasApiKey: false,
    apiKeySource: "none",
    effectiveModel: "local-preview",
    modelSource: "preview",
    agentBackendSource: "preview",
    approvalMode: "ask",
    approvalModeSource: "default",
    loadedSourceCount: 0,
  },
};

const threads: readonly ThreadSummaryData[] = [
  thread("thread-ui", "UI / CLI refactor", "active", 2, 6),
  thread("thread-contract", "Desktop contract review", "completed", 1, 3),
  thread("thread-release", "Release readiness", "completed", 1, 4),
];

const details = new Map<string, ThreadDetailData>([
  ["thread-ui", {
    thread: threads[0]!,
    turns: [
      turn("turn-discovery", 1, "completed", "Align the desktop information architecture."),
      turn("turn-shell", 2, "completed", "Build the first Chat-first shell slice."),
    ],
    timeline: [
      timeline("item-1", "turn-discovery", 1, "user.message", "Bring the desktop back to a focused Chat-first workspace.", "Use OpenCowork's information hierarchy while keeping the C-AICLI identity."),
      timeline("item-2", "turn-discovery", 2, "assistant.message", "The workspace is now organized around conversation.", "Threads stay on the left; review and terminal tools move into one contextual inspector."),
      timeline("item-3", "turn-discovery", 3, "plan.updated", "Week86 shell checkpoint prepared.", "Protocol and security boundaries remain unchanged."),
      timeline("item-4", "turn-shell", 4, "command.completed", "Renderer typecheck passed.", "Completed in 3 seconds."),
      timeline("item-5", "turn-shell", 5, "command.completed", "Targeted renderer tests passed.", "7 tests passed in 17 seconds."),
      timeline("item-6", "turn-shell", 6, "assistant.message", "Ready for visual review.", "Please review the conversation focus, panel hierarchy, and C-AICLI branding."),
    ],
    nextSequence: null,
    timelineTruncated: false,
    recoveryRequired: false,
  }],
  ["thread-contract", detailFor(threads[1]!, "Contract boundary remains unchanged.")],
  ["thread-release", detailFor(threads[2]!, "Release verification stays bounded to five minutes.")],
]);

const changes: ChangesData = {
  status: "ready",
  exitCode: 0,
  gitStatusSummary: "M apps/desktop/src/renderer/App.tsx\nM apps/desktop/src/renderer/app/app-shell.css",
  gitStatusSucceeded: true,
  gitStatusErrorCode: null,
  dirty: true,
  diffStatSummary: "Chat-first shell · contextual workspace tools · C-AICLI brand retained",
  diffSucceeded: true,
  diffErrorCode: null,
  diffTruncated: false,
  changedFiles: [
    { path: "apps/desktop/src/renderer/App.tsx", status: "M" },
    { path: "apps/desktop/src/renderer/app/app-shell.css", status: "M" },
  ],
  sessionSource: "local-preview",
  sessionName: "Week86 visual checkpoint",
  warnings: ["Preview data only. No provider or product authority is active."],
};

export function installWeek86PreviewBridge() {
  const unsupported = () => Promise.reject(new Error("This action is disabled in visual preview."));
  const implemented: Partial<DesktopBridge> = {
    getRuntimeStatus: async () => createRuntimeStatus("runtime-ready"),
    restartRuntime: async () => createRuntimeStatus("runtime-ready"),
    openWorkspace: async () => result(workspace),
    getWorkspaceSnapshot: async () => workspace,
    listThreads: async () => result({ threads, truncated: false }),
    getThread: async ({ threadId }) => result(details.get(threadId) ?? details.get("thread-ui")!),
    getChanges: async () => result(changes),
    listReports: async () => result({ reports: [], truncated: false }),
    listArtifacts: async () => result({ artifacts: [], truncated: false }),
    getComposer: async ({ threadId }) => result(composer(threadId)),
    onRuntimeStatus: () => () => undefined,
    onThreadChanged: () => () => undefined,
  };
  window.caicli = new Proxy(implemented, {
    get(target, property, receiver) {
      return Reflect.get(target, property, receiver) ?? unsupported;
    },
  }) as DesktopBridge;
}

function result<T>(data: T) {
  return {
    schemaVersion: 1,
    succeeded: true,
    data,
    error: null,
    diagnostics: [],
    truncated: false,
  } as const;
}

function thread(
  threadId: string,
  title: string,
  status: string,
  turnCount: number,
  timelineItemCount: number,
): ThreadSummaryData {
  return {
    threadId,
    revision: 1,
    workspaceId: workspace.workspaceId,
    title,
    status,
    createdAtUtc: timestamp,
    updatedAtUtc: timestamp,
    archivedAtUtc: null,
    turnCount,
    timelineItemCount,
    activeTurnId: null,
    origin: { kind: "desktop", sourceKind: null, sourceId: null, sourceFingerprint: null },
  };
}

function turn(turnId: string, ordinal: number, status: string, taskSummary: string) {
  return {
    turnId,
    ordinal,
    revision: 1,
    status,
    createdAtUtc: timestamp,
    startedAtUtc: timestamp,
    completedAtUtc: timestamp,
    taskSummary,
    stopReason: null,
    errorCode: null,
    sourcePointers: [],
    timelineFirstSequence: null,
    timelineLastSequence: null,
    timelineItemCount: 3,
    recoveryRequired: false,
    approval: null,
  } as const;
}

function timeline(
  itemId: string,
  turnId: string,
  sequence: number,
  type: string,
  summary: string,
  text: string,
): TimelineItemData {
  return {
    itemId,
    turnId,
    sequence,
    timestampUtc: timestamp,
    type,
    source: null,
    status: "completed",
    summary,
    payload: {
      kind: "text",
      text,
      name: null,
      succeeded: true,
      errorCode: null,
      count: null,
      referenceId: null,
      stopReason: null,
    },
    redacted: false,
  };
}

function detailFor(summary: ThreadSummaryData, message: string): ThreadDetailData {
  return {
    thread: summary,
    turns: [turn(`${summary.threadId}-turn`, 1, summary.status, message)],
    timeline: [timeline(`${summary.threadId}-item`, `${summary.threadId}-turn`, 1, "assistant.message", message, "No authority-bearing behavior changed.")],
    nextSequence: null,
    timelineTruncated: false,
    recoveryRequired: false,
  };
}

function composer(threadId: string): ComposerStateData {
  return {
    workspaceId: workspace.workspaceId,
    threadId,
    threadRevision: 1,
    queueRevision: 1,
    pendingIntent: null,
    effectiveModel: "local-preview",
    modelSource: "preview",
    approvalMode: "ask",
    approvalModeSource: "default",
    controlledContext: true,
  };
}
