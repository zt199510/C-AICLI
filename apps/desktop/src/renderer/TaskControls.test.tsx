import { act, fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { ThreadDetailData, TurnSummaryData } from "../generated/desktop-contracts";
import { TaskControls } from "./TaskControls";

describe("TaskControls", () => {
  it("reuses its DOM across active and terminal turn transitions", () => {
    const props = {
      onCancel: vi.fn(async () => null),
      onApproval: vi.fn(async () => null),
      onResume: vi.fn(async () => null),
      onRestart: vi.fn(async () => null),
    };
    const view = render(<TaskControls {...props} detail={detail(null)} />);
    const stableNode = document.querySelector(".task-controls");
    expect(stableNode).not.toBeNull();
    expect((stableNode as HTMLElement).hidden).toBe(true);
    view.rerender(<TaskControls {...props} detail={detail(turn("running"))} />);
    expect(document.querySelector(".task-controls")).toBe(stableNode);
    expect((stableNode as HTMLElement).hidden).toBe(false);
    view.rerender(<TaskControls {...props} detail={detail(turn("completed"))} />);
    expect(document.querySelector(".task-controls")).toBe(stableNode);
    expect((stableNode as HTMLElement).hidden).toBe(true);
  });

  it("submits an approval revision once while the mutation is in flight", async () => {
    let finish: ((value: string | null) => void) | null = null;
    const onApproval = vi.fn(() => new Promise<string | null>((resolve) => { finish = resolve; }));
    const waiting = {
      ...turn("waiting-for-approval"),
      approval: {
        requestId: "approval-1",
        workspaceId: "workspace-1",
        threadId: "thread-1",
        turnId: "turn-1",
        turnRevision: 4,
        approvalRevision: 2,
        policyIdentity: "policy",
        policyRevision: "1",
        risk: "write",
        operation: "workspace.apply_patch",
        targetClass: "workspace",
        safeSummary: "Apply the proposed patch",
        createdAtUtc: "2026-07-17T00:00:00.000Z",
        expiresAtUtc: "2026-07-17T00:30:00.000Z",
      },
    };
    render(<TaskControls detail={detail(waiting)} onCancel={vi.fn(async () => null)} onApproval={onApproval} onResume={vi.fn(async () => null)} onRestart={vi.fn(async () => null)} />);

    const approve = screen.getByRole("button", { name: "Approve" });
    fireEvent.click(approve);
    fireEvent.click(approve);
    expect(onApproval).toHaveBeenCalledOnce();
    expect(onApproval).toHaveBeenCalledWith("turn-1", "approval-1", 2, 4, "approve");
    expect((approve as HTMLButtonElement).disabled).toBe(true);
    await act(async () => { finish?.(null); });
  });

  it("shows recovery actions instead of conflicting running and stop controls", () => {
    const interrupted = { ...turn("running"), recoveryRequired: true };
    render(<TaskControls detail={detail(interrupted)} onCancel={vi.fn(async () => null)} onApproval={vi.fn(async () => null)} onResume={vi.fn(async () => null)} onRestart={vi.fn(async () => null)} />);
    expect(screen.getByText("Turn interrupted")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Resume" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Restart" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Stop" })).toBeNull();
  });

  it("keeps the running state compact and exposes only the stop action", () => {
    render(<TaskControls detail={detail(turn("running"))} onCancel={vi.fn(async () => null)} onApproval={vi.fn(async () => null)} onResume={vi.fn(async () => null)} onRestart={vi.fn(async () => null)} />);
    expect(screen.getByText("C-AICLI is working")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Stop" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Resume" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Restart" })).toBeNull();
  });
});

function turn(status: string): TurnSummaryData {
  return {
    turnId: "turn-1", ordinal: 1, revision: 1, status,
    createdAtUtc: "2026-07-17T00:00:00.000Z", startedAtUtc: "2026-07-17T00:00:01.000Z",
    completedAtUtc: status === "completed" ? "2026-07-17T00:00:02.000Z" : null,
    taskSummary: "Task", stopReason: null, errorCode: null, sourcePointers: [],
    timelineFirstSequence: 1, timelineLastSequence: 6, timelineItemCount: 6,
    recoveryRequired: false, approval: null,
  };
}

function detail(value: TurnSummaryData | null): ThreadDetailData {
  return {
    thread: {
      threadId: "thread-1", revision: 1, workspaceId: "workspace-1", title: "Thread", status: value?.status ?? "completed",
      createdAtUtc: "2026-07-17T00:00:00.000Z", updatedAtUtc: "2026-07-17T00:00:00.000Z", archivedAtUtc: null,
      turnCount: value ? 1 : 0, timelineItemCount: value?.timelineItemCount ?? 0,
      activeTurnId: value && value.status !== "completed" ? value.turnId : null,
      origin: { kind: "desktop", sourceKind: null, sourceId: null, sourceFingerprint: null },
    },
    turns: value ? [value] : [], timeline: [], nextSequence: null, timelineTruncated: false, recoveryRequired: false,
  };
}
