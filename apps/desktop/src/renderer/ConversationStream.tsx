import { Bot, UserRound } from "lucide-react";
import type { ThreadDetailData } from "../generated/desktop-contracts";
import { ApprovalCard } from "./ApprovalCard";
import { AssistantMessage } from "./AssistantMessage";
import { assistantAnnouncement } from "./AssistantStatusLine";
import {
  projectConversationBlocks,
  type ConversationBlock,
} from "./conversation-block-projector";
import { RecoveryActions } from "./RecoveryActions";
import { TimelineItem } from "./TimelineItem";
import { ToolActivityGroup } from "./ToolActivityGroup";
import type { OptimisticExchange } from "./use-desktop-controller";

export interface ConversationMutationProps {
  readonly onApproval?: (
    turnId: string,
    requestId: string,
    approvalRevision: number,
    turnRevision: number,
    decision: "approve" | "deny",
  ) => Promise<string | null>;
  readonly onResume?: (turnId: string, turnRevision: number) => Promise<string | null>;
  readonly onRestart?: (turnId: string, turnRevision: number) => Promise<string | null>;
}

export function ConversationStream({
  detail,
  optimisticExchanges,
  onRestoreOptimistic,
  onApproval,
  onResume,
  onRestart,
}: {
  readonly detail: ThreadDetailData;
  readonly optimisticExchanges: readonly OptimisticExchange[];
  readonly onRestoreOptimistic?: (localId: string) => void;
} & ConversationMutationProps) {
  const blocks = projectConversationBlocks(detail);
  const authoritativeClientIds = new Set(detail.turns
    .map((turn) => turn.clientMessageId)
    .filter((value): value is string => Boolean(value)));
  const local = optimisticExchanges.filter((item) =>
    !item.authorityId || !authoritativeClientIds.has(item.authorityId));
  const latestAssistant = [...blocks].reverse().find((block) => block.kind === "assistant-message");

  return (
    <div className="conversation-stream" data-conversation-block-count={blocks.length}>
      <div className="sr-only" aria-live="polite" aria-atomic="true">
        {latestAssistant?.kind === "assistant-message" && assistantAnnouncement(latestAssistant)
          ? `状态更新：${assistantAnnouncement(latestAssistant)}`
          : ""}
      </div>
      <div className="timeline-items">
        {blocks.map((block) => renderBlock(block, { onApproval, onResume, onRestart }))}
        <OptimisticProjection items={local} onRestore={onRestoreOptimistic} />
      </div>
    </div>
  );
}

function renderBlock(block: ConversationBlock, mutations: ConversationMutationProps) {
  if (block.kind === "user-message") {
    return <TimelineItem key={block.key} item={block.item} projectionKind="message" />;
  }
  if (block.kind === "assistant-message") {
    return <AssistantMessage key={block.key} block={block} onRestart={mutations.onRestart} />;
  }
  if (block.kind === "tool-group") {
    return <ToolActivityGroup key={block.key} block={block} />;
  }
  if (block.kind === "approval") {
    return <ApprovalCard key={block.key} block={block} onApproval={mutations.onApproval} />;
  }
  if (block.kind === "recovery") {
    return <RecoveryActions key={block.key} block={block} onResume={mutations.onResume} onRestart={mutations.onRestart} />;
  }
  return null;
}

function OptimisticProjection({
  items,
  onRestore,
}: {
  readonly items: readonly OptimisticExchange[];
  readonly onRestore?: (localId: string) => void;
}) {
  return (
    <>
      {items.map((item) => (
        <div className="optimistic-exchange" data-optimistic-id={item.localId} key={item.localId}>
          <article className="conversation-message message-user optimistic-message">
            <div className="conversation-message-identity" aria-hidden="true"><UserRound size={15} /></div>
            <div className="conversation-message-copy">
              <header className="conversation-message-meta"><strong>You</strong></header>
              <div className="conversation-message-content">{item.text}</div>
            </div>
          </article>
          <article className={`conversation-message message-assistant assistant-message lifecycle-${item.error ? "failed" : "connecting"}`} data-assistant-message-id={`optimistic:${item.localId}`}>
            <div className="conversation-message-identity" aria-hidden="true"><Bot size={15} /></div>
            <div className="conversation-message-copy">
              <header className="conversation-message-meta"><strong>C-AICLI</strong></header>
              <div className={item.error ? "assistant-error-summary" : "assistant-status-line assistant-status-connecting"}>
                {!item.error ? <span className="assistant-status-dot" aria-hidden="true" /> : null}
                <span>{item.error ?? "正在连接"}</span>
              </div>
              {item.error && onRestore ? <button className="command-button restore-input" type="button" onClick={() => onRestore(item.localId)}>恢复输入</button> : null}
            </div>
          </article>
        </div>
      ))}
    </>
  );
}
