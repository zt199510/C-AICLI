import type {
  ApprovalRequestData,
  ThreadDetailData,
  TimelineItemData,
  TurnSummaryData,
} from "../generated/desktop-contracts";

export const FROZEN_CONVERSATION_TIMELINE_TYPES = [
  "user.message",
  "assistant.message",
  "assistant.final",
  "provider.attempt",
  "plan.updated",
  "tool.started",
  "tool.completed",
  "command.started",
  "command.completed",
  "approval.requested",
  "approval.resolved",
  "verification.completed",
  "changes.updated",
  "report.available",
  "artifact.available",
  "warning.raised",
  "turn.completed",
] as const;

export type AssistantLifecycle =
  | "connecting"
  | "thinking"
  | "streaming"
  | "retry-wait"
  | "approval"
  | "canceling"
  | "canceled"
  | "completed"
  | "retry-exhausted"
  | "recovery-required"
  | "failed";

interface ConversationBlockBase {
  readonly key: string;
  readonly turnId: string;
  readonly sequence: number;
  readonly auditItems: readonly TimelineItemData[];
}

export interface UserMessageBlock extends ConversationBlockBase {
  readonly kind: "user-message";
  readonly item: TimelineItemData;
  readonly content: string;
}

export interface AssistantMessageBlock extends ConversationBlockBase {
  readonly kind: "assistant-message";
  readonly assistantMessageId: string;
  readonly turn: TurnSummaryData;
  readonly lifecycle: AssistantLifecycle;
  readonly content: string;
  readonly contentAttempt: number | null;
  readonly stalePartial: boolean;
}

export interface ToolActivityBlock extends ConversationBlockBase {
  readonly kind: "tool-group";
  readonly status: "queued" | "running" | "failed" | "completed";
  readonly summary: string;
  readonly defaultExpanded: boolean;
}

export interface ApprovalBlock extends ConversationBlockBase {
  readonly kind: "approval";
  readonly approval: ApprovalRequestData | null;
  readonly state: "active" | "resolved";
  readonly summary: string;
}

export interface WarningBlock extends ConversationBlockBase {
  readonly kind: "warning";
  readonly summary: string;
}

export interface ResultSummaryBlock extends ConversationBlockBase {
  readonly kind: "result";
  readonly summary: string;
}

export interface RecoveryBlock extends ConversationBlockBase {
  readonly kind: "recovery";
  readonly turn: TurnSummaryData;
  readonly summary: string;
}

export interface AuditFallbackBlock extends ConversationBlockBase {
  readonly kind: "audit-fallback";
  readonly item: TimelineItemData;
}

export type ConversationBlock =
  | UserMessageBlock
  | AssistantMessageBlock
  | ToolActivityBlock
  | ApprovalBlock
  | WarningBlock
  | ResultSummaryBlock
  | RecoveryBlock
  | AuditFallbackBlock;

const knownTypes = new Set<string>(FROZEN_CONVERSATION_TIMELINE_TYPES);
const activityTypes = new Set([
  "plan.updated",
  "tool.started",
  "tool.completed",
  "command.started",
  "command.completed",
  "verification.completed",
  "changes.updated",
  "report.available",
  "artifact.available",
]);

