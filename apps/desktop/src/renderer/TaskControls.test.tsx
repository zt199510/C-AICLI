import { render } from "@testing-library/react";
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
