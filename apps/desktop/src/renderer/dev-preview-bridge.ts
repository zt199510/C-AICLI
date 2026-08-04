import type {
  ArtifactMetadataData,
  ChangesData,
  ComposerStateData,
  GerberReviewData,
  ReportDetailData,
  ReportMetadataData,
  ThreadDetailData,
  ThreadSummaryData,
  TimelineItemData,
  WorkspaceSnapshotData,
} from "../generated/desktop-contracts";
import { createRuntimeStatus, type DesktopBridge } from "../shared/bridge-contract";

const timestamp = "2026-07-30T09:00:00.000Z";
const previewNow = new Date();

type PreviewThreadStatus =
  | "idle"
  | "running"
  | "waiting-for-approval"
  | "canceling"
  | "canceled"
  | "failed"
  | "completed"
  | "archived";

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
  // Today
  thread("thread-ui", "UI / CLI refactor", "running", 2, 6, previewThreadTimestamp(0, 2)),
  thread(
    "thread-long-chinese",
    "实现左侧聊天对话历史记录栏重构：按项目与时间分组，并补齐搜索、筛选、会话状态、右键菜单、键盘导航和窄窗口响应行为",
    "idle",
    9,
    28,
    previewThreadTimestamp(0, 12),
  ),
  thread("thread-approval", "等待批准：运行桌面端视觉回归并更新基线截图", "waiting-for-approval", 5, 19, previewThreadTimestamp(0, 24)),
  thread("thread-canceling", "正在取消：重新生成侧栏设计预览资产", "canceling", 3, 11, previewThreadTimestamp(0, 36)),

  // Yesterday
  thread("thread-contract", "Desktop contract review", "completed", 1, 3, previewThreadTimestamp(1, 0)),
  thread("thread-release", "Release readiness", "failed", 1, 4, previewThreadTimestamp(1, 1)),
  thread("thread-canceled", "取消未使用的旧导航原型构建", "canceled", 4, 14, previewThreadTimestamp(1, 2)),
  thread("thread-yesterday-idle", "整理项目分组与未归类会话的边界规则", "idle", 6, 18, previewThreadTimestamp(1, 3)),

  // Previous seven days
  thread(
    "thread-long-english",
    "Investigate an intermittently disappearing conversation selection after resizing the desktop window, switching projects, filtering archived threads, and restoring the previous workspace session",
    "completed",
    13,
    47,
    previewThreadTimestamp(3, 0),
  ),
  thread(
    "thread-long-path",
    "Review D:\\AI\\C-AICLI\\apps\\desktop\\src\\renderer\\components\\conversation-history\\ThreadSidebar.virtualized.grouping.accessibility.tsx before release",
    "running",
    7,
    31,
    previewThreadTimestamp(4, 0),
  ),
  thread("thread-recent-failed", "检查多项目会话迁移后出现的状态同步失败", "failed", 8, 23, previewThreadTimestamp(5, 0)),
  thread("thread-recent-completed", "验证 200 条会话上限与虚拟滚动边界", "completed", 11, 38, previewThreadTimestamp(6, 0)),

  // Earlier
  thread("thread-archived", "已归档：Week86 对话工作区初版视觉评审", "archived", 10, 34, previewThreadTimestamp(8, 0)),
  thread("thread-earlier-idle", "讨论桌面端账户区与同步状态的展示方式", "idle", 2, 7, previewThreadTimestamp(14, 0)),
  thread("thread-earlier-canceled", "放弃旧版双层最近记录分组方案", "canceled", 5, 16, previewThreadTimestamp(30, 0)),
  thread("thread-earlier-completed", "Initial desktop conversation navigation audit", "completed", 12, 41, previewThreadTimestamp(60, 0)),
];

