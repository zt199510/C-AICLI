import { useCallback, useEffect, useReducer, useRef } from "react";
import type { CatalogItemData, ContextDescriptorData, ThreadChangedParams, WorkspaceSnapshotData } from "../generated/desktop-contracts";
import { createRuntimeStatus, type DesktopBridge } from "../shared/bridge-contract";
import { desktopReducer, initialDesktopState, threadEventIdentity, type ReviewState } from "./desktop-state";
import { composerReducer, currentDraft, draftKey, initialComposerUiState, type ComposerCatalogKind, type SelectedCatalogItem } from "./composer-state";
import { incrementMemoryDiagnostic } from "./memory-diagnostics";

export function shouldQueueThreadResync(previous: ThreadChangedParams | null, event: ThreadChangedParams): boolean {
  if (!previous || previous.workspaceId !== event.workspaceId || previous.threadId !== event.threadId) return true;
  if (previous.eventSequence === event.eventSequence && threadEventIdentity(previous) === threadEventIdentity(event)) return false;
  if (event.changeKind !== "updated") return true;
  if (event.revision < previous.revision && event.committedSequence <= previous.committedSequence) return false;
  return previous.committedSequence === event.committedSequence;
}

export function useDesktopController(bridge: DesktopBridge | undefined) {
  const [state, dispatch] = useReducer(desktopReducer, initialDesktopState);
  const [composer, dispatchComposer] = useReducer(composerReducer, initialComposerUiState);
  const stateRef = useRef(state);
  stateRef.current = state;
  const composerRef = useRef(composer);
  composerRef.current = composer;
  const mentionRequest = useRef(0);
  const composerWorkspace = useRef<string | null>(null);
  const detailRequest = useRef(0);
  const resyncRunning = useRef(false);
  const resyncDirty = useRef(false);
  const lastThreadEvent = useRef<ThreadChangedParams | null>(null);

  const refreshThreads = useCallback(async (epoch = stateRef.current.contextEpoch) => {
    if (!bridge) return;
    dispatch({ type: "threads-loading", epoch });
    try {
      const result = await bridge.listThreads();
      if (!result.succeeded || !result.data) {
        dispatch({ type: "threads-error", epoch, message: safeFailure(result.error?.safeMessage) });
        return;
      }
      const corrupt = result.diagnostics.find((diagnostic) => diagnostic.category === "corrupt-state");
      dispatch({
        type: "threads-ready",
        epoch,
        threads: result.data.threads,
        truncated: result.data.truncated || result.truncated,
        warning: corrupt ? `Corrupt state was isolated: ${safeFailure(corrupt.safeMessage)}` : null,
      });
    } catch {
      dispatch({ type: "threads-error", epoch, message: "Threads could not be loaded." });
    }
  }, [bridge]);

  const fetchThread = useCallback(async (
    threadId: string,
    afterSequence: number,
    append: boolean,
    epoch = stateRef.current.contextEpoch,
    selectionEpoch = stateRef.current.selectionEpoch,
  ) => {
    if (!bridge) return;
    const requestId = ++detailRequest.current;
    incrementMemoryDiagnostic("detailRequestsStarted");
    dispatch({ type: "detail-loading", epoch, selectionEpoch, requestId, threadId });
    try {
      const result = await bridge.getThread({ threadId, afterSequence });
      if (!result.succeeded || !result.data) {
        dispatch({
          type: "detail-error",
          epoch,
          selectionEpoch,
          requestId,
          message: safeFailure(result.error?.safeMessage),
          notFound: result.error?.code === "thread-not-found",
        });
        return;
      }
      const current = stateRef.current;
      const latestEvent = lastThreadEvent.current;
      const behindNotification = !append && afterSequence === 0 &&
          latestEvent?.workspaceId === result.data.thread.workspaceId &&
          latestEvent.threadId === result.data.thread.threadId &&
          result.data.thread.revision < latestEvent.revision;
      const stale = requestId !== detailRequest.current ||
          current.contextEpoch !== epoch ||
          current.selectionEpoch !== selectionEpoch ||
          current.selectedThreadId !== threadId ||
          behindNotification;
      if (stale) {
        incrementMemoryDiagnostic("ignoredStaleResponses");
        if (behindNotification && resyncRunning.current) resyncDirty.current = true;
      } else {
        incrementMemoryDiagnostic(append ? "projectionAppended" : "projectionReplaced");
        dispatch({ type: "detail-ready", epoch, selectionEpoch, requestId, detail: result.data, append });
      }
    } catch {
      dispatch({ type: "detail-error", epoch, selectionEpoch, requestId, message: "Thread history could not be loaded." });
    } finally {
      incrementMemoryDiagnostic("detailRequestsCompleted");
    }
  }, [bridge]);

  const adoptWorkspace = useCallback((workspace: WorkspaceSnapshotData | null) => {
    const current = stateRef.current;
    const changed = workspace?.workspaceId !== current.workspace?.workspaceId;
    const epoch = current.contextEpoch + (changed ? 1 : 0);
    dispatch({ type: "workspace", workspace });
    if (workspace) void refreshThreads(epoch);
  }, [refreshThreads]);

  const queueResync = useCallback(() => {
    incrementMemoryDiagnostic("resyncRequested");
    if (resyncRunning.current) {
      incrementMemoryDiagnostic("resyncCoalesced");
      resyncDirty.current = true;
      return;
    }
    resyncRunning.current = true;
    void (async () => {
      do {
        resyncDirty.current = false;
        const snapshot = stateRef.current;
        await refreshThreads(snapshot.contextEpoch);
        if (snapshot.selectedThreadId) {
          await fetchThread(snapshot.selectedThreadId, 0, false, snapshot.contextEpoch, snapshot.selectionEpoch);
        }
      } while (resyncDirty.current);
      dispatch({ type: "refresh-complete" });
      resyncRunning.current = false;
      incrementMemoryDiagnostic("resyncCompleted");
    })();
  }, [fetchThread, refreshThreads]);

  useEffect(() => {
    if (!bridge) {
      dispatch({ type: "runtime", status: createRuntimeStatus("apphost-start-failed") });
      return;
    }
    let disposed = false;
    let receivedRuntimeEvent = false;
    const handleReady = async () => {
      try {
        const snapshot = await bridge.getWorkspaceSnapshot();
        if (!disposed) adoptWorkspace(snapshot);
      } catch {
        if (!disposed) adoptWorkspace(null);
      }
    };
    const unsubscribeRuntime = bridge.onRuntimeStatus((status) => {
      receivedRuntimeEvent = true;
      dispatch({ type: "runtime", status });
      if (status.state === "ready") void handleReady();
    });
    const unsubscribeThread = bridge.onThreadChanged((event: ThreadChangedParams) => {
      const snapshot = stateRef.current;
      const shouldResync = shouldQueueThreadResync(lastThreadEvent.current, event);
      const eventTargetsSelection = snapshot.selectedThreadId === event.threadId;
      const selectedDetail = eventTargetsSelection && snapshot.detail?.thread.threadId === event.threadId
        ? snapshot.detail
        : null;
      const committedLag = selectedDetail
        ? Math.max(0, event.committedSequence - selectedDetail.thread.timelineItemCount)
        : 0;
      const selectedProjectionBehind = selectedDetail !== null &&
        (selectedDetail.thread.revision < event.revision || committedLag > 0);
      const boundedCatchupRequired = selectedDetail !== null && committedLag >= 3;
      lastThreadEvent.current = event;
      if (!snapshot.workspace || event.workspaceId !== snapshot.workspace.workspaceId) {
        dispatch({ type: "event", event });
        return;
      }
      dispatch({ type: "event", event });
      if (shouldResync) {
        if (!eventTargetsSelection || selectedDetail === null || selectedProjectionBehind) queueResync();
      } else if (boundedCatchupRequired) {
        if (resyncRunning.current) resyncDirty.current = true;
        else queueResync();
      }
    });
    void bridge.getRuntimeStatus().then((status) => {
      if (disposed || receivedRuntimeEvent) return;
      dispatch({ type: "runtime", status });
      if (status.state === "ready") void handleReady();
    }).catch(() => {
      if (!disposed) dispatch({ type: "runtime", status: createRuntimeStatus("apphost-start-failed") });
    });
    return () => { disposed = true; unsubscribeThread(); unsubscribeRuntime(); };
  }, [adoptWorkspace, bridge, queueResync]);

  useEffect(() => {
    const workspaceId = state.workspace?.workspaceId ?? null;
    if (workspaceId !== composerWorkspace.current) {
      composerWorkspace.current = workspaceId;
      mentionRequest.current++;
      dispatchComposer({ type: "reset" });
    }
    if (!bridge || !workspaceId || !state.selectedThreadId || state.runtime.state !== "ready") return;
    const threadId = state.selectedThreadId;
    const epoch = state.contextEpoch;
    const selectionEpoch = state.selectionEpoch;
    dispatchComposer({ type: "snapshot-loading" });
    void bridge.getComposer({ threadId }).then((result) => {
      const current = stateRef.current;
      if (current.contextEpoch !== epoch || current.selectionEpoch !== selectionEpoch || current.selectedThreadId !== threadId) return;
      if (result.succeeded && result.data) dispatchComposer({ type: "snapshot", snapshot: result.data });
      else dispatchComposer({ type: "snapshot-error" });
    }).catch(() => dispatchComposer({ type: "snapshot-error" }));
  }, [bridge, state.contextEpoch, state.runtime.state, state.selectedThreadId, state.selectionEpoch, state.workspace?.workspaceId]);

  const selectThread = useCallback((threadId: string) => {
    const current = stateRef.current;
    const selectionEpoch = current.selectionEpoch + (current.selectedThreadId === threadId ? 0 : 1);
    dispatch({ type: "select", threadId });
    void fetchThread(threadId, 0, false, current.contextEpoch, selectionEpoch);
  }, [fetchThread]);

  const loadMore = useCallback(() => {
    const current = stateRef.current;
    const nextSequence = current.detail?.nextSequence;
    if (!current.selectedThreadId || nextSequence === null || nextSequence === undefined || current.detailStatus === "loading") return;
    void fetchThread(current.selectedThreadId, nextSequence, true);
  }, [fetchThread]);

  const openWorkspace = useCallback(async () => {
    if (!bridge || stateRef.current.runtime.state !== "ready") return null;
    const result = await bridge.openWorkspace();
    if (result?.succeeded && result.data) adoptWorkspace(result.data);
    return result;
  }, [adoptWorkspace, bridge]);

  const restartRuntime = useCallback(async () => {
    if (!bridge) return;
    dispatch({ type: "runtime", status: createRuntimeStatus("runtime-restarting") });
    try { dispatch({ type: "runtime", status: await bridge.restartRuntime() }); }
    catch { dispatch({ type: "runtime", status: createRuntimeStatus("restart-failed") }); }
  }, [bridge]);

  const mutate = useCallback(async (operation: () => ReturnType<DesktopBridge["createThread"]>) => {
    try {
      const result = await operation();
      if (!result.succeeded) {
        queueResync();
        return safeFailure(result.error?.safeMessage);
      }
      await refreshThreads();
      if (result.data) selectThread(result.data.threadId);
      return null;
    } catch { return "Thread metadata could not be updated."; }
  }, [queueResync, refreshThreads, selectThread]);

  const setReviewTab = useCallback((tab: ReviewState["activeTab"]) => {
    const current = stateRef.current;
    if (current.workspace && current.review.activeTab === tab && current.review.status === "ready") return;
    dispatch({ type: "review-tab", tab });
    const epoch = current.contextEpoch;
    if (!bridge || !current.workspace) return;
    dispatch({ type: "review-loading", epoch });
    const failed = () => dispatch({ type: "review-error", epoch, message: "Review data could not be loaded." });
    if (tab === "changes") void bridge.getChanges({}).then((result) => {
      if (result.succeeded && result.data) dispatch({ type: "changes-ready", epoch, value: result.data, truncated: result.data.diffTruncated || result.truncated });
      else failed();
    }).catch(failed);
    else if (tab === "reports") void bridge.listReports().then((result) => {
      if (result.succeeded && result.data) dispatch({ type: "reports-ready", epoch, value: result.data.reports, truncated: result.data.truncated || result.truncated });
      else failed();
    }).catch(failed);
    else void bridge.listArtifacts().then((result) => {
      if (result.succeeded && result.data) dispatch({ type: "artifacts-ready", epoch, value: result.data.artifacts, truncated: result.data.truncated || result.truncated });
      else failed();
    }).catch(failed);
  }, [bridge]);

  const selectReport = useCallback(async (reportId: string) => {
    if (!bridge) return;
    const epoch = stateRef.current.contextEpoch;
    dispatch({ type: "review-loading", epoch });
    try {
      const result = await bridge.getReport({ reportId });
      if (result.succeeded && result.data) dispatch({ type: "report-ready", epoch, value: result.data });
      else dispatch({ type: "review-error", epoch, message: safeFailure(result.error?.safeMessage) });
    } catch { dispatch({ type: "review-error", epoch, message: "Report could not be loaded." }); }
  }, [bridge]);

  const selectArtifact = useCallback(async (artifactId: string) => {
    if (!bridge) return;
    const epoch = stateRef.current.contextEpoch;
    dispatch({ type: "review-loading", epoch });
    try {
      const result = await bridge.getArtifact({ artifactId });
      if (result.succeeded && result.data) dispatch({ type: "artifact-ready", epoch, value: result.data });
      else dispatch({ type: "review-error", epoch, message: safeFailure(result.error?.safeMessage) });
    } catch { dispatch({ type: "review-error", epoch, message: "Artifact could not be loaded." }); }
  }, [bridge]);

  const previewArtifact = useCallback(async (artifactId: string) => {
    if (!bridge) throw new Error("Desktop bridge unavailable.");
    const result = await bridge.previewArtifact({ artifactId });
    if (!result.succeeded || !result.data) throw new Error(safeFailure(result.error?.safeMessage));
    return result.data.safeMessage;
  }, [bridge]);

  const verifyArtifact = useCallback(async (artifactId: string) => {
    if (!bridge) throw new Error("Desktop bridge unavailable.");
    const result = await bridge.verifyArtifact({ artifactId });
    if (!result.succeeded || !result.data) throw new Error(safeFailure(result.error?.safeMessage));
    return result.data.safeMessage;
  }, [bridge]);

  const exportArtifact = useCallback(async (artifactId: string) => {
    if (!bridge) throw new Error("Desktop bridge unavailable.");
    const result = await bridge.exportArtifact({ artifactId });
    if (result === null) return "Export canceled.";
    if (!result.succeeded || !result.data) throw new Error(safeFailure(result.error?.safeMessage));
    return `Exported ${result.data.fileName}.`;
  }, [bridge]);

  const loadGerber = useCallback(async (runId: string, preview: boolean) => {
    if (!bridge) throw new Error("Desktop bridge unavailable.");
    const result = preview
      ? await bridge.getGerberPreview({ runId })
      : await bridge.getGerberReview({ runId });
    if (!result.succeeded || !result.data) throw new Error(safeFailure(result.error?.safeMessage));
    return result.data;
  }, [bridge]);

  const decideGerber = useCallback(async (runId: string, expectedRevision: number, reason: string, accept: boolean) => {
    if (!bridge) throw new Error("Desktop bridge unavailable.");
    const command = { runId, expectedRevision, reason, clientMutationId: `gerber-${Date.now()}` };
    const result = accept ? await bridge.acceptGerber(command) : await bridge.rejectGerber(command);
    if (!result.succeeded || !result.data) throw new Error(safeFailure(result.error?.safeMessage));
    return result.data;
  }, [bridge]);

  const activeDraftKey = state.workspace && state.selectedThreadId
    ? draftKey(state.workspace.workspaceId, state.selectedThreadId)
    : null;

  const searchMentions = useCallback(async (query: string) => {
    if (!bridge) return;
    const snapshot = stateRef.current;
    if (!snapshot.workspace || !snapshot.selectedThreadId) return;
    const requestId = ++mentionRequest.current;
    dispatchComposer({ type: "mentions-loading" });
    try {
      const [context, skills, experts, automations] = await Promise.all([
        bridge.searchContext({ query }),
        bridge.listCatalog({ kind: "skills" }),
        bridge.listCatalog({ kind: "experts" }),
        bridge.listCatalog({ kind: "automations" }),
      ]);
      const current = stateRef.current;
      if (requestId !== mentionRequest.current || current.contextEpoch !== snapshot.contextEpoch || current.selectionEpoch !== snapshot.selectionEpoch) return;
      if (!context.succeeded || !context.data || !skills.succeeded || !skills.data || !experts.succeeded || !experts.data || !automations.succeeded || !automations.data) {
        dispatchComposer({ type: "mentions", value: { loading: false, error: "Mentions could not be loaded.", truncated: false, context: [], skills: [], experts: [], automations: [], revisions: { skills: "", experts: "", automations: "" } } });
        return;
      }
      const filter = (items: readonly CatalogItemData[]) => !query ? items : items.filter(item => `${item.displayName} ${item.description}`.toLowerCase().includes(query.toLowerCase()));
      dispatchComposer({ type: "mentions", value: {
        loading: false, error: null,
        truncated: context.data.truncated || context.truncated || skills.data.truncated || experts.data.truncated || automations.data.truncated,
        context: context.data.items, skills: filter(skills.data.items), experts: filter(experts.data.items), automations: filter(automations.data.items),
        revisions: { skills: skills.data.catalogRevision, experts: experts.data.catalogRevision, automations: automations.data.catalogRevision },
      } });
    } catch {
      if (requestId === mentionRequest.current) dispatchComposer({ type: "mentions", value: { loading: false, error: "Mentions could not be loaded.", truncated: false, context: [], skills: [], experts: [], automations: [], revisions: { skills: "", experts: "", automations: "" } } });
    }
  }, [bridge]);

  const addContext = useCallback((item: ContextDescriptorData) => {
    const current = stateRef.current;
    if (!current.workspace || !current.selectedThreadId) return;
    const key = draftKey(current.workspace.workspaceId, current.selectedThreadId);
    dispatchComposer({ type: "add-context", key, item });
    dispatchComposer({ type: "text", key, text: (composerRef.current.drafts[key]?.text ?? "").replace(/(?:^|\s)@[^\s@]*$/u, " ").trimStart() });
    dispatchComposer({ type: "mentions-close" });
  }, []);

  const addCatalog = useCallback((kind: ComposerCatalogKind, item: CatalogItemData, revision: string) => {
    const current = stateRef.current;
    if (!current.workspace || !current.selectedThreadId || !revision) return;
    const key = draftKey(current.workspace.workspaceId, current.selectedThreadId);
    dispatchComposer({ type: "add-catalog", key, item: { kind, id: item.id, label: item.displayName, catalogRevision: revision } });
    dispatchComposer({ type: "text", key, text: (composerRef.current.drafts[key]?.text ?? "").replace(/(?:^|\s)@[^\s@]*$/u, " ").trimStart() });
    dispatchComposer({ type: "mentions-close" });
  }, []);

  const pickContext = useCallback(async (kind: "file" | "folder") => {
    if (!bridge) return;
    const snapshot = stateRef.current;
    if (!snapshot.workspace || !snapshot.selectedThreadId) return;
    const key = draftKey(snapshot.workspace.workspaceId, snapshot.selectedThreadId);
    try {
      const picked = kind === "file" ? await bridge.pickFile() : await bridge.pickFolder();
      const current = stateRef.current;
      if (current.contextEpoch !== snapshot.contextEpoch || current.selectionEpoch !== snapshot.selectionEpoch) return;
      if (picked.canceled) return;
      if (!picked.result?.succeeded || !picked.result.data) {
        dispatchComposer({ type: "status", key, status: "error", error: safeFailure(picked.result?.error?.safeMessage) });
        return;
      }
      dispatchComposer({ type: "add-context", key, item: picked.result.data });
    } catch { dispatchComposer({ type: "status", key, status: "error", error: "Context picker failed." }); }
  }, [bridge]);

  const enqueueComposer = useCallback(async () => {
    if (!bridge) return;
    const snapshot = stateRef.current;
    const composerSnapshot = composerRef.current.snapshot;
    if (!snapshot.workspace || !snapshot.selectedThreadId || !snapshot.detail || !composerSnapshot) return;
    const key = draftKey(snapshot.workspace.workspaceId, snapshot.selectedThreadId);
    const draft = currentDraft(composerRef.current, key);
    if (!draft.text.trim()) return;
    dispatchComposer({ type: "status", key, status: "validating" });
    const epoch = snapshot.contextEpoch;
    const selectionEpoch = snapshot.selectionEpoch;
    try {
      dispatchComposer({ type: "status", key, status: "enqueueing" });
      const result = await bridge.enqueueComposer({
        threadId: snapshot.selectedThreadId,
        expectedThreadRevision: snapshot.detail.thread.revision,
        expectedQueueRevision: composerSnapshot.queueRevision,
        clientMutationId: `enqueue-${crypto.randomUUID()}`,
        prompt: draft.text,
        contextSelectionIds: draft.contextSelections.map(item => item.selectionId),
        catalogSelections: draft.catalogSelections.map(item => ({ kind: item.kind, id: item.id, catalogRevision: item.catalogRevision })),
      });
      if (!result.succeeded || !result.data) {
        dispatchComposer({ type: "status", key, status: "error", error: safeFailure(result.error?.safeMessage) });
        return;
      }
      const intentId = result.data.pendingIntent?.intentId;
      const authoritative = await bridge.getComposer({ threadId: snapshot.selectedThreadId });
      const current = stateRef.current;
      if (current.contextEpoch !== epoch || current.selectionEpoch !== selectionEpoch || current.selectedThreadId !== snapshot.selectedThreadId) return;
      if (!authoritative.succeeded || !authoritative.data || !intentId || authoritative.data.pendingIntent?.intentId !== intentId) {
        dispatchComposer({ type: "status", key, status: "error", error: "Queue confirmation could not be verified." });
        return;
      }
      dispatchComposer({ type: "snapshot", snapshot: authoritative.data });
      dispatchComposer({ type: "clear-draft", key });
      const started = await bridge.startTurn({
        threadId: snapshot.selectedThreadId,
        expectedThreadRevision: snapshot.detail.thread.revision,
        expectedQueueRevision: authoritative.data.queueRevision,
        clientMutationId: `start-${intentId}`,
      });
      if (!started.succeeded || !started.data) {
        dispatchComposer({ type: "status", key, status: "error", error: safeFailure(started.error?.safeMessage) });
        return;
      }
      const refreshed = await bridge.getComposer({ threadId: snapshot.selectedThreadId });
      if (refreshed.succeeded && refreshed.data) dispatchComposer({ type: "snapshot", snapshot: refreshed.data });
      await refreshThreads(epoch);
      await fetchThread(snapshot.selectedThreadId, 0, false, epoch, selectionEpoch);
    } catch { dispatchComposer({ type: "status", key, status: "error", error: "Prompt could not be queued." }); }
  }, [bridge, fetchThread, refreshThreads]);

  const clearComposer = useCallback(async () => {
    if (!bridge) return;
    const snapshot = stateRef.current;
    const composerSnapshot = composerRef.current.snapshot;
    if (!snapshot.selectedThreadId || !composerSnapshot?.pendingIntent) return;
    try {
      const result = await bridge.clearComposer({ threadId: snapshot.selectedThreadId, expectedQueueRevision: composerSnapshot.queueRevision, clientMutationId: `clear-${crypto.randomUUID()}` });
      if (result.succeeded && result.data) dispatchComposer({ type: "snapshot", snapshot: result.data });
    } catch { /* authoritative pending state remains visible */ }
  }, [bridge]);

  const cancelTurn = useCallback(async (turnId: string, turnRevision: number) => {
    if (!bridge) return "Desktop bridge unavailable.";
    const snapshot = stateRef.current;
    if (!snapshot.selectedThreadId || !snapshot.detail) return "Select a thread first.";
    try {
      const clientMutationId = `cancel-${crypto.randomUUID()}`;
      let expectedThreadRevision = snapshot.detail.thread.revision;
      let expectedTurnRevision = turnRevision;
      let result = await bridge.cancelTurn({ threadId: snapshot.selectedThreadId, turnId, expectedThreadRevision, expectedTurnRevision, clientMutationId });
      for (let attempt = 1; !result.succeeded && result.error?.category === "conflict" && attempt < 4; attempt++) {
        // A running turn may commit bounded diagnostic items between render and
        // click. Bounded conflict reconciliation repeats the user's cancel with
        // the same mutation identity; it never starts or replays execution.
        const latest = await bridge.getThread({ threadId: snapshot.selectedThreadId, afterSequence: 0 });
        if (!latest.succeeded || !latest.data) return safeFailure(latest.error?.safeMessage);
        const latestTurn = latest.data.turns.find((turn) => turn.turnId === turnId);
        if (!latestTurn) return "Turn is no longer available.";
        if (latestTurn.status === "canceling" || latestTurn.status === "canceled") {
          await refreshThreads(snapshot.contextEpoch);
          await fetchThread(snapshot.selectedThreadId, 0, false, snapshot.contextEpoch, snapshot.selectionEpoch);
          return null;
        }
        expectedThreadRevision = latest.data.thread.revision;
        expectedTurnRevision = latestTurn.revision;
        result = await bridge.cancelTurn({ threadId: snapshot.selectedThreadId, turnId, expectedThreadRevision, expectedTurnRevision, clientMutationId });
      }
      await refreshThreads(snapshot.contextEpoch);
      await fetchThread(snapshot.selectedThreadId, 0, false, snapshot.contextEpoch, snapshot.selectionEpoch);
      return result.succeeded ? null : safeFailure(result.error?.safeMessage);
    } catch {
      return "Turn could not be canceled.";
    }
  }, [bridge, fetchThread, refreshThreads]);

  const resolveApproval = useCallback(async (turnId: string, requestId: string, approvalRevision: number, turnRevision: number, decision: "approve" | "deny") => {
    if (!bridge) return "Desktop bridge unavailable.";
    const snapshot = stateRef.current;
    if (!snapshot.selectedThreadId || !snapshot.detail) return "Select a thread first.";
    try {
      const clientMutationId = `approval-${crypto.randomUUID()}`;
      let expectedThreadRevision = snapshot.detail.thread.revision;
      let expectedTurnRevision = turnRevision;
      let expectedApprovalRevision = approvalRevision;
      let result = await bridge.resolveApproval({ threadId: snapshot.selectedThreadId, turnId, requestId, decision, expectedThreadRevision, expectedTurnRevision, expectedApprovalRevision, clientMutationId });
      for (let attempt = 1; !result.succeeded && result.error?.category === "conflict" && attempt < 4; attempt++) {
        const latest = await bridge.getThread({ threadId: snapshot.selectedThreadId, afterSequence: 0 });
        if (!latest.succeeded || !latest.data) return safeFailure(latest.error?.safeMessage);
        const latestTurn = latest.data.turns.find((turn) => turn.turnId === turnId);
        const latestApproval = latestTurn?.approval;
        if (!latestTurn || latestApproval?.requestId !== requestId) return "Approval request is no longer active.";
        expectedThreadRevision = latest.data.thread.revision;
        expectedTurnRevision = latestTurn.revision;
        expectedApprovalRevision = latestApproval.approvalRevision;
        result = await bridge.resolveApproval({ threadId: snapshot.selectedThreadId, turnId, requestId, decision, expectedThreadRevision, expectedTurnRevision, expectedApprovalRevision, clientMutationId });
      }
      await refreshThreads(snapshot.contextEpoch);
      await fetchThread(snapshot.selectedThreadId, 0, false, snapshot.contextEpoch, snapshot.selectionEpoch);
      return result.succeeded ? null : safeFailure(result.error?.safeMessage);
    } catch {
      return "Approval could not be resolved.";
    }
  }, [bridge, fetchThread, refreshThreads]);

  const resumeTurn = useCallback(async (turnId: string, turnRevision: number) => {
    if (!bridge) return "Desktop bridge unavailable.";
    const snapshot = stateRef.current;
    if (!snapshot.selectedThreadId || !snapshot.detail) return "Select a thread first.";
    try {
      const result = await bridge.resumeTurn({
        threadId: snapshot.selectedThreadId,
        turnId,
        expectedThreadRevision: snapshot.detail.thread.revision,
        expectedTurnRevision: turnRevision,
        checkpointId: "checkpoint-unavailable",
        clientMutationId: `resume-${crypto.randomUUID()}`,
      });
      await refreshThreads(snapshot.contextEpoch);
      await fetchThread(snapshot.selectedThreadId, 0, false, snapshot.contextEpoch, snapshot.selectionEpoch);
      return result.succeeded ? null : safeFailure(result.error?.safeMessage);
    } catch {
      return "Turn could not be resumed.";
    }
  }, [bridge, fetchThread, refreshThreads]);

  const restartTurn = useCallback(async (sourceTurnId: string, sourceTurnRevision: number) => {
    if (!bridge) return "Desktop bridge unavailable.";
    const snapshot = stateRef.current;
    if (!snapshot.selectedThreadId || !snapshot.detail) return "Select a thread first.";
    try {
      const result = await bridge.restartTurn({
        threadId: snapshot.selectedThreadId,
        sourceTurnId,
        expectedThreadRevision: snapshot.detail.thread.revision,
        expectedSourceTurnRevision: sourceTurnRevision,
        confirmed: true,
        clientMutationId: `restart-${crypto.randomUUID()}`,
      });
      await refreshThreads(snapshot.contextEpoch);
      await fetchThread(snapshot.selectedThreadId, 0, false, snapshot.contextEpoch, snapshot.selectionEpoch);
      return result.succeeded ? null : safeFailure(result.error?.safeMessage);
    } catch {
      return "Turn could not be restarted.";
    }
  }, [bridge, fetchThread, refreshThreads]);

  return {
    state,
    openWorkspace,
    restartRuntime,
    selectThread,
    loadMore,
    createThread: (title: string) => bridge ? mutate(() => bridge.createThread({ title })) : Promise.resolve("Desktop bridge unavailable."),
    renameThread: (threadId: string, expectedRevision: number, title: string) => bridge ? mutate(() => bridge.renameThread({ threadId, expectedRevision, title })) : Promise.resolve("Desktop bridge unavailable."),
    archiveThread: (threadId: string, expectedRevision: number) => bridge ? mutate(() => bridge.archiveThread({ threadId, expectedRevision })) : Promise.resolve("Desktop bridge unavailable."),
    setReviewTab,
    selectReport,
    selectArtifact,
    reviewCommands: {
      previewArtifact,
      verifyArtifact,
      exportArtifact,
      loadGerber,
      decideGerber,
    },
    composer,
    composerDraft: currentDraft(composer, activeDraftKey),
    composerDisabledReason: !state.workspace ? "Open a workspace to compose." : !state.selectedThreadId ? "Select a thread to compose." : state.detail?.thread.status === "archived" ? "Archived threads cannot accept input." : state.runtime.state !== "ready" ? "AppHost is unavailable." : composer.snapshot?.pendingIntent ? "Clear the pending input before composing another." : null,
    setComposerText: (text: string) => { if (activeDraftKey) dispatchComposer({ type: "text", key: activeDraftKey, text }); },
    searchMentions,
    closeMentions: () => { mentionRequest.current++; dispatchComposer({ type: "mentions-close" }); },
    addComposerContext: addContext,
    addComposerCatalog: addCatalog,
    removeComposerContext: (selectionId: string) => { if (activeDraftKey) dispatchComposer({ type: "remove-context", key: activeDraftKey, selectionId }); },
    removeComposerCatalog: (item: SelectedCatalogItem) => { if (activeDraftKey) dispatchComposer({ type: "remove-catalog", key: activeDraftKey, kind: item.kind, id: item.id }); },
    pickComposerFile: () => pickContext("file"),
    pickComposerFolder: () => pickContext("folder"),
    enqueueComposer,
    clearComposer,
    cancelTurn,
    resolveApproval,
    resumeTurn,
    restartTurn,
  };
}

function safeFailure(message: string | null | undefined): string {
  return message && message.length <= 4096 ? message : "The requested operation failed.";
}