export function projectConversationBlocks(detail: ThreadDetailData): readonly ConversationBlock[] {
  const turnById = new Map(detail.turns.map((turn) => [turn.turnId, turn]));
  const timeline = deduplicateTimeline(detail.timeline);
  const itemsByTurn = new Map<string, TimelineItemData[]>();
  for (const item of timeline) {
    const current = itemsByTurn.get(item.turnId) ?? [];
    current.push(item);
    itemsByTurn.set(item.turnId, current);
  }

  const turnIds = new Set<string>([
    ...detail.turns.map((turn) => turn.turnId),
    ...timeline.map((item) => item.turnId),
  ]);
  const orderedTurnIds = [...turnIds].sort((left, right) => {
    const leftTurn = turnById.get(left);
    const rightTurn = turnById.get(right);
    if (leftTurn && rightTurn) return leftTurn.ordinal - rightTurn.ordinal;
    const leftSequence = itemsByTurn.get(left)?.[0]?.sequence ?? Number.MAX_SAFE_INTEGER;
    const rightSequence = itemsByTurn.get(right)?.[0]?.sequence ?? Number.MAX_SAFE_INTEGER;
    return leftSequence - rightSequence;
  });

  const blocks: ConversationBlock[] = [];
  for (const turnId of orderedTurnIds) {
    const turnItems = itemsByTurn.get(turnId) ?? [];
    const turn = turnById.get(turnId);
    const userItems = turnItems.filter((item) => item.type === "user.message");
    for (const item of userItems) {
      blocks.push({
        kind: "user-message",
        key: `user:${item.itemId}`,
        turnId,
        sequence: item.sequence,
        auditItems: [item],
        item,
        content: visibleText(item),
      });
    }

    const assistantAuditItems = turnItems.filter((item) =>
      item.type === "assistant.message" ||
      item.type === "assistant.final" ||
      item.type === "provider.attempt");
    if (turn) {
      const contentItem = selectAssistantContent(turn, assistantAuditItems);
      const userSequence = userItems.at(-1)?.sequence ?? turnItems[0]?.sequence ?? turn.ordinal;
      const assistantSequence = userItems.length > 0
        ? userSequence + 0.1
        : assistantAuditItems[0]?.sequence ?? userSequence;
      const lifecycle = assistantLifecycle(turn);
      const contentAttempt = contentItem?.payload.attempt ?? null;
      blocks.push({
        kind: "assistant-message",
        key: `assistant:${turn.provider.assistantMessageId}`,
        turnId,
        sequence: assistantSequence,
        auditItems: assistantAuditItems,
        assistantMessageId: turn.provider.assistantMessageId,
        turn,
        lifecycle,
        content: contentItem ? visibleText(contentItem) : "",
        contentAttempt,
        stalePartial: Boolean(contentItem) && (
          lifecycle === "retry-wait" ||
          (!isTerminalLifecycle(lifecycle) &&
            contentAttempt !== null &&
            contentAttempt < turn.provider.attempt)
        ),
      });
    } else {
      for (const item of assistantAuditItems) {
        blocks.push({
          kind: "audit-fallback",
          key: `audit:${item.itemId}`,
          turnId,
          sequence: item.sequence,
          auditItems: [item],
          item,
        });
      }
    }

    const activities = turnItems.filter((item) => activityTypes.has(item.type));
    if (activities.length > 0) {
      const status = activityStatus(activities);
      blocks.push({
        kind: "tool-group",
        key: `activity:${turnId}`,
        turnId,
        sequence: activities[0]!.sequence,
        auditItems: activities,
        status,
        summary: activitySummary(activities, status),
        defaultExpanded: status === "running" || status === "failed",
      });
    }

    const approvalItems = turnItems.filter((item) =>
      item.type === "approval.requested" || item.type === "approval.resolved");
    if (turn?.approval) {
      blocks.push({
        kind: "approval",
        key: `approval:${turn.approval.requestId}`,
        turnId,
        sequence: approvalItems[0]?.sequence ?? assistantAuditItems.at(-1)?.sequence ?? userSequence(turnItems),
        auditItems: approvalItems,
        approval: turn.approval,
        state: "active",
        summary: turn.approval.safeSummary,
      });
    } else if (approvalItems.length > 0) {
      const latest = approvalItems.at(-1)!;
      blocks.push({
        kind: "approval",
        key: `approval:${turnId}:${latest.itemId}`,
        turnId,
        sequence: latest.sequence,
        auditItems: approvalItems,
        approval: null,
        state: "resolved",
        summary: visibleText(latest),
      });
    }

    for (const item of turnItems.filter((value) => value.type === "warning.raised")) {
      blocks.push({
        kind: "warning",
        key: `warning:${item.itemId}`,
        turnId,
        sequence: item.sequence,
        auditItems: [item],
        summary: visibleText(item),
      });
    }

    for (const item of turnItems.filter((value) => value.type === "turn.completed")) {
      blocks.push({
        kind: "result",
        key: `result:${item.itemId}`,
        turnId,
        sequence: item.sequence,
        auditItems: [item],
        summary: visibleText(item),
      });
    }

    if (turn && assistantLifecycle(turn) === "recovery-required") {
      blocks.push({
        kind: "recovery",
        key: `recovery:${turnId}:${turn.revision}`,
        turnId,
        sequence: turnItems.at(-1)?.sequence ?? turn.ordinal,
        auditItems: [],
        turn,
        summary: turn.stopReason ?? turn.provider.safeErrorMessage ?? "执行已中断",
      });
    }

    for (const item of turnItems.filter((value) => !knownTypes.has(value.type))) {
      blocks.push({
        kind: "audit-fallback",
        key: `audit:${item.itemId}`,
        turnId,
        sequence: item.sequence,
        auditItems: [item],
        item,
      });
    }
  }

  return blocks.sort((left, right) =>
    left.sequence - right.sequence || blockRank(left.kind) - blockRank(right.kind));
}

