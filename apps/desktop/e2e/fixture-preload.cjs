const { contextBridge, ipcRenderer } = require("electron");

const timestamp = "2026-07-17T00:00:00.000Z";
const longSession = process.env.CAICLI_E2E_SCENARIO === "long-session";
const week80Profile = /^week80-c[1-7]$/.test(process.env.CAICLI_E2E_SCENARIO ?? "")
  ? process.env.CAICLI_E2E_SCENARIO
  : null;
const workspace = {
  workspaceId: "fixture-workspace", rootPath: "C:\\fixture\\workspace", status: "ready",
  capabilities: { readOnlyQueries: true, gitQueries: true, localCatalogs: true, managedArtifacts: true, controlledContext: true },
  configuration: { hasApiKey: false, apiKeySource: "none", effectiveModel: "gpt-fixture", modelSource: "fixture", agentBackendSource: "fixture", approvalMode: "OnRequest", approvalModeSource: "fixture", loadedSourceCount: 0 },
};
const types = ["user.message", "assistant.message", "plan.updated", "tool.started", "tool.completed", "command.started", "command.completed", "approval.requested", "approval.resolved", "changes.updated", "report.available", "artifact.available", "warning.raised", "turn.completed"];
let projectedTurnCount = week80Profile === "week80-c2" ||
    week80Profile === "week80-c5" ||
    week80Profile === "week80-c6" ||
    week80Profile === "week80-c7"
  ? 1
  : week80Profile === "week80-c3"
    ? 6
    : week80Profile
      ? 0
      : 1;
let projectedTimelineCount = longSession || week80Profile === "week80-c2"
  ? 240
  : week80Profile === "week80-c3"
    ? 36
    : week80Profile === "week80-c5" ||
        week80Profile === "week80-c6" ||
        week80Profile === "week80-c7"
      ? 6
      : week80Profile
        ? 0
        : 14;
let threadRevision = 3;
let eventSequence = 0;
let responseDelayMilliseconds = 0;
const threadListeners = new Set();
const diagnosticCounterNames = [
  "detailRequestsStarted", "detailRequestsCompleted", "resyncRequested", "resyncCoalesced",
  "resyncCompleted", "projectionAppended", "projectionReplaced", "ignoredStaleResponses",
];
const diagnosticCounters = Object.fromEntries(diagnosticCounterNames.map((name) => [name, 0]));
let pendingDetailRequests = 0;
let maximumPendingDetailRequests = 0;
let listThreadsCalls = 0;
let getThreadCalls = 0;

const currentThread = () => ({
  threadId: "fixture-thread", revision: threadRevision, workspaceId: workspace.workspaceId, title: "Fixture review thread", status: "completed",
  createdAtUtc: timestamp, updatedAtUtc: timestamp, archivedAtUtc: null, turnCount: projectedTurnCount, timelineItemCount: projectedTimelineCount, activeTurnId: null,
  origin: { kind: "desktop", sourceKind: null, sourceId: null, sourceFingerprint: null },
});
const currentTurns = () => Array.from({ length: projectedTurnCount }, (_, index) => {
  const itemsPerTurn = projectedTurnCount === 0 ? 0 : Math.ceil(projectedTimelineCount / projectedTurnCount);
  const first = (index * itemsPerTurn) + 1;
  const last = Math.min(projectedTimelineCount, first + itemsPerTurn - 1);
  const count = first <= projectedTimelineCount ? last - first + 1 : 0;
  return {
    turnId: `fixture-turn-${index + 1}`, ordinal: index + 1, revision: 1, status: "completed",
    createdAtUtc: timestamp, startedAtUtc: timestamp, completedAtUtc: timestamp,
    taskSummary: `Fixture turn ${index + 1}`, stopReason: "completed", errorCode: null, sourcePointers: [],
    timelineFirstSequence: count > 0 ? first : null, timelineLastSequence: count > 0 ? last : null,
    timelineItemCount: count, recoveryRequired: false, approval: null,
  };
});
const currentTimeline = () => Array.from({ length: projectedTimelineCount }, (_, index) => ({
  type: types[index % types.length], index,
})).map(({ type, index }) => ({
  itemId: `fixture-item-${index + 1}`,
  turnId: `fixture-turn-${Math.min(projectedTurnCount || 1, Math.floor(index / 6) + 1)}`,
  sequence: index + 1,
  timestampUtc: week80Profile === "week80-c7"
    ? new Date(Date.parse(timestamp) + ((index + 1) * 1000)).toISOString()
    : timestamp,
  type, source: null,
  status: index === 12 ? "failed" : "completed", summary: `${type} fixture summary`,
  payload: { kind: "text", text: index < 2 ? `Fixture message ${index + 1}` : null, name: null, succeeded: true, errorCode: null, count: null, referenceId: null, stopReason: null },
  redacted: index === 7,
}));
const responseDelay = () => responseDelayMilliseconds === 0
  ? Promise.resolve()
  : new Promise((resolve) => setTimeout(resolve, responseDelayMilliseconds));
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
  listThreads: async () => {
    listThreadsCalls++;
    await responseDelay();
    return ok({ threads: [currentThread()], truncated: false });
  },
  getThread: async ({ afterSequence }) => {
    getThreadCalls++;
    await responseDelay();
    const timeline = currentTimeline();
    const remaining = timeline.filter((item) => item.sequence > afterSequence);
    const page = longSession || week80Profile === "week80-c2" ? remaining.slice(0, 80) : remaining;
    const timelineTruncated = page.length < remaining.length;
    return ok({ thread: currentThread(), turns: currentTurns(), timeline: page, nextSequence: timelineTruncated ? page.at(-1).sequence : null, timelineTruncated, recoveryRequired: false });
  },
  createThread: async () => ok(currentThread()), renameThread: async () => ok(currentThread()), archiveThread: async () => ok(currentThread()),
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
  onRuntimeStatus: noop,
  onThreadChanged: (listener) => {
    threadListeners.add(listener);
    return () => threadListeners.delete(listener);
  },
}));

