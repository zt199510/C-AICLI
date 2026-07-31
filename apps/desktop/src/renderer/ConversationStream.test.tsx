import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { ThreadDetailData, TimelineItemData, TurnSummaryData } from "../generated/desktop-contracts";
import { ConversationStream } from "./ConversationStream";

describe("ConversationStream state matrix", () => {
  it.each([
    ["connecting", turn(), "正在连接"],
    ["thinking", turn({ provider: { phase: "thinking" } }), "正在思考"],
    ["streaming", turn({ provider: { phase: "streaming", attemptHasStreamContent: true } }), "正在生成回复"],
    ["retry-wait", turn({ provider: { phase: "retry-wait", attempt: 2 } }), "连接暂时中断，正在重试 2/5"],
    ["canceling", turn({ status: "canceling" }), "正在停止"],
    ["retry-exhausted", turn({ status: "failed", provider: { phase: "failed", attempt: 6, retryExhausted: true } }), "连接失败，已重试 5 次"],
    ["generic-failed", turn({ status: "failed", provider: { phase: "failed", safeErrorMessage: "Safe failure" } }), "处理失败"],
  ])("projects %s into one assistant message", (_, value, expected) => {
    renderStream(value);
    expect(screen.getByText(expected)).toBeTruthy();
    expect(document.querySelectorAll('[data-assistant-message-id="assistant-1"]')).toHaveLength(1);
  });

  it("renders completed and canceled as quiet terminal metadata without a large completed state", () => {
    const completed = renderStream(turn({
      status: "completed",
      completedAtUtc: "2026-07-17T00:00:08.400Z",
      provider: { phase: "streaming", attemptHasStreamContent: true },
    }));
    expect(screen.getByText("已处理 8.4 秒")).toBeTruthy();
    expect(screen.queryByText("已完成")).toBeNull();

    completed.rerender(stream(turn({
      status: "canceled",
      completedAtUtc: "2026-07-17T00:00:03.200Z",
      provider: { phase: "streaming", attemptHasStreamContent: true },
    })));
    expect(screen.getByText("已停止 · 处理 3.2 秒")).toBeTruthy();
  });

  it("keeps approval, retry-exhausted, and recovery mutations mutually exclusive", () => {
    const onApproval = vi.fn(async () => null);
    const onRestart = vi.fn(async () => null);
    const onResume = vi.fn(async () => null);
    const approvalTurn = turn({
      status: "waiting-for-approval",
      approval: approval(),
      recoveryRequired: true,
      provider: { phase: "retry-wait", attempt: 3 },
    });
    const view = render(stream(approvalTurn, { onApproval, onRestart, onResume }));
    expect(screen.getByRole("button", { name: "批准" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "拒绝" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "重新尝试" })).toBeNull();
    expect(screen.queryByRole("button", { name: "继续" })).toBeNull();
    expect(screen.queryByRole("button", { name: "重新开始" })).toBeNull();

    view.rerender(stream(turn({
      status: "failed",
      completedAtUtc: "2026-07-17T00:00:12.600Z",
      provider: { phase: "failed", attempt: 6, retryExhausted: true },
    }), { onApproval, onRestart, onResume }));
    expect(screen.getByRole("button", { name: "重新尝试" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "复制错误信息" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "批准" })).toBeNull();
    expect(screen.queryByRole("button", { name: "继续" })).toBeNull();

    view.rerender(stream(turn({
      status: "failed",
      recoveryRequired: true,
      provider: { phase: "failed" },
    }), { onApproval, onRestart, onResume }));
    expect(screen.getByRole("button", { name: "继续" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "重新开始" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "重新尝试" })).toBeNull();
    expect(screen.queryByRole("button", { name: "批准" })).toBeNull();
  });

  it("does not expose recovery actions for a generic non-retryable failure", () => {
    renderStream(turn({
      status: "failed",
      provider: { phase: "failed", retryable: false, safeErrorMessage: "Invalid configuration" },
    }));
    expect(screen.getByText("Invalid configuration")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "重新尝试" })).toBeNull();
    expect(screen.queryByRole("button", { name: "重新开始" })).toBeNull();
  });

  it("folds completed activity and expands failed activity without exposing raw payloads", () => {
    const detail = makeDetail(turn({ status: "completed", completedAtUtc: "2026-07-17T00:00:08.400Z" }), [
      item(1, "user.message", { text: "Question" }),
      item(2, "tool.started", { name: "workspace.read" }),
      item(3, "tool.completed", { name: "workspace.read" }),
      item(4, "assistant.final", { text: "Done", attempt: 1, assistantMessageId: "assistant-1" }),
    ]);
    render(<ConversationStream detail={detail} optimisticExchanges={[]} />);
    const activity = screen.getByText("已完成 1 项操作").closest("details");
    expect(activity?.hasAttribute("open")).toBe(false);
    expect(document.body.textContent).not.toContain("raw-secret-payload");
  });
});

