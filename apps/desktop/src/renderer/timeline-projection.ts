import type { TimelineItemData } from "../generated/desktop-contracts";

export const FROZEN_TIMELINE_TYPES = [
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

export type TimelineProjectionKind =
  | "message"
  | "execution"
  | "approval"
  | "result"
  | "event"
  | "audit-fallback";

export interface TimelineProjectionBlock {
  readonly key: string;
  readonly kind: TimelineProjectionKind;
  readonly items: readonly TimelineItemData[];
}

const messageTypes = new Set(["user.message", "assistant.message", "assistant.final"]);
const executionTypes = new Set([
  "tool.started",
  "tool.completed",
  "command.started",
  "command.completed",
  "verification.completed",
]);
const approvalTypes = new Set(["approval.requested", "approval.resolved"]);
const resultTypes = new Set(["warning.raised", "turn.completed"]);
const eventTypes = new Set([
  "provider.attempt",
  "plan.updated",
  "changes.updated",
  "report.available",
  "artifact.available",
]);
const knownTypes = new Set<string>(FROZEN_TIMELINE_TYPES);

export function projectTimeline(items: readonly TimelineItemData[]): readonly TimelineProjectionBlock[] {
  const blocks: TimelineProjectionBlock[] = [];
  for (const item of items) {
    const kind = projectionKind(item.type);
    if (kind === "execution") {
      const identity = executionIdentity(item);
      const previous = blocks.at(-1);
      if (
        previous?.kind === "execution" &&
        executionIdentity(previous.items[0]!) === identity
      ) {
        blocks[blocks.length - 1] = {
          ...previous,
          items: [...previous.items, item],
        };
        continue;
      }
    }

    blocks.push({
      key: `${kind}:${item.itemId}`,
      kind,
      items: [item],
    });
  }

  return blocks;
}

function projectionKind(type: string): TimelineProjectionKind {
  if (!knownTypes.has(type)) return "audit-fallback";
  if (messageTypes.has(type)) return "message";
  if (executionTypes.has(type)) return "execution";
  if (approvalTypes.has(type)) return "approval";
  if (resultTypes.has(type)) return "result";
  if (eventTypes.has(type)) return "event";
  return "audit-fallback";
}

function executionIdentity(item: TimelineItemData): string {
  const family = item.type.split(".", 1)[0] ?? item.type;
  return `${family}:${item.payload.referenceId ?? item.payload.name ?? family}`;
}
