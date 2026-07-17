const { contextBridge } = require("electron");

const timestamp = "2026-07-17T00:00:00.000Z";
const workspace = {
  workspaceId: "fixture-workspace", rootPath: "C:\\fixture\\workspace", status: "ready",
  capabilities: { readOnlyQueries: true, gitQueries: true, localCatalogs: true, managedArtifacts: true },
  configuration: { hasApiKey: false, apiKeySource: "none", modelSource: "fixture", agentBackendSource: "fixture", approvalMode: "ask", approvalModeSource: "fixture", loadedSourceCount: 0 },
};
const thread = {
  threadId: "fixture-thread", revision: 3, workspaceId: workspace.workspaceId, title: "Fixture review thread", status: "completed",
  createdAtUtc: timestamp, updatedAtUtc: timestamp, archivedAtUtc: null, turnCount: 1, timelineItemCount: 14, activeTurnId: null,
  origin: { kind: "desktop", sourceKind: null, sourceId: null, sourceFingerprint: null },
};
const types = ["user.message", "assistant.message", "plan.updated", "tool.started", "tool.completed", "command.started", "command.completed", "approval.requested", "approval.resolved", "changes.updated", "report.available", "artifact.available", "warning.raised", "turn.completed"];
const timeline = types.map((type, index) => ({
  itemId: `fixture-item-${index + 1}`, turnId: "fixture-turn", sequence: index + 1, timestampUtc: timestamp, type, source: null,
  status: index === 12 ? "failed" : "completed", summary: `${type} fixture summary`,
  payload: { kind: "text", text: index < 2 ? `Fixture message ${index + 1}` : null, name: null, succeeded: true, errorCode: null, count: null, referenceId: null, stopReason: null },
  redacted: index === 7,
}));
const artifact = {
  artifactId: "fixture-artifact", pointerId: "fixture-pointer", kind: "gerber-preview", ownership: "managed", relativePath: "artifacts/preview.gbr",
  size: 128, sha256: "a".repeat(64), availability: "available", verification: "verified", runState: "completed", declaredAtUtc: timestamp, updatedAtUtc: timestamp,
  owner: { runId: "fixture-run", jobId: null, queueId: null, rootRunId: null, parentRunId: null, attempt: 1 },
  retention: { class: "managed", owned: true, prunable: false, defaultMinimumAgeDays: 7 },
};
const ok = (data) => ({ schemaVersion: 1, succeeded: true, data, error: null, diagnostics: [], truncated: false });
const noop = () => () => {};

contextBridge.exposeInMainWorld("caicli", Object.freeze({
  getRuntimeStatus: async () => ({ schemaVersion: 1, state: "ready", code: "runtime-ready", message: "AppHost ready", canRestart: false, protocolVersion: "desktop-v1" }),
  restartRuntime: async () => ({ schemaVersion: 1, state: "ready", code: "runtime-ready", message: "AppHost ready", canRestart: false, protocolVersion: "desktop-v1" }),
  openWorkspace: async () => ok(workspace),
  getWorkspaceSnapshot: async () => workspace,
  listThreads: async () => ok({ threads: [thread], truncated: false }),
  getThread: async () => ok({ thread, turns: [], timeline, nextSequence: null, timelineTruncated: false, recoveryRequired: false }),
  createThread: async () => ok(thread), renameThread: async () => ok(thread), archiveThread: async () => ok(thread),
  getChanges: async () => ok({ status: "ready", exitCode: 0, gitStatusSummary: "M src/review.ts", gitStatusSucceeded: true, gitStatusErrorCode: null, dirty: true, diffStatSummary: "1 file changed", diffSucceeded: true, diffErrorCode: null, diffTruncated: false, changedFiles: [{ path: "src/review.ts", status: "M" }], sessionSource: null, sessionName: null, warnings: [] }),
  listReports: async () => ok({ reports: [], truncated: false }), getReport: async () => { throw new Error("not used"); },
  listArtifacts: async () => ok({ artifacts: [artifact], truncated: false }), getArtifact: async () => ok(artifact),
  onRuntimeStatus: noop, onThreadChanged: noop,
}));
