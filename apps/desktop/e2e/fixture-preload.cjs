const { contextBridge, ipcRenderer } = require("electron");

const timestamp = "2026-07-17T00:00:00.000Z";
let chatUiFixture = process.env.CAICLI_CHAT_UI_FIXTURE ?? null;
const longSession = process.env.CAICLI_E2E_SCENARIO === "long-session";
const week80Profile = /^week80-c[1-8]$/.test(process.env.CAICLI_E2E_SCENARIO ?? "")
  ? process.env.CAICLI_E2E_SCENARIO
  : null;
const workspace = {
  workspaceId: "fixture-workspace", rootPath: "C:\\fixture\\workspace", status: "ready",
  capabilities: { readOnlyQueries: true, gitQueries: true, localCatalogs: true, managedArtifacts: true, controlledContext: true },
  configuration: { hasApiKey: false, apiKeySource: "none", effectiveModel: "gpt-fixture", modelSource: "fixture", agentBackendSource: "fixture", approvalMode: "OnRequest", approvalModeSource: "fixture", loadedSourceCount: 0 },
};
const types = ["user.message", "assistant.message", "plan.updated", "tool.started", "tool.completed", "command.started", "command.completed", "approval.requested", "approval.resolved", "changes.updated", "report.available", "artifact.available", "warning.raised", "turn.completed"];
const runtimeListeners = new Set();
const chatFixtureConfig = () => {
  const base = {
    threadStatus: "completed",
    turnStatus: "completed",
    phase: "streaming",
    attempt: 1,
    retryExhausted: false,
    recoveryRequired: false,
    completedAtUtc: "2026-07-17T00:00:08.400Z",
    approval: null,
  };
  if (!chatUiFixture || chatUiFixture === "completed" || chatUiFixture === "selected-thread" || chatUiFixture === "review-terminal") return base;
  if (chatUiFixture === "new" || chatUiFixture === "open-workspace") return { ...base, empty: true };
  if (chatUiFixture === "thinking") return { ...base, threadStatus: "running", turnStatus: "running", phase: "thinking", completedAtUtc: null };
  if (chatUiFixture === "streaming") return { ...base, threadStatus: "running", turnStatus: "running", phase: "streaming", completedAtUtc: null };
  if (chatUiFixture === "retry-wait") return { ...base, threadStatus: "running", turnStatus: "running", phase: "retry-wait", attempt: 2, completedAtUtc: null };
  if (chatUiFixture === "retry-exhausted") return { ...base, threadStatus: "failed", turnStatus: "failed", phase: "failed", attempt: 6, retryExhausted: true, completedAtUtc: "2026-07-17T00:00:12.600Z" };
  if (chatUiFixture === "approval" || chatUiFixture === "waiting-approval") return {
    ...base,
    threadStatus: "waiting-for-approval",
    turnStatus: "waiting-for-approval",
    phase: "thinking",
    completedAtUtc: null,
    approval: {
      requestId: "fixture-approval",
      workspaceId: workspace.workspaceId,
      threadId: "fixture-thread",
      turnId: "fixture-turn-1",
      turnRevision: 4,
      approvalRevision: 2,
      policyIdentity: "fixture-policy",
      policyRevision: "fixture-revision",
      risk: "write",
      operation: "npm install",
      targetClass: "workspace",
      safeSummary: "Install the workspace dependencies required by this task.",
      createdAtUtc: "2026-07-17T00:00:02.000Z",
      expiresAtUtc: "2026-07-17T00:05:02.000Z",
    },
  };
  if (chatUiFixture === "canceled") return { ...base, threadStatus: "canceled", turnStatus: "canceled", completedAtUtc: "2026-07-17T00:00:03.200Z" };
  if (chatUiFixture === "activity-expanded") return { ...base, threadStatus: "running", turnStatus: "running", phase: "thinking", completedAtUtc: null };
  return base;
};
let projectedTurnCount = week80Profile === "week80-c2" ||
    week80Profile === "week80-c5" ||
    week80Profile === "week80-c6" ||
    week80Profile === "week80-c7" ||
    week80Profile === "week80-c8"
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
        week80Profile === "week80-c7" ||
        week80Profile === "week80-c8"
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