export function assistantLifecycle(turn: TurnSummaryData): AssistantLifecycle {
  if (turn.approval) return "approval";
  if (turn.status === "canceling") return "canceling";
  if (turn.status === "canceled") return "canceled";
  if (turn.status === "failed" && turn.provider.retryExhausted) return "retry-exhausted";
  if (turn.recoveryRequired) return "recovery-required";
  if (turn.status === "failed") return "failed";
  if (turn.status === "completed") return "completed";
  if (turn.provider.phase === "retry-wait") return "retry-wait";
  if (turn.provider.phase === "streaming") return "streaming";
  if (turn.provider.phase === "thinking") return "thinking";
  return "connecting";
}

function deduplicateTimeline(items: readonly TimelineItemData[]): readonly TimelineItemData[] {
  const byIdentity = new Map<string, TimelineItemData>();
  for (const item of [...items].sort((left, right) => left.sequence - right.sequence)) {
    const identity = `${item.turnId}:${item.itemId}:${item.sequence}`;
    byIdentity.set(identity, item);
  }
  return [...byIdentity.values()];
}

function selectAssistantContent(
  turn: TurnSummaryData,
  items: readonly TimelineItemData[],
): TimelineItemData | null {
  const messages = items.filter((item) =>
    item.type === "assistant.message" || item.type === "assistant.final");
  if (messages.length === 0) return null;
  const final = [...messages].reverse().find((item) => item.type === "assistant.final");
  if (turn.status === "completed" && final) return final;

  const matchingIdentity = messages.filter((item) =>
    !item.payload.assistantMessageId ||
    item.payload.assistantMessageId === turn.provider.assistantMessageId);
  const candidates = matchingIdentity.length > 0 ? matchingIdentity : messages;
  const activeAttempt = candidates.filter((item) => item.payload.attempt === turn.provider.attempt);
  if (activeAttempt.length > 0) return activeAttempt.at(-1)!;
  const priorAttempt = candidates.filter((item) =>
    item.payload.attempt == null ||
    (typeof item.payload.attempt === "number" && item.payload.attempt <= turn.provider.attempt));
  return priorAttempt.at(-1) ?? candidates.at(-1) ?? null;
}

function visibleText(item: TimelineItemData): string {
  return item.redacted ? item.summary : item.payload.text || item.summary;
}

function activityStatus(items: readonly TimelineItemData[]): ToolActivityBlock["status"] {
  if (items.some((item) => item.status === "failed" || item.payload.succeeded === false)) return "failed";
  const latest = items.at(-1);
  if (latest?.status === "running" || latest?.type.endsWith(".started")) return "running";
  if (items.every((item) => item.status === "queued")) return "queued";
  return "completed";
}

function activitySummary(
  items: readonly TimelineItemData[],
  status: ToolActivityBlock["status"],
): string {
  const operations = new Set(items.map((item) =>
    item.payload.referenceId ?? item.payload.name ?? item.type));
  if (status === "failed") return `操作失败：${visibleText(items.at(-1)!)}`;
  if (status === "running") return `正在执行：${visibleText(items.at(-1)!)}`;
  if (status === "queued") return "准备执行";
  return `已完成 ${operations.size} 项操作`;
}

function isTerminalLifecycle(lifecycle: AssistantLifecycle): boolean {
  return lifecycle === "completed" ||
    lifecycle === "canceled" ||
    lifecycle === "failed" ||
    lifecycle === "retry-exhausted";
}

function userSequence(items: readonly TimelineItemData[]): number {
  return items[0]?.sequence ?? 1;
}

function blockRank(kind: ConversationBlock["kind"]): number {
  if (kind === "user-message") return 0;
  if (kind === "assistant-message") return 1;
  if (kind === "tool-group") return 2;
  if (kind === "approval") return 3;
  if (kind === "recovery") return 4;
  if (kind === "warning") return 5;
  if (kind === "result") return 6;
  return 7;
}