const details = new Map<string, ThreadDetailData>([
  ["thread-ui", {
    thread: previewThread("thread-ui"),
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
  ["thread-contract", detailFor(previewThread("thread-contract"), "Contract boundary remains unchanged.")],
  ["thread-release", detailFor(previewThread("thread-release"), "Release verification stays bounded to five minutes.")],
]);

const changes: ChangesData = {
  status: "ready",
  exitCode: 0,
  gitStatusSummary: "M apps/desktop/src/renderer/App.tsx\nM apps/desktop/src/renderer/app/app-shell.css",
  gitStatusSucceeded: true,
  gitStatusErrorCode: null,
  dirty: true,
  diffStatSummary: "Chat-first shell · unified context workspace · C-AICLI brand retained",
  diffSucceeded: true,
  diffErrorCode: null,
  diffTruncated: false,
  changedFiles: [
    { path: "apps/desktop/src/renderer/App.tsx", status: "M" },
    { path: "apps/desktop/src/renderer/app/app-shell.css", status: "M" },
  ],
  sessionSource: "local-preview",
  sessionName: "Week89 visual checkpoint",
  warnings: ["Preview data only. No provider or product authority is active."],
};

const report: ReportMetadataData = {
  reportId: "report-week89",
  sourceKind: "turn",
  sourceId: "turn-shell",
  status: "completed",
  createdAtUtc: timestamp,
  updatedAtUtc: timestamp,
  taskReportPointer: "managed:report-week89",
  artifactPointer: "managed:artifact-week89",
};

const reportDetail: ReportDetailData = {
  metadata: report,
  summary: "The context workspace now uses one five-panel navigation model without changing protocol authority.",
  stopReason: null,
  errorCode: null,
  changedFiles: ["apps/desktop/src/renderer/WorkspaceInspector.tsx"],
  commands: ["npm run typecheck", "vitest targeted renderer tests"],
  verification: ["Five panels share one accessible tab shell", "Terminal remains an explicit user surface"],
  risks: ["Visual acceptance remains a user decision"],
  artifactPointers: ["managed:artifact-week89"],
  summaryTruncated: false,
};

const artifact: ArtifactMetadataData = {
  artifactId: "artifact-week89",
  pointerId: "pointer-week89",
  kind: "gerber-preview",
  ownership: "managed",
  relativePath: "artifacts/week89/preview.gbr",
  size: 4096,
  sha256: "a".repeat(64),
  availability: "available",
  verification: "verified",
  runState: "completed",
  declaredAtUtc: timestamp,
  updatedAtUtc: timestamp,
  owner: { runId: "run-week89", jobId: null, queueId: null, rootRunId: null, parentRunId: null, attempt: 1 },
  retention: { class: "managed", owned: true, prunable: false, defaultMinimumAgeDays: 7 },
};

const gerberReview: GerberReviewData = {
  runId: "run-week89",
  revision: 3,
  state: "awaiting-human",
  hardVerificationPassed: true,
  humanDecisionEligible: true,
  previewAvailable: true,
  correctnessProof: false,
  decision: null,
  disabledReason: null,
  verificationArtifactId: artifact.artifactId,
  previewArtifactIds: [artifact.artifactId],
};

export function installDesktopPreviewBridge() {
  const unsupported = () => Promise.reject(new Error("This action is disabled in visual preview."));
  const implemented: Partial<DesktopBridge> = {
    getRuntimeStatus: async () => createRuntimeStatus("runtime-ready"),
    restartRuntime: async () => createRuntimeStatus("runtime-ready"),
    openWorkspace: async () => result(workspace),
    getWorkspaceSnapshot: async () => workspace,
    listThreads: async () => result({ threads, truncated: false }),
    getThread: async ({ threadId }) => result(details.get(threadId) ?? details.get("thread-ui")!),
    getChanges: async () => result(changes),
    listReports: async () => result({ reports: [report], truncated: false }),
    getReport: async () => result(reportDetail),
    listArtifacts: async () => result({ artifacts: [artifact], truncated: false }),
    getArtifact: async () => result(artifact),
    previewArtifact: async () => result({
      artifactId: artifact.artifactId, kind: artifact.kind, availability: artifact.availability,
      verified: true, observedSize: artifact.size, previewAvailable: true, correctnessProof: false,
      diagnosticCode: null, safeMessage: "Preview metadata is available; correctness remains unproven.",
    }),
    verifyArtifact: async () => result({
      artifactId: artifact.artifactId, kind: artifact.kind, availability: artifact.availability,
      verified: true, observedSize: artifact.size, previewAvailable: true, correctnessProof: false,
      diagnosticCode: null, safeMessage: "Artifact identity verified.",
    }),
    exportArtifact: async () => null,
    getGerberReview: async () => result(gerberReview),
    getGerberPreview: async () => result(gerberReview),
    acceptGerber: async () => result({ ...gerberReview, revision: gerberReview.revision + 1, state: "accepted", decision: "accepted", humanDecisionEligible: false }),
    rejectGerber: async () => result({ ...gerberReview, revision: gerberReview.revision + 1, state: "rejected", decision: "rejected", humanDecisionEligible: false }),
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
  status: PreviewThreadStatus,
  turnCount: number,
  timelineItemCount: number,
  updatedAtUtc = timestamp,
): ThreadSummaryData {
  const activeTurnId = ["running", "waiting-for-approval", "canceling"].includes(status)
    ? `${threadId}-active-turn`
    : null;
  return {
    threadId,
    revision: 1,
    workspaceId: workspace.workspaceId,
    title,
    status,
    createdAtUtc: updatedAtUtc,
    updatedAtUtc,
    archivedAtUtc: status === "archived" ? updatedAtUtc : null,
    turnCount,
    timelineItemCount,
    activeTurnId,
    origin: { kind: "desktop", sourceKind: null, sourceId: null, sourceFingerprint: null },
  };
}

function previewThreadTimestamp(daysAgo: number, order: number): string {
  const value = new Date(previewNow);
  if (daysAgo === 0) {
    const startOfToday = new Date(previewNow);
    startOfToday.setHours(0, 0, 0, 0);
    value.setTime(Math.max(startOfToday.getTime(), previewNow.getTime() - order * 60_000));
  } else {
    value.setDate(value.getDate() - daysAgo);
    value.setHours(Math.max(0, 18 - order), 0, 0, 0);
  }
  return value.toISOString();
}

function previewThread(threadId: string): ThreadSummaryData {
  const summary = threads.find((candidate) => candidate.threadId === threadId);
  if (!summary) throw new Error(`Missing preview thread: ${threadId}`);
  return summary;
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
    clientMessageId: `intent-${turnId}`,
    provider: {
      phase: "streaming",
      attempt: 1,
      maxAdditionalRetries: 5,
      attemptHasStreamContent: true,
      assistantMessageId: `assistant-${turnId}`,
      errorCategory: null,
      retryable: null,
      safeErrorMessage: null,
      retryExhausted: false,
    },
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