if (week80Profile) {
  contextBridge.exposeInMainWorld("caicliMemoryDiagnostics", Object.freeze({
    increment: (name) => {
      if (!diagnosticCounterNames.includes(name)) return;
      diagnosticCounters[name] = Math.min(1_000_000, diagnosticCounters[name] + 1);
      if (name === "detailRequestsStarted") {
        pendingDetailRequests = Math.min(1_000_000, pendingDetailRequests + 1);
        maximumPendingDetailRequests = Math.max(maximumPendingDetailRequests, pendingDetailRequests);
      } else if (name === "detailRequestsCompleted") {
        pendingDetailRequests = Math.max(0, pendingDetailRequests - 1);
      }
    },
    snapshot: () => ({
      ...diagnosticCounters,
      pendingDetailRequests,
      maximumPendingDetailRequests,
      listThreadsCalls,
      getThreadCalls,
      authoritativeTimelineItems: projectedTimelineCount,
      turns: projectedTurnCount,
      authoritativeProjectionJsonUtf8Bytes: Buffer.byteLength(JSON.stringify({
        thread: currentThread(),
        turns: currentTurns(),
        timeline: currentTimeline(),
      }), "utf8"),
      timelineSummaryUtf8Bytes: currentTimeline().reduce(
        (total, item) => total + Buffer.byteLength(item.summary, "utf8"), 0,
      ),
      timelinePayloadJsonUtf8Bytes: currentTimeline().reduce(
        (total, item) => total + Buffer.byteLength(JSON.stringify(item.payload), "utf8"), 0,
      ),
      distinctTimelineTimestamps: new Set(currentTimeline().map((item) => item.timestampUtc)).size,
    }),
    reset: () => {
      for (const name of diagnosticCounterNames) diagnosticCounters[name] = 0;
      pendingDetailRequests = 0;
      maximumPendingDetailRequests = 0;
      listThreadsCalls = 0;
      getThreadCalls = 0;
    },
    setProjection: ({ turns, timelineItems }) => {
      if (!Number.isSafeInteger(turns) || turns < 0 || turns > 1000) throw new Error("Invalid diagnostic turn count.");
      if (!Number.isSafeInteger(timelineItems) || timelineItems < 0 || timelineItems > 10000) throw new Error("Invalid diagnostic timeline count.");
      projectedTurnCount = turns;
      projectedTimelineCount = timelineItems;
      threadRevision++;
    },
    setResponseDelay: (milliseconds) => {
      if (!Number.isSafeInteger(milliseconds) || milliseconds < 0 || milliseconds > 250) throw new Error("Invalid diagnostic response delay.");
      responseDelayMilliseconds = milliseconds;
    },
    emitThreadChanges: (count) => {
      if (!Number.isSafeInteger(count) || count < 1 || count > 1000) throw new Error("Invalid diagnostic event count.");
      for (let index = 0; index < count; index++) {
        eventSequence++;
        const event = {
          schemaVersion: 1, eventSequence, workspaceId: workspace.workspaceId, threadId: "fixture-thread",
          revision: threadRevision, committedSequence: projectedTimelineCount, changeKind: "updated", emittedAtUtc: timestamp,
        };
        for (const listener of threadListeners) listener(event);
      }
    },
  }));
}
