import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { isTimelineItemData, type ThreadDetailData, type TimelineItemData } from "../generated/desktop-contracts";
import { TimelineItem } from "./TimelineItem";
import { projectConversationMessages, TimelineView } from "./TimelineView";
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
    expect(document.querySelectorAll("[data-sequence]").length).toBeLessThanOrEqual(80);
    await userEvent.click(screen.getByRole("button", { name: "Load newer items" }));
    expect(onLoadMore).toHaveBeenCalledOnce();
  });

  it("shows message history across turns by default and keeps activity available", async () => {
    const timeline = Array.from({ length: 36 }, (_, index) => ({
      ...item(index + 1, types[index % types.length] as string),
      turnId: `turn-${Math.floor(index / 6) + 1}`,
    }));
    render(<TimelineView detail={{ ...detail, timeline }} status="ready" error={null} onLoadMore={vi.fn()} />);
    const conversationCount = document.querySelectorAll(".conversation-message").length;
    expect(screen.getByRole("button", { name: /Activity/ }).getAttribute("aria-pressed")).toBe("false");
    expect(conversationCount).toBeGreaterThan(3);
    expect(conversationCount).toBeLessThan(36);
    expect(screen.getByText("Timeline item 1")).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: /Activity/ }));
    expect(document.querySelectorAll("[data-sequence]").length).toBeGreaterThan(conversationCount);
  });

  it("shows the safe summary for redacted messages without expanding their payload", () => {
    const message = {
      ...item(1, "assistant.final"),
      redacted: true,
      summary: "Safe assistant answer",
      payload: { ...item(1, "assistant.final").payload, text: "Raw payload must stay hidden" },
    };
    render(<TimelineView detail={{ ...detail, timeline: [message] }} status="ready" error={null} onLoadMore={vi.fn()} />);
    expect(screen.getByText("Safe assistant answer")).toBeTruthy();
    expect(screen.queryByText("Raw payload must stay hidden")).toBeNull();
  });

  it("coalesces assistant progress and final events into one visible reply", () => {
    const user = { ...item(1, "user.message"), redacted: true, summary: "Hello" };
    const progress = { ...item(2, "assistant.message"), redacted: true, summary: "Working" };
    const final = { ...item(3, "assistant.final"), redacted: true, summary: "Final answer" };
    const messages = projectConversationMessages([user, progress, final]);
    expect(messages.map((message) => message.summary)).toEqual(["Hello", "Final answer"]);
  });

  it("reconciles an optimistic user message with authority without a duplicate", () => {
    const authoritative = continuousDetail("running", "thinking", 1, [
      { ...item(1, "user.message"), summary: "Hello authority", payload: { ...item(1, "user.message").payload, text: "Hello authority" } },
    ]);
    render(<TimelineView
      detail={authoritative}
      status="ready"
      error={null}
      optimisticExchanges={[{
        localId: "local-1", threadId: "thread-1", authorityId: "intent-1",
        text: "Hello authority", createdAtUtc: "2026-07-17T00:00:00.000Z", error: null,
      }]}
      onLoadMore={vi.fn()}
    />);
    expect(screen.getAllByText("Hello authority")).toHaveLength(1);
  });

  it("keeps connecting thinking streaming and final in one assistant block", () => {
    const user = { ...item(1, "user.message"), summary: "Question" };
    const partial = {
      ...item(2, "assistant.message"),
      summary: "new attempt complete prefix",
      payload: { ...item(2, "assistant.message").payload, text: "new attempt complete prefix", attempt: 2, assistantMessageId: "assistant-1" },
    };
    const final = {
      ...item(3, "assistant.final"),
      summary: "Final answer",
      payload: { ...item(3, "assistant.final").payload, text: "Final answer", attempt: 2, assistantMessageId: "assistant-1" },
    };
    const view = render(<TimelineView detail={continuousDetail("running", "connecting", 1, [user])} status="ready" error={null} onLoadMore={vi.fn()} />);
    expect(document.querySelectorAll('[data-assistant-message-id="assistant-1"]')).toHaveLength(1);
    expect(screen.getByText("正在连接")).toBeTruthy();
    view.rerender(<TimelineView detail={continuousDetail("running", "thinking", 1, [user])} status="ready" error={null} onLoadMore={vi.fn()} />);
    expect(screen.getByText("正在思考")).toBeTruthy();
    view.rerender(<TimelineView detail={continuousDetail("running", "streaming", 2, [user, partial])} status="ready" error={null} onLoadMore={vi.fn()} />);
    expect(screen.getByText("new attempt complete prefix")).toBeTruthy();
    expect(document.querySelectorAll('[data-assistant-message-id="assistant-1"]')).toHaveLength(1);
    view.rerender(<TimelineView detail={continuousDetail("completed", "streaming", 2, [user, partial, final])} status="ready" error={null} onLoadMore={vi.fn()} />);
    expect(screen.getByText("Final answer")).toBeTruthy();
    expect(screen.queryByText("new attempt complete prefix")).toBeNull();
    expect(document.querySelectorAll('[data-assistant-message-id="assistant-1"]')).toHaveLength(1);
  });

  it("shows retry progress, authoritative duration and retry-exhausted actions", () => {
    const retrying = render(<TimelineView detail={continuousDetail("running", "retry-wait", 5, [
      { ...item(1, "user.message"), summary: "Question" },
    ])} status="ready" error={null} onLoadMore={vi.fn()} />);
    expect(screen.getByText("连接中断，正在重试 5/5...")).toBeTruthy();

    retrying.rerender(<TimelineView detail={continuousDetail("failed", "failed", 6, [
      { ...item(1, "user.message"), summary: "Question" },
    ], true)} status="ready" error={null} onLoadMore={vi.fn()} onRestart={vi.fn(async () => null)} />);
    expect(screen.getByText("连接失败，已重试 5 次")).toBeTruthy();
    expect(screen.getByText("连接失败 · 处理 8.4 秒")).toBeTruthy();
    expect(screen.getByRole("button", { name: /重新尝试/ })).toBeTruthy();
    expect(screen.getByRole("button", { name: /复制错误信息/ })).toBeTruthy();
  });

  it("shows message payload text without hiding it behind details", () => {
    const message = { ...item(1, "assistant.message"), summary: "Assistant message", payload: { ...item(1, "assistant.message").payload, text: "Visible assistant answer" } };
    render(<TimelineView detail={{ ...detail, timeline: [message] }} status="ready" error={null} onLoadMore={vi.fn()} />);
    expect(screen.getByText("Visible assistant answer")).toBeTruthy();
    expect(screen.queryByText("Long payload")).toBeNull();
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

function continuousDetail(
  status: string,
  phase: "connecting" | "thinking" | "streaming" | "retry-wait" | "failed",
  attempt: number,
  timeline: readonly TimelineItemData[],
  retryExhausted = false,
): ThreadDetailData {
  return {
    thread: {
      ...summary,
      status,
      activeTurnId: ["completed", "failed", "canceled"].includes(status) ? null : "turn-1",
      timelineItemCount: timeline.length,
    },
    turns: [{
      turnId: "turn-1", ordinal: 1, revision: 4, status,
      createdAtUtc: "2026-07-17T00:00:00.000Z",
      startedAtUtc: "2026-07-17T00:00:00.100Z",
      completedAtUtc: ["completed", "failed", "canceled"].includes(status) ? "2026-07-17T00:00:08.400Z" : null,
      taskSummary: "Question", stopReason: status, errorCode: status === "failed" ? "provider-transport-error" : null,
      sourcePointers: [], timelineFirstSequence: timeline.length ? 1 : null,
      timelineLastSequence: timeline.length ? timeline.length : null, timelineItemCount: timeline.length,
      recoveryRequired: false, approval: null, clientMessageId: "intent-1",
      provider: {
        phase, attempt, maxAdditionalRetries: 5, attemptHasStreamContent: phase === "streaming",
        assistantMessageId: "assistant-1", errorCategory: status === "failed" ? "transport" : null,
        retryable: status === "failed" ? true : null,
        safeErrorMessage: status === "failed" ? "The model connection could not be established." : null,
        retryExhausted,
      },
    }],
    timeline,
    nextSequence: null,
    timelineTruncated: false,
    recoveryRequired: false,
  };
}