type Overrides = Partial<Omit<TurnSummaryData, "provider">> & {
  readonly provider?: Partial<TurnSummaryData["provider"]>;
};

function turn(overrides: Overrides = {}): TurnSummaryData {
  return {
    turnId: "turn-1",
    ordinal: 1,
    revision: 4,
    status: "running",
    createdAtUtc: "2026-07-17T00:00:00.000Z",
    startedAtUtc: "2026-07-17T00:00:00.100Z",
    completedAtUtc: null,
    taskSummary: "Question",
    stopReason: null,
    errorCode: null,
    sourcePointers: [],
    timelineFirstSequence: 1,
    timelineLastSequence: 4,
    timelineItemCount: 4,
    recoveryRequired: false,
    approval: null,
    clientMessageId: "intent-1",
    ...overrides,
    provider: {
      phase: "connecting",
      attempt: 1,
      maxAdditionalRetries: 5,
      attemptHasStreamContent: false,
      assistantMessageId: "assistant-1",
      errorCategory: null,
      retryable: null,
      safeErrorMessage: null,
      retryExhausted: false,
      ...overrides.provider,
    },
  };
}

function approval() {
  return {
    requestId: "approval-1",
    workspaceId: "workspace-1",
    threadId: "thread-1",
    turnId: "turn-1",
    turnRevision: 4,
    approvalRevision: 2,
    policyIdentity: "policy",
    policyRevision: "revision",
    risk: "write",
    operation: "workspace.apply_patch",
    targetClass: "workspace",
    safeSummary: "Apply the proposed patch",
    createdAtUtc: "2026-07-17T00:00:02.000Z",
    expiresAtUtc: "2026-07-17T00:05:02.000Z",
  } as const;
}

function renderStream(value: TurnSummaryData) {
  return render(stream(value));
}

function stream(
  value: TurnSummaryData,
  mutations: Partial<Parameters<typeof ConversationStream>[0]> = {},
) {
  return (
    <ConversationStream
      detail={makeDetail(value)}
      optimisticExchanges={[]}
      {...mutations}
    />
  );
}

function makeDetail(
  value: TurnSummaryData,
  timeline: readonly TimelineItemData[] = [
    item(1, "user.message", { text: "Question" }),
    item(2, "assistant.message", {
      text: "Assistant content",
      attempt: 1,
      assistantMessageId: "assistant-1",
    }),
  ],
): ThreadDetailData {
  return {
    thread: {
      threadId: "thread-1",
      revision: 5,
      workspaceId: "workspace-1",
      title: "Conversation",
      status: value.status,
      createdAtUtc: value.createdAtUtc,
      updatedAtUtc: value.completedAtUtc ?? value.createdAtUtc,
      archivedAtUtc: null,
      turnCount: 1,
      timelineItemCount: timeline.length,
      activeTurnId: ["completed", "failed", "canceled"].includes(value.status) ? null : value.turnId,
      origin: { kind: "desktop", sourceKind: null, sourceId: null, sourceFingerprint: null },
    },
    turns: [value],
    timeline: value.approval && !timeline.some((entry) => entry.type === "approval.requested")
      ? [...timeline, item(3, "approval.requested")]
      : timeline,
    nextSequence: null,
    timelineTruncated: false,
    recoveryRequired: false,
  };
}

function item(
  sequence: number,
  type: string,
  payload: Partial<TimelineItemData["payload"]> = {},
): TimelineItemData {
  return {
    itemId: `item-${sequence}`,
    turnId: "turn-1",
    sequence,
    timestampUtc: `2026-07-17T00:00:${String(sequence).padStart(2, "0")}.000Z`,
    type,
    source: null,
    status: type.endsWith(".started") ? "running" : "completed",
    summary: `${type} summary`,
    payload: {
      kind: type,
      text: null,
      name: null,
      succeeded: null,
      errorCode: null,
      count: null,
      referenceId: null,
      stopReason: null,
      ...payload,
    },
    redacted: false,
  };
}
