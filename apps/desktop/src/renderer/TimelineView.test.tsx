import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { isTimelineItemData, type ThreadDetailData, type TimelineItemData } from "../generated/desktop-contracts";
import { TimelineItem } from "./TimelineItem";
import { TimelineView } from "./TimelineView";
import { FROZEN_TIMELINE_TYPES, projectTimeline } from "./timeline-projection";

const types = FROZEN_TIMELINE_TYPES;

describe("frozen timeline projection", () => {
  it("renders every frozen type from validator-checked data", () => {
    const items = types.map((type, index) => item(index + 1, type));
    expect(items.every(isTimelineItemData)).toBe(true);
    render(<>{items.map((value) => <TimelineItem key={value.itemId} item={value} />)}</>);
    for (const label of ["User message", "Assistant message", "Assistant result", "Plan updated", "Approval requested", "Verification completed", "Changes updated", "Artifact available", "Turn completed"]) {
      expect(screen.getByText(label)).toBeTruthy();
    }
  });

  it("groups adjacent execution events while keeping warnings and approvals visible", () => {
    const projected = projectTimeline([
      { ...item(1, "tool.started"), payload: { ...item(1, "tool.started").payload, name: "workspace.read" } },
      { ...item(2, "tool.completed"), payload: { ...item(2, "tool.completed").payload, name: "workspace.read" } },
      item(3, "approval.requested"),
      item(4, "warning.raised"),
    ]);
    expect(projected.map((block) => [block.kind, block.items.length])).toEqual([
      ["execution", 2],
      ["approval", 1],
      ["result", 1],
    ]);
  });

  it("renders an unknown type as an explicit audit fallback", () => {
    render(<TimelineItem item={item(1, "future.event")} projectionKind="audit-fallback" />);
    expect(screen.getByText("Unrecognized audit event")).toBeTruthy();
    expect(screen.getByText("Type: future.event")).toBeTruthy();
  });

  it("keeps active turn controls inside the conversation surface", () => {
    render(
      <TimelineView
        detail={{ ...detail, timeline: [item(1, "user.message")] }}
        status="ready"
        error={null}
        controls={<div data-testid="inline-controls">Controls</div>}
        onLoadMore={vi.fn()}
      />,
    );
    const controls = screen.getByTestId("inline-controls");
    expect(controls.closest(".timeline-view")).not.toBeNull();
    expect(controls.compareDocumentPosition(document.querySelector(".timeline-items")!) & Node.DOCUMENT_POSITION_PRECEDING).toBeTruthy();
  });

  it("keeps a 2,000 item history DOM-bounded and loads only on request", async () => {
    const onLoadMore = vi.fn();
    const timeline = Array.from({ length: 2000 }, (_, index) => item(index + 1, types[index % types.length] as string));
    render(<TimelineView detail={{ ...detail, timeline }} status="ready" error={null} onLoadMore={onLoadMore} />);
    expect(document.querySelectorAll(".timeline-card").length).toBeLessThanOrEqual(80);
    await userEvent.click(screen.getByRole("button", { name: "Load newer items" }));
    expect(onLoadMore).toHaveBeenCalledOnce();
  });

  it("shows the latest turn by default and lets the user browse another turn", async () => {
    const timeline = Array.from({ length: 36 }, (_, index) => ({
      ...item(index + 1, types[index % types.length] as string),
      turnId: `turn-${Math.floor(index / 6) + 1}`,
    }));
    render(<TimelineView detail={{ ...detail, timeline }} status="ready" error={null} onLoadMore={vi.fn()} />);
    expect(document.querySelectorAll(".timeline-turn-browser-toggle")).toHaveLength(1);
    expect(document.querySelectorAll(".timeline-turn-option")).toHaveLength(0);
    expect(document.querySelectorAll(".timeline-card").length).toBeGreaterThan(0);
    expect(document.querySelectorAll(".timeline-card").length).toBeLessThanOrEqual(6);
    await userEvent.click(screen.getByRole("button", { name: /Browse 6 turns/ }));
    expect(document.querySelectorAll(".timeline-turn-option")).toHaveLength(6);
    await userEvent.click(screen.getByRole("button", { name: /Turn 1/ }));
    expect(document.querySelectorAll(".timeline-card").length).toBeGreaterThan(0);
    expect(screen.getByText("Timeline item 1")).toBeTruthy();
  });

  it("reuses the recovery banner across projection transitions", () => {
    const view = render(<TimelineView detail={detail} status="ready" error={null} onLoadMore={vi.fn()} />);
    const stableNode = document.querySelector(".recovery-banner");
    expect(stableNode).not.toBeNull();
    expect((stableNode as HTMLElement).hidden).toBe(true);
    view.rerender(<TimelineView detail={{ ...detail, recoveryRequired: true }} status="ready" error={null} onLoadMore={vi.fn()} />);
    expect(document.querySelector(".recovery-banner")).toBe(stableNode);
    expect((stableNode as HTMLElement).hidden).toBe(false);
    view.rerender(<TimelineView detail={detail} status="ready" error={null} onLoadMore={vi.fn()} />);
    expect(document.querySelector(".recovery-banner")).toBe(stableNode);
    expect((stableNode as HTMLElement).hidden).toBe(true);
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
