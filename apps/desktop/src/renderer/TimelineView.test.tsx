import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { isTimelineItemData, type ThreadDetailData, type TimelineItemData } from "../generated/desktop-contracts";
import { TimelineItem } from "./TimelineItem";
import { TimelineView } from "./TimelineView";

const types = [
  "user.message", "assistant.message", "plan.updated", "tool.started", "tool.completed", "command.started", "command.completed",
  "approval.requested", "approval.resolved", "changes.updated", "report.available", "artifact.available", "warning.raised", "turn.completed",
] as const;

describe("frozen timeline projection", () => {
  it("renders every frozen type from validator-checked data", () => {
    const items = types.map((type, index) => item(index + 1, type));
    expect(items.every(isTimelineItemData)).toBe(true);
    render(<>{items.map((value) => <TimelineItem key={value.itemId} item={value} />)}</>);
    for (const label of ["User message", "Assistant message", "Plan updated", "Approval requested", "Changes updated", "Artifact available", "Turn completed"]) {
      expect(screen.getByText(label)).toBeTruthy();
    }
  });

  it("keeps a 2,000 item history DOM-bounded and loads only on request", async () => {
    const onLoadMore = vi.fn();
    const timeline = Array.from({ length: 2000 }, (_, index) => item(index + 1, types[index % types.length] as string));
    render(<TimelineView detail={{ ...detail, timeline }} status="ready" error={null} onLoadMore={onLoadMore} />);
    expect(document.querySelectorAll(".timeline-card").length).toBeLessThanOrEqual(80);
    await userEvent.click(screen.getByRole("button", { name: "Load newer items" }));
    expect(onLoadMore).toHaveBeenCalledOnce();
  });
});

const summary = {
  threadId: "thread-1", revision: 1, workspaceId: "workspace-1", title: "Timeline", status: "completed",
  createdAtUtc: "2026-07-17T00:00:00.000Z", updatedAtUtc: "2026-07-17T00:00:00.000Z", archivedAtUtc: null,
  turnCount: 1, timelineItemCount: 2000, activeTurnId: null,
  origin: { kind: "desktop", sourceKind: null, sourceId: null, sourceFingerprint: null },
} as const;

const detail: ThreadDetailData = { thread: summary, turns: [], timeline: [], nextSequence: 2001, timelineTruncated: true, recoveryRequired: false };

function item(sequence: number, type: string): TimelineItemData {
  return {
    itemId: `item-${sequence}`, turnId: "turn-1", sequence, timestampUtc: "2026-07-17T00:00:00.000Z", type,
    source: null, status: sequence % 7 === 0 ? "failed" : "completed", summary: `Timeline item ${sequence}`,
    payload: { kind: "text", text: sequence % 5 === 0 ? "Long payload" : null, name: null, succeeded: true, errorCode: null, count: null, referenceId: null, stopReason: null },
    redacted: sequence % 101 === 0,
  };
}