const currentThread = () => {
  const chat = chatFixtureConfig();
  const chatMode = Boolean(chatUiFixture);
  const turnCount = chatMode ? (chat.empty ? 0 : 1) : projectedTurnCount;
  const timelineItemCount = chatMode ? currentTimeline().length : projectedTimelineCount;
  return {
  threadId: "fixture-thread", revision: threadRevision, workspaceId: workspace.workspaceId, title: chatMode ? "Chat-first renderer delivery" : "Fixture review thread", status: chatMode ? (chat.empty ? "active" : chat.threadStatus) : "completed",
  createdAtUtc: timestamp, updatedAtUtc: chat.completedAtUtc ?? timestamp, archivedAtUtc: null, turnCount, timelineItemCount,
  activeTurnId: chatMode && !chat.empty && !["completed", "failed", "canceled"].includes(chat.turnStatus) ? "fixture-turn-1" : null,
  origin: { kind: "desktop", sourceKind: null, sourceId: null, sourceFingerprint: null },
  };
};
const currentTurns = () => {
  const chat = chatFixtureConfig();
  if (chatUiFixture) {
    if (chat.empty) return [];
    const timeline = currentTimeline();
    return [{
      turnId: "fixture-turn-1", ordinal: 1, revision: 4, status: chat.turnStatus,
      createdAtUtc: timestamp, startedAtUtc: "2026-07-17T00:00:00.100Z", completedAtUtc: chat.completedAtUtc,
      taskSummary: "Implement the confirmed Chat-first conversation UI plan.",
      stopReason: chat.turnStatus, errorCode: chat.turnStatus === "failed" ? "provider-transport-error" : null,
      sourcePointers: [], timelineFirstSequence: timeline.length ? 1 : null,
      timelineLastSequence: timeline.length ? timeline.length : null, timelineItemCount: timeline.length,
      recoveryRequired: chat.recoveryRequired, approval: chat.approval,
      clientMessageId: "fixture-intent-1",
      provider: {
        phase: chat.phase, attempt: chat.attempt, maxAdditionalRetries: 5,
        attemptHasStreamContent: chat.phase === "streaming" || chat.phase === "retry-wait",
        assistantMessageId: "fixture-assistant-1",
        errorCategory: chat.turnStatus === "failed" ? "transport" : null,
        retryable: chat.turnStatus === "failed" ? true : null,
        safeErrorMessage: chat.turnStatus === "failed" ? "The model connection could not be established." : null,
        retryExhausted: chat.retryExhausted,
      },
    }];
  }
  return Array.from({ length: projectedTurnCount }, (_, index) => {
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
    clientMessageId: `fixture-intent-${index + 1}`,
    provider: {
      phase: "streaming", attempt: 1, maxAdditionalRetries: 5,
      attemptHasStreamContent: true, assistantMessageId: `fixture-assistant-${index + 1}`,
      errorCategory: null, retryable: null, safeErrorMessage: null, retryExhausted: false,
    },
  };
  });
};
const currentTimeline = () => {
  if (chatUiFixture) return chatTimeline(chatUiFixture, chatFixtureConfig());
  return Array.from({ length: projectedTimelineCount }, (_, index) => ({
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
};
function chatTimeline(fixture, config) {
  if (config.empty) return [];
  const entries = [];
  const add = (type, summary, payload = {}, status = "completed") => {
    const sequence = entries.length + 1;
    entries.push({
      itemId: `chat-fixture-item-${sequence}`,
      turnId: "fixture-turn-1",
      sequence,
      timestampUtc: new Date(Date.parse(timestamp) + (sequence * 1000)).toISOString(),
      type,
      source: null,
      status,
      summary,
      payload: {
        kind: type,
        text: null,
        name: null,
        succeeded: null,
        errorCode: null,
        count: null,
        referenceId: null,
        stopReason: null,
        phase: null,
        attempt: null,
        maxAdditionalRetries: null,
        attemptHasStreamContent: null,
        assistantMessageId: null,
        errorCategory: null,
        retryable: null,
        safeErrorMessage: null,
        retryExhausted: null,
        ...payload,
      },
      redacted: false,
    });
  };
  add("user.message", "Please implement the confirmed Chat-first conversation UI plan.", {
    text: "请按已确认的计划实现 Chat-first 对话 UI，并保留 AppHost authority 与副作用防重放。",
  });
  if (fixture === "thinking") return entries;
  if (fixture === "streaming") {
    add("assistant.message", "Streaming response", {
      text: "我正在把同一 Turn 的连接、思考、流式内容和终态收敛到一个稳定的 assistant message。\n\n```tsx\n<AssistantMessage identity={assistantMessageId} />\n```\n\n长内容和代码只在对话列内部换行或滚动，不会产生页面横向溢出。",
      attempt: 1,
      assistantMessageId: "fixture-assistant-1",
    }, "streaming");
    return entries;
  }
  if (fixture === "retry-wait") {
    add("assistant.message", "Previous attempt partial", {
      text: "上一 attempt 的部分内容暂时保留；新 attempt 首段到达后会在这里原位替换。",
      attempt: 1,
      assistantMessageId: "fixture-assistant-1",
    }, "interrupted");
    return entries;
  }
  if (fixture === "retry-exhausted") {
    add("assistant.message", "Last attempt partial", {
      text: "消息和上下文已经保留，不同 provider attempt 的内容没有拼接。",
      attempt: 6,
      assistantMessageId: "fixture-assistant-1",
    }, "failed");
    add("warning.raised", "The provider retry limit was reached.", { errorCode: "provider-transport-error" }, "failed");
    return entries;
  }
  if (fixture === "approval" || fixture === "waiting-approval") {
    add("assistant.message", "Approval context", {
      text: "依赖安装需要显式批准；批准参数只来自 AppHost 的权威审批状态。",
      attempt: 1,
      assistantMessageId: "fixture-assistant-1",
    });
    add("tool.started", "准备运行 npm install", { name: "workspace.run_shell" }, "waiting-for-approval");
    add("approval.requested", "Install the workspace dependencies required by this task.", { text: "active" }, "active");
    return entries;
  }
  if (fixture === "canceled") {
    add("assistant.message", "Canceled partial", {
      text: "停止前已经生成的内容会保留，尚未开始的自动重试已经取消。",
      attempt: 1,
      assistantMessageId: "fixture-assistant-1",
    });
    return entries;
  }
  if (fixture === "activity-expanded") {
    add("assistant.message", "Activity response", {
      text: "当前工具活动保持紧凑，但 running 项会自动展开必要摘要。",
      attempt: 1,
      assistantMessageId: "fixture-assistant-1",
    });
    add("tool.started", "正在运行 Renderer state-matrix tests", { name: "test.renderer" }, "running");
    return entries;
  }
  add("assistant.message", "Completed response prefix", {
    text: "Chat-first 主对话已经收敛为用户消息、AI 正文和必要的内联操作。",
    attempt: 1,
    assistantMessageId: "fixture-assistant-1",
  });
  add("tool.started", "读取 Renderer 基线", { name: "workspace.read" }, "running");
  add("tool.completed", "已读取 Renderer 基线", { name: "workspace.read", succeeded: true });
  add("command.started", "运行 Renderer tests", { name: "npm test" }, "running");
  add("command.completed", "Renderer tests passed", { name: "npm test", succeeded: true });
  add("assistant.final", "Completed response", {
    text: "Chat-first 主对话已经完成：assistant identity 稳定、活动默认折叠、Composer 位于底部 slot，并保留完整 Activity/Audit 入口。",
    attempt: 1,
    assistantMessageId: "fixture-assistant-1",
  });
  return entries;
}
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
const currentRuntimeStatus = () => chatUiFixture === "runtime-unavailable"
  ? { schemaVersion: 1, state: "failed", code: "apphost-exited", message: "AppHost stopped unexpectedly", canRestart: true, protocolVersion: null }
  : { schemaVersion: 1, state: "ready", code: "runtime-ready", message: "AppHost ready", canRestart: false, protocolVersion: "desktop-v1" };

contextBridge.exposeInMainWorld("caicli", Object.freeze({
  getRuntimeStatus: async () => currentRuntimeStatus(),
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
  onRuntimeStatus: (listener) => {
    runtimeListeners.add(listener);
    return () => runtimeListeners.delete(listener);
  },
  onThreadChanged: (listener) => {
    threadListeners.add(listener);
    return () => threadListeners.delete(listener);
  },
}));

contextBridge.exposeInMainWorld("caicliVisualFixtures", Object.freeze({
  setFixture: (fixture) => {
    if (typeof fixture !== "string" || !/^[a-z-]+$/.test(fixture)) throw new Error("Invalid visual fixture.");
    chatUiFixture = fixture;
    threadRevision++;
    const runtime = currentRuntimeStatus();
    for (const listener of runtimeListeners) listener(runtime);
    eventSequence++;
    const event = {
      schemaVersion: 1,
      eventSequence,
      workspaceId: workspace.workspaceId,
      threadId: "fixture-thread",
      revision: threadRevision,
      committedSequence: currentTimeline().length,
      changeKind: "updated",
      emittedAtUtc: timestamp,
    };
    for (const listener of threadListeners) listener(event);
    return { fixture, runtime: runtime.state };
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
