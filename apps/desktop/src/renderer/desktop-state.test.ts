import { describe, expect, it } from "vitest";
import type { ChangesData, ThreadDetailData, ThreadSummaryData, TimelineItemData, WorkspaceSnapshotData } from "../generated/desktop-contracts";
import { createRuntimeStatus } from "../shared/bridge-contract";
import { desktopReducer, initialDesktopState, mergeDetail } from "./desktop-state";

describe("desktop authoritative projection", () => {
  it("resets workspace truth when runtime leaves ready", () => {
    const ready = desktopReducer(initialDesktopState, { type: "workspace", workspace });
    const failed = desktopReducer(ready, { type: "runtime", status: createRuntimeStatus("apphost-exited") });
    expect(failed.workspace).toBeNull();
    expect(failed.threads).toEqual([]);
    expect(failed.selectedThreadId).toBeNull();
    expect(failed.contextEpoch).toBe(ready.contextEpoch + 1);
  });

  it("drops stale list and detail responses after context or selection changes", () => {
    const opened = desktopReducer(initialDesktopState, { type: "workspace", workspace });
    const selected = desktopReducer(opened, { type: "select", threadId: "thread-1" });
    const changed = desktopReducer(selected, { type: "select", threadId: "thread-2" });
    const staleList = desktopReducer(changed, { type: "threads-ready", epoch: 0, threads: [thread], truncated: false });
    const staleDetail = desktopReducer(changed, { type: "detail-ready", epoch: opened.contextEpoch, selectionEpoch: selected.selectionEpoch, requestId: 1, detail, append: false });
    expect(staleList.threads).toEqual([]);
    expect(staleDetail.detail).toBeNull();
  });

  it("keeps the latest detail when same-selection requests resolve out of order", () => {
    const opened = desktopReducer(initialDesktopState, { type: "workspace", workspace });
    const selected = desktopReducer(opened, { type: "select", threadId: "thread-1" });
    const first = desktopReducer(selected, {
      type: "detail-loading", epoch: opened.contextEpoch, selectionEpoch: selected.selectionEpoch,
      requestId: 1, threadId: "thread-1",
    });
    const second = desktopReducer(first, {
      type: "detail-loading", epoch: opened.contextEpoch, selectionEpoch: selected.selectionEpoch,
      requestId: 2, threadId: "thread-1",
    });
    const latest = desktopReducer(second, {
      type: "detail-ready", epoch: opened.contextEpoch, selectionEpoch: selected.selectionEpoch,
      requestId: 2, detail: { ...detail, thread: { ...thread, revision: 2 } }, append: false,
    });
    const stale = desktopReducer(latest, {
      type: "detail-ready", epoch: opened.contextEpoch, selectionEpoch: selected.selectionEpoch,
      requestId: 1, detail, append: false,
    });

    expect(stale.detail?.thread.revision).toBe(2);
    expect(stale.detailRequestId).toBe(2);
  });

  it("deduplicates late notifications and ignores another workspace", () => {
    const opened = desktopReducer(initialDesktopState, { type: "workspace", workspace });
    const first = desktopReducer(opened, { type: "event", event: event(4) });
    const duplicate = desktopReducer(first, { type: "event", event: event(4) });
    const mismatch = desktopReducer(duplicate, { type: "event", event: { ...event(9), workspaceId: "other" } });
    expect(first.lastEventSequence).toBe(4);
    expect(duplicate.lastEventSequence).toBe(4);
    expect(mismatch.ignoredEvents).toBe(2);
    expect(mismatch.refreshing).toBe(true);
  });

  it("marks gaps, out-of-order events, and conflicting identities dirty without mutating canonical data", () => {
    const opened = desktopReducer(initialDesktopState, { type: "workspace", workspace });
    const first = desktopReducer(opened, { type: "event", event: event(4) });
    const settled = desktopReducer(first, { type: "refresh-complete" });
    const gap = desktopReducer(settled, { type: "event", event: event(7) });
    const gapSettled = desktopReducer(gap, { type: "refresh-complete" });
    const outOfOrder = desktopReducer(gapSettled, { type: "event", event: event(6) });
    const conflict = desktopReducer(gapSettled, { type: "event", event: { ...event(7), revision: 99 } });
    const duplicate = desktopReducer(gapSettled, { type: "event", event: event(7) });

    expect(gap.lastEventSequence).toBe(7);
    expect(outOfOrder.lastEventSequence).toBe(7);
    expect(outOfOrder.refreshing).toBe(true);
    expect(conflict.lastEventSequence).toBe(7);
    expect(conflict.refreshing).toBe(true);
    expect(duplicate.refreshing).toBe(false);
    expect(outOfOrder.threads).toBe(gapSettled.threads);
  });

  it("keeps valid thread rows while surfacing a bounded corrupt-state warning", () => {
    const opened = desktopReducer(initialDesktopState, { type: "workspace", workspace });
    const ready = desktopReducer(opened, {
      type: "threads-ready",
      epoch: opened.contextEpoch,
      threads: [thread],
      truncated: false,
      warning: "Corrupt state was isolated: Thread JSON is corrupt.",
    });
    expect(ready.threads).toEqual([thread]);
    expect(ready.threadsStatus).toBe("ready");
    expect(ready.threadsError).toContain("Corrupt state was isolated");
  });

  it("preserves the bounded flag for a truncated changes projection", () => {
    const opened = desktopReducer(initialDesktopState, { type: "workspace", workspace });
    const ready = desktopReducer(opened, {
      type: "changes-ready",
      epoch: opened.contextEpoch,
      value: changes,
      truncated: true,
    });
    expect(ready.review.status).toBe("ready");
    expect(ready.review.truncated).toBe(true);
    expect(ready.review.changes?.diffTruncated).toBe(true);
  });

  it("merges forward pages by sequence/id and marks invariant conflicts for recovery", () => {
    const next = { ...detail, timeline: [item(2, "item-2")], nextSequence: null, timelineTruncated: false };
    expect(mergeDetail(detail, next).timeline.map((value) => value.sequence)).toEqual([1, 2]);
    const conflict = { ...detail, timeline: [item(1, "different")] };
    expect(mergeDetail(detail, conflict).recoveryRequired).toBe(true);
  });
});

