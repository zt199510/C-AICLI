import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type {
  ApprovalRequestData,
  ThreadChangedParams,
  ThreadDetailData,
  ThreadSummaryData,
  TurnSummaryData,
  WorkspaceSnapshotData,
} from "../generated/desktop-contracts";
import { createRuntimeStatus, type DesktopBridge } from "../shared/bridge-contract";
import { TaskControls } from "./TaskControls";
import { useDesktopController } from "./use-desktop-controller";

describe("approval projection resync", () => {
  it("rejects an old running response after ThreadStore commits waiting-for-approval", async () => {
    let notify: ((event: ThreadChangedParams) => void) | null = null;
    let authoritative = detail("running", 2, null);
    const delayedRunning = deferred<ReturnType<typeof threadResult>>();
    let detailCalls = 0;
    const bridge = {
      getRuntimeStatus: vi.fn(async () => createRuntimeStatus("runtime-ready")),
      getWorkspaceSnapshot: vi.fn(async () => workspace),
      listThreads: vi.fn(async () => listResult(authoritative.thread)),
      getThread: vi.fn(async () => {
        detailCalls++;
        if (detailCalls === 2) return delayedRunning.promise;
        return threadResult(authoritative);
      }),
      getComposer: vi.fn(async () => { throw new Error("not needed by this regression"); }),
      onRuntimeStatus: vi.fn(() => () => undefined),
      onThreadChanged: vi.fn((listener: (event: ThreadChangedParams) => void) => {
        notify = listener;
        return () => undefined;
      }),
    } as unknown as DesktopBridge;

    render(<ApprovalProjectionHarness bridge={bridge} />);
    await waitFor(() => expect((screen.getByRole("button", { name: "Select regression thread" }) as HTMLButtonElement).disabled).toBe(false));
    fireEvent.click(screen.getByRole("button", { name: "Select regression thread" }));
    await screen.findByText("running projection r2");

    act(() => notify?.(event(1, 2, 7)));
    await waitFor(() => expect(bridge.getThread).toHaveBeenCalledTimes(2));

    authoritative = detail("waiting-for-approval", 4, approval);
    act(() => {
      notify?.(event(2, 3, 8));
      notify?.(event(3, 4, 9));
    });

    expect(authoritative.thread.status).toBe("waiting-for-approval");
    expect(screen.getByText("running projection r2")).toBeTruthy();

    await act(async () => delayedRunning.resolve(threadResult(detail("running", 2, null))));

    await waitFor(() => expect(screen.getByRole("group", { name: "Approval request" })).toBeTruthy());
    expect(screen.getByText("waiting-for-approval projection r4")).toBeTruthy();
    expect(bridge.getThread).toHaveBeenCalledTimes(3);
  });
});

function ApprovalProjectionHarness(props: { readonly bridge: DesktopBridge }) {
  const controller = useDesktopController(props.bridge);
  return (
    <>
      <button
        type="button"
        disabled={!controller.state.workspace}
        onClick={() => controller.selectThread("thread-1")}
      >Select regression thread</button>
      <span>{controller.state.detail
        ? `${controller.state.detail.thread.status} projection r${controller.state.detail.thread.revision}`
        : "no projection"}</span>
      <TaskControls
        detail={controller.state.detail}
        onCancel={async () => null}
        onApproval={async () => null}
        onResume={async () => null}
        onRestart={async () => null}
      />
    </>
  );
}

const workspace: WorkspaceSnapshotData = {
  workspaceId: "workspace-1", rootPath: "C:\\workspace", status: "ready",
  capabilities: { readOnlyQueries: true, gitQueries: true, localCatalogs: true, managedArtifacts: true, controlledContext: true },
  configuration: { hasApiKey: false, apiKeySource: "none", effectiveModel: "gpt-test", modelSource: "default", agentBackendSource: "default", approvalMode: "ask", approvalModeSource: "default", loadedSourceCount: 0 },
};

const approval: ApprovalRequestData = {
  requestId: "approval-1", workspaceId: "workspace-1", threadId: "thread-1", turnId: "turn-1",
  turnRevision: 4, approvalRevision: 1, policyIdentity: "policy-1", policyRevision: "1",
  risk: "write", operation: "workspace.apply_patch", targetClass: "workspace-file",
  safeSummary: "Replace before with after", createdAtUtc: "2026-07-28T00:00:00.000Z",
  expiresAtUtc: "2026-07-28T00:30:00.000Z",
};

function detail(status: string, revision: number, activeApproval: ApprovalRequestData | null): ThreadDetailData {
  const turn: TurnSummaryData = {
    turnId: "turn-1", ordinal: 1, revision, status,
    createdAtUtc: "2026-07-28T00:00:00.000Z", startedAtUtc: "2026-07-28T00:00:01.000Z", completedAtUtc: null,
    taskSummary: "Approval projection", stopReason: null, errorCode: null, sourcePointers: [],
    timelineFirstSequence: 1, timelineLastSequence: status === "waiting-for-approval" ? 4 : 1,
    timelineItemCount: status === "waiting-for-approval" ? 4 : 1, recoveryRequired: false, approval: activeApproval,
  };
  return {
    thread: summary(status, revision), turns: [turn], timeline: [], nextSequence: null,
    timelineTruncated: false, recoveryRequired: false,
  };
}

function summary(status: string, revision: number): ThreadSummaryData {
  return {
    threadId: "thread-1", revision, workspaceId: "workspace-1", title: "Approval projection", status,
    createdAtUtc: "2026-07-28T00:00:00.000Z", updatedAtUtc: "2026-07-28T00:00:04.000Z", archivedAtUtc: null,
    turnCount: 1, timelineItemCount: status === "waiting-for-approval" ? 4 : 1, activeTurnId: "turn-1",
    origin: { kind: "desktop", sourceKind: null, sourceId: null, sourceFingerprint: null },
  };
}

function event(eventSequence: number, revision: number, committedSequence: number): ThreadChangedParams {
  return {
    schemaVersion: 1, eventSequence, workspaceId: "workspace-1", threadId: "thread-1",
    revision, committedSequence, changeKind: "updated", emittedAtUtc: "2026-07-28T00:00:04.000Z",
  };
}

function threadResult(value: ThreadDetailData) {
  return { schemaVersion: 1, succeeded: true, data: value, error: null, diagnostics: [], truncated: false } as const;
}

function listResult(value: ThreadSummaryData) {
  return { schemaVersion: 1, succeeded: true, data: { threads: [value], truncated: false }, error: null, diagnostics: [], truncated: false } as const;
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((complete) => { resolve = complete; });
  return { promise, resolve };
}
