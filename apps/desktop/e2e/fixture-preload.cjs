const { contextBridge, ipcRenderer } = require("electron");

const timestamp = "2026-07-17T00:00:00.000Z";
const longSession = process.env.CAICLI_E2E_SCENARIO === "long-session";
const workspace = {
  workspaceId: "fixture-workspace", rootPath: "C:\\fixture\\workspace", status: "ready",
  capabilities: { readOnlyQueries: true, gitQueries: true, localCatalogs: true, managedArtifacts: true, controlledContext: true },
  configuration: { hasApiKey: false, apiKeySource: "none", effectiveModel: "gpt-fixture", modelSource: "fixture", agentBackendSource: "fixture", approvalMode: "OnRequest", approvalModeSource: "fixture", loadedSourceCount: 0 },
};
const thread = {
  threadId: "fixture-thread", revision: 3, workspaceId: workspace.workspaceId, title: "Fixture review thread", status: "completed",
  createdAtUtc: timestamp, updatedAtUtc: timestamp, archivedAtUtc: null, turnCount: 1, timelineItemCount: longSession ? 240 : 14, activeTurnId: null,
  origin: { kind: "desktop", sourceKind: null, sourceId: null, sourceFingerprint: null },
};
const types = ["user.message", "assistant.message", "plan.updated", "tool.started", "tool.completed", "command.started", "command.completed", "approval.requested", "approval.resolved", "changes.updated", "report.available", "artifact.available", "warning.raised", "turn.completed"];
const timeline = Array.from({ length: longSession ? 240 : types.length }, (_, index) => ({
  type: types[index % types.length], index,
})).map(({ type, index }) => ({
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
const contextItem = { selectionId: "ctx_fixture", relativePath: "src/review.ts", kind: "file", byteCount: 128, fileCount: 1, availability: "available" };
const catalog = (kind) => ok({ workspaceId: workspace.workspaceId, kind, catalogRevision: "a".repeat(64), items: [{ id: `fixture-${kind}`, displayName: `Fixture ${kind}`, version: "1", description: `Fixture ${kind} capability`, sourceKind: "fixture", readOnly: true, toolBoundary: "read-only", capabilities: [] }], truncated: false });
let terminalOutput = "";
let terminalStatus = "closed";
let terminalCursor = 0;
let terminalTruncated = false;
const terminal = () => ok({ sessionId: "terminal_fixture", status: terminalStatus, shellProfile: "system-default", output: terminalOutput, cursor: terminalCursor, truncated: terminalTruncated, exitCode: terminalStatus === "exited" ? 130 : null, startedAtUtc: timestamp, exitedAtUtc: terminalStatus === "running" ? null : timestamp });
const gerber = () => ok({ runId: "fixture-run", revision: 4, state: "awaiting-acceptance", hardVerificationPassed: false, humanDecisionEligible: false, previewAvailable: true, correctnessProof: false, decision: null, disabledReason: "Fixture evidence is not a current hard verification pass.", verificationArtifactId: null, previewArtifactIds: [artifact.artifactId] });

contextBridge.exposeInMainWorld("caicli", Object.freeze({
  getRuntimeStatus: async () => ({ schemaVersion: 1, state: "ready", code: "runtime-ready", message: "AppHost ready", canRestart: false, protocolVersion: "desktop-v1" }),
  restartRuntime: async () => ({ schemaVersion: 1, state: "ready", code: "runtime-ready", message: "AppHost ready", canRestart: false, protocolVersion: "desktop-v1" }),
  openWorkspace: async () => ok(workspace),
  getWorkspaceSnapshot: async () => workspace,
  listThreads: async () => ok({ threads: [thread], truncated: false }),
  getThread: async ({ afterSequence }) => {
    const remaining = timeline.filter((item) => item.sequence > afterSequence);
    const page = longSession ? remaining.slice(0, 80) : remaining;
    const timelineTruncated = page.length < remaining.length;
    return ok({ thread, turns: [], timeline: page, nextSequence: timelineTruncated ? page.at(-1).sequence : null, timelineTruncated, recoveryRequired: false });
  },
  createThread: async () => ok(thread), renameThread: async () => ok(thread), archiveThread: async () => ok(thread),
  getChanges: async () => ({ ...ok({ status: "ready", exitCode: 0, gitStatusSummary: "M src/review.ts", gitStatusSucceeded: true, gitStatusErrorCode: null, dirty: true, diffStatSummary: longSession ? "240 files changed (bounded fixture)" : "1 file changed", diffSucceeded: true, diffErrorCode: null, diffTruncated: longSession, changedFiles: longSession ? Array.from({ length: 50 }, (_, index) => ({ path: `src/generated/file-${index + 1}.ts`, status: "M" })) : [{ path: "src/review.ts", status: "M" }], sessionSource: null, sessionName: null, warnings: longSession ? ["Diff projection was truncated at the existing bound."] : [] }), truncated: longSession }),
  listReports: async () => ok({ reports: [], truncated: false }), getReport: async () => { throw new Error("not used"); },
  listArtifacts: async () => ok({ artifacts: [artifact], truncated: false }), getArtifact: async () => ok(artifact),
  openTerminal: async () => { terminalStatus = "running"; terminalOutput = ""; terminalCursor = 0; terminalTruncated = false; return terminal(); },
  inputTerminal: async ({ text }) => {
    if (longSession && text.includes("long-output")) {
      terminalCursor = 70 * 1024;
      terminalOutput = `${"x".repeat((64 * 1024) - 32)}\nlong-output-tail-sentinel\n`;
      terminalTruncated = true;
    } else {
      const addition = text.includes("echo") ? "terminal-user-sentinel\n" : text;
      terminalOutput += addition; terminalCursor += addition.length;
    }
    return terminal();
  },
  resizeTerminal: async () => terminal(), getTerminal: async () => terminal(),
  cancelTerminal: async () => { terminalStatus = "exited"; return terminal(); },
  closeTerminal: async () => { terminalStatus = "closed"; return terminal(); },
  previewArtifact: async () => ok({ artifactId: artifact.artifactId, kind: artifact.kind, availability: "available", verified: true, observedSize: artifact.size, previewAvailable: true, correctnessProof: false, diagnosticCode: null, safeMessage: "Managed preview only; correctness is unproven." }),
  verifyArtifact: async () => ok({ artifactId: artifact.artifactId, kind: artifact.kind, availability: "available", verified: true, observedSize: artifact.size, previewAvailable: true, correctnessProof: false, diagnosticCode: null, safeMessage: "Identity verified." }),
  exportArtifact: async () => null,
  getGerberReview: async () => gerber(), getGerberPreview: async () => gerber(),
  acceptGerber: async () => { throw new Error("Preview cannot accept fixture."); }, rejectGerber: async () => { throw new Error("Fixture reject unavailable."); },
  listCatalog: async ({ kind }) => catalog(kind),
  searchContext: async () => ok({ items: [contextItem], truncated: false, scannedEntries: 1 }),
  pickFile: async () => ({ schemaVersion: 1, canceled: false, result: ok(contextItem) }),
  pickFolder: async () => ({ schemaVersion: 1, canceled: true, result: null }),
  getComposer: async (command) => ipcRenderer.invoke("fixture:composer-get", command),
  enqueueComposer: async (command) => ipcRenderer.invoke("fixture:composer-enqueue", command),
  clearComposer: async (command) => ipcRenderer.invoke("fixture:composer-clear", command),
  onRuntimeStatus: noop, onThreadChanged: noop,
}));