const workspace: WorkspaceSnapshotData = {
  workspaceId: "workspace-1", rootPath: "C:\\workspace", status: "ready",
  capabilities: { readOnlyQueries: true, gitQueries: true, localCatalogs: true, managedArtifacts: true, controlledContext: true },
  configuration: { hasApiKey: false, apiKeySource: "none", effectiveModel: "gpt-test", modelSource: "default", agentBackendSource: "default", approvalMode: "ask", approvalModeSource: "default", loadedSourceCount: 0 },
};

const changes: ChangesData = {
  status: "ready", exitCode: 0, gitStatusSummary: "M src/review.ts", gitStatusSucceeded: true, gitStatusErrorCode: null,
  dirty: true, diffStatSummary: "bounded", diffSucceeded: true, diffErrorCode: null, diffTruncated: true,
  changedFiles: [{ path: "src/review.ts", status: "M" }], sessionSource: null, sessionName: null, warnings: ["bounded"],
};

const thread: ThreadSummaryData = {
  threadId: "thread-1", revision: 1, workspaceId: "workspace-1", title: "Review", status: "completed",
  createdAtUtc: "2026-07-17T00:00:00.000Z", updatedAtUtc: "2026-07-17T00:00:00.000Z", archivedAtUtc: null,
  turnCount: 1, timelineItemCount: 1, activeTurnId: null,
  origin: { kind: "desktop", sourceKind: null, sourceId: null, sourceFingerprint: null },
};

const detail: ThreadDetailData = { thread, turns: [], timeline: [item(1, "item-1")], nextSequence: 2, timelineTruncated: true, recoveryRequired: false };

function item(sequence: number, itemId: string): TimelineItemData {
  return {
    itemId, turnId: "turn-1", sequence, timestampUtc: "2026-07-17T00:00:00.000Z", type: "user.message",
    source: null, status: "completed", summary: `Item ${sequence}`,
    payload: { kind: "text", text: null, name: null, succeeded: null, errorCode: null, count: null, referenceId: null, stopReason: null },
    redacted: false,
  };
}

function event(eventSequence: number) {
  return { schemaVersion: 1, eventSequence, workspaceId: "workspace-1", threadId: "thread-1", revision: 2, committedSequence: 0, changeKind: "renamed", emittedAtUtc: "2026-07-17T00:00:00.000Z" } as const;
}
