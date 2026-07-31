import { Bot } from "lucide-react";
import type { AssistantMessageBlock } from "./conversation-block-projector";
import { AssistantStatusLine, processingDuration } from "./AssistantStatusLine";
import { RetryExhaustedActions } from "./RecoveryActions";

export function AssistantMessage({
  block,
  onRestart,
}: {
  readonly block: AssistantMessageBlock;
  readonly onRestart?: (turnId: string, turnRevision: number) => Promise<string | null>;
}) {
  const duration = processingDuration(block);
  const safeFailure = block.turn.provider.safeErrorMessage ?? block.turn.errorCode;

  return (
    <article
      className={`conversation-message message-assistant assistant-message lifecycle-${block.lifecycle}`}
      data-assistant-message-id={block.assistantMessageId}
      data-turn-id={block.turnId}
      data-attempt={block.turn.provider.attempt}
      data-content-attempt={block.contentAttempt ?? ""}
    >
      <div className="conversation-message-identity" aria-hidden="true"><Bot size={15} /></div>
      <div className="conversation-message-copy">
        <header className="conversation-message-meta"><strong>C-AICLI</strong></header>
        {block.content ? (
          <div className={`conversation-message-content ${block.stalePartial ? "assistant-partial-stale" : ""}`}>
            {block.content}
          </div>
        ) : null}
        {block.lifecycle === "failed" && safeFailure ? (
          <div className="assistant-error-summary">{safeFailure}</div>
        ) : null}
        <AssistantStatusLine block={block} />
        {duration ? <div className="assistant-duration">{duration}</div> : null}
        {block.lifecycle === "retry-exhausted" ? (
          <RetryExhaustedActions block={block} onRestart={onRestart} />
        ) : null}
      </div>
    </article>
  );
}
