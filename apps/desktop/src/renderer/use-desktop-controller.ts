import { useCallback, useEffect, useReducer, useRef } from "react";
import type { ThreadChangedParams, WorkspaceSnapshotData } from "../generated/desktop-contracts";
import { createRuntimeStatus, type DesktopBridge } from "../shared/bridge-contract";
import { desktopReducer, initialDesktopState, type ReviewState } from "./desktop-state";

export function useDesktopController(bridge: DesktopBridge | undefined) {
  const [state, dispatch] = useReducer(desktopReducer, initialDesktopState);
  const stateRef = useRef(state);
  stateRef.current = state;
  const resyncRunning = useRef(false);
  const resyncDirty = useRef(false);

  const refreshThreads = useCallback(async (epoch = stateRef.current.contextEpoch) => {
    if (!bridge) return;
    dispatch({ type: "threads-loading", epoch });
    try {
      const result = await bridge.listThreads();
      if (!result.succeeded || !result.data) {
        dispatch({ type: "threads-error", epoch, message: safeFailure(result.error?.safeMessage) });
        return;
      }
      dispatch({ type: "threads-ready", epoch, threads: result.data.threads, truncated: result.data.truncated || result.truncated });
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
    dispatch({ type: "detail-loading", epoch, selectionEpoch, threadId });
    try {
      const result = await bridge.getThread({ threadId, afterSequence });
      if (!result.succeeded || !result.data) {
        dispatch({
          type: "detail-error",
          epoch,
          selectionEpoch,
          message: safeFailure(result.error?.safeMessage),
          notFound: result.error?.code === "thread-not-found",
        });
        return;
      }
      dispatch({ type: "detail-ready", epoch, selectionEpoch, detail: result.data, append });
    } catch {
      dispatch({ type: "detail-error", epoch, selectionEpoch, message: "Thread history could not be loaded." });
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
    if (resyncRunning.current) { resyncDirty.current = true; return; }
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
      if (!snapshot.workspace || event.workspaceId !== snapshot.workspace.workspaceId) {
        dispatch({ type: "event", event });
        return;
      }
      if (snapshot.lastEventSequence !== null && event.eventSequence <= snapshot.lastEventSequence) {
        dispatch({ type: "event", event });
        return;
      }
      dispatch({ type: "event", event });
      queueResync();
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
    dispatch({ type: "review-tab", tab });
    const epoch = stateRef.current.contextEpoch;
    if (!bridge || !stateRef.current.workspace) return;
    dispatch({ type: "review-loading", epoch });
    const failed = () => dispatch({ type: "review-error", epoch, message: "Review data could not be loaded." });
    if (tab === "changes") void bridge.getChanges({}).then((result) => {
      if (result.succeeded && result.data) dispatch({ type: "changes-ready", epoch, value: result.data });
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
  };
}

function safeFailure(message: string | null | undefined): string {
  return message && message.length <= 4096 ? message : "The requested operation failed.";
}
