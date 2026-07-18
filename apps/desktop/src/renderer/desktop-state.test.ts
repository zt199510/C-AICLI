import { describe, expect, it } from "vitest";
import type { ThreadDetailData, ThreadSummaryData, TimelineItemData, WorkspaceSnapshotData } from "../generated/desktop-contracts";
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
    const staleDetail = desktopReducer(changed, { type: "detail-ready", epoch: opened.contextEpoch, selectionEpoch: selected.selectionEpoch, detail, append: false });
    expect(staleList.threads).toEqual([]);
    expect(staleDetail.detail).toBeNull();
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
