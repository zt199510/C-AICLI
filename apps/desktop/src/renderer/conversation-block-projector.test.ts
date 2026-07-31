import { describe, expect, it } from "vitest";
import type { ThreadDetailData, TimelineItemData, TurnSummaryData } from "../generated/desktop-contracts";
import {
  FROZEN_CONVERSATION_TIMELINE_TYPES,
  projectConversationBlocks,
} from "./conversation-block-projector";

describe("conversation block projector", () => {
  it("maps every frozen timeline type exactly once and fails closed for an unknown type", () => {
    const timeline = [
      ...FROZEN_CONVERSATION_TIMELINE_TYPES.map((type, index) => timelineItem(index + 1, type)),
      timelineItem(99, "future.timeline-event"),
    ];

    const blocks = projectConversationBlocks(detail(turn(), timeline));
    const audited = blocks.flatMap((block) => block.auditItems.map((item) => item.itemId));

    expect(new Set(audited).size).toBe(timeline.length);
    expect(audited).toHaveLength(timeline.length);
    expect(blocks.filter((block) => block.kind === "audit-fallback")).toHaveLength(1);
  });

  it("keeps one stable assistant identity when tool events separate stream and final events", () => {
    const timeline = [
      timelineItem(1, "user.message", { text: "Question" }),
      timelineItem(2, "assistant.message", {
        text: "Partial answer",
        attempt: 1,
        assistantMessageId: "assistant-stable",
      }),
      timelineItem(3, "tool.started", { name: "workspace.read" }),
      timelineItem(4, "tool.completed", { name: "workspace.read" }),
      timelineItem(5, "assistant.final", {
        text: "Final answer",
        attempt: 1,
        assistantMessageId: "assistant-stable",
      }),
    ];

    const blocks = projectConversationBlocks(detail(turn({
      status: "completed",
      completedAtUtc: "2026-07-17T00:00:08.400Z",
    }), timeline));
    const assistants = blocks.filter((block) => block.kind === "assistant-message");

    expect(assistants).toHaveLength(1);
    expect(assistants[0]).toMatchObject({
      key: "assistant:assistant-stable",
      content: "Final answer",
      contentAttempt: 1,
      lifecycle: "completed",
    });
  });

  it("replaces content when a new provider attempt starts streaming and never concatenates attempts", () => {
    const previous = timelineItem(2, "assistant.message", {
      text: "attempt-one-partial",
      attempt: 1,
      assistantMessageId: "assistant-stable",
    });
    const waiting = projectConversationBlocks(detail(turn({
      provider: {
        phase: "retry-wait",
        attempt: 1,
        attemptHasStreamContent: true,
      },
    }), [timelineItem(1, "user.message"), previous]));
    const waitingAssistant = waiting.find((block) => block.kind === "assistant-message");

    expect(waitingAssistant).toMatchObject({
      content: "attempt-one-partial",
      contentAttempt: 1,
      stalePartial: true,
      lifecycle: "retry-wait",
    });

    const streaming = projectConversationBlocks(detail(turn({
      provider: {
        phase: "streaming",
        attempt: 2,
        attemptHasStreamContent: true,
      },
    }), [
      timelineItem(1, "user.message"),
      previous,
      timelineItem(3, "assistant.message", {
        text: "attempt-two-prefix",
        attempt: 2,
        assistantMessageId: "assistant-stable",
      }),
    ]));
    const streamingAssistant = streaming.find((block) => block.kind === "assistant-message");

    expect(streamingAssistant).toMatchObject({
      content: "attempt-two-prefix",
      contentAttempt: 2,
      stalePartial: false,
      lifecycle: "streaming",
    });
    expect(streamingAssistant?.kind === "assistant-message" ? streamingAssistant.content : "")
      .not.toContain("attempt-one-partial");
  });

  it("applies the authority priority so approval suppresses running, recovery, and retry actions", () => {
    const blocks = projectConversationBlocks(detail(turn({
      status: "waiting-for-approval",
      recoveryRequired: true,
      approval: approval(),
      provider: { phase: "retry-wait", attempt: 3 },
    }), [timelineItem(1, "user.message"), timelineItem(2, "approval.requested")]));

    expect(blocks.filter((block) => block.kind === "approval")).toHaveLength(1);
    const assistant = blocks.find((block) => block.kind === "assistant-message");
    expect(assistant).toMatchObject({ lifecycle: "approval" });
    expect(blocks.filter((block) => block.kind === "recovery")).toHaveLength(0);
  });

  it("projects generic failure, retry exhaustion, cancellation, and recovery as distinct states", () => {
    const states = [
      turn({ status: "failed", provider: { phase: "failed", retryExhausted: false } }),
      turn({ status: "failed", provider: { phase: "failed", retryExhausted: true } }),
      turn({ status: "canceled", provider: { phase: "streaming" } }),
      turn({ status: "failed", recoveryRequired: true, provider: { phase: "failed" } }),
    ];

    expect(states.map((value) => {
      const assistant = projectConversationBlocks(detail(value, [timelineItem(1, "user.message")]))
        .find((block) => block.kind === "assistant-message");
      return assistant?.kind === "assistant-message" ? assistant.lifecycle : null;
    })).toEqual(["failed", "retry-exhausted", "canceled", "recovery-required"]);
  });
});

type TurnOverrides = Partial<Omit<TurnSummaryData, "provider">> & {
  readonly provider?: Partial<TurnSummaryData["provider"]>;
};

function turn(overrides: TurnOverrides = {}): TurnSummaryData {
  const provider = {
    phase: "connecting",
    attempt: 1,
    maxAdditionalRetries: 5,
    attemptHasStreamContent: false,
    assistantMessageId: "assistant-stable",
    errorCategory: null,
    retryable: null,
    safeErrorMessage: null,
    retryExhausted: false,
    ...overrides.provider,
  };
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
    timelineLastSequence: 20,
    timelineItemCount: 20,
    recoveryRequired: false,
    approval: null,
    clientMessageId: "intent-1",
    ...overrides,
    provider,
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
    operation: "npm install",
    targetClass: "workspace",
    safeSummary: "Install workspace dependencies",
    createdAtUtc: "2026-07-17T00:00:02.000Z",
    expiresAtUtc: "2026-07-17T00:05:02.000Z",
  } as const;
}

function detail(value: TurnSummaryData, timeline: readonly TimelineItemData[]): ThreadDetailData {
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
    timeline,
    nextSequence: null,
    timelineTruncated: false,
    recoveryRequired: false,
  };
}

function timelineItem(
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
    status: type.endsWith("started") ? "running" : "completed",
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
      phase: null,
      attempt: null,
      maxAdditionalRetries: null,
      attemptHasStreamContent: null,
      assistantMessageId: null,
      errorCategory: null,
      retryable: null,
      safeErrorMessage: null,
      retryExhausted: null,
      ...payload,
    },
    redacted: false,
  };
}
