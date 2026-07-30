import { Activity, Bot, Copy, MessageSquareText, RefreshCw, UserRound } from "lucide-react";
import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import type { ThreadDetailData, TimelineItemData, TurnSummaryData } from "../generated/desktop-contracts";
import type { QueryStatus } from "./desktop-state";
import type { OptimisticExchange } from "./use-desktop-controller";
import { TimelineProjectionBlock } from "./TimelineProjectionBlock";
import { projectTimeline } from "./timeline-projection";
import { TimelineItem } from "./TimelineItem";

const TIMELINE_WINDOW_SIZE = 80;

export interface TimelineViewProps {
  detail: ThreadDetailData | null;
  status: QueryStatus;
  error: string | null;
  controls?: ReactNode;
  optimisticExchanges?: readonly OptimisticExchange[];
  onRestoreOptimistic?(localId: string): void;
  onRestart?(turnId: string, turnRevision: number): Promise<string | null>;
  onLoadMore(): void;
}

export function TimelineView({
  detail,
  status,
  error,
  controls,
  optimisticExchanges = [],
  onRestoreOptimistic,
  onRestart,
  onLoadMore,
}: TimelineViewProps) {
  const items = detail?.timeline ?? [];
  const view = useRef<HTMLDivElement>(null);
  const activeThread = useRef<string | null>(null);
  const followLatest = useRef(true);

  useLayoutEffect(() => {
    const element = view.current;
    const threadId = detail?.thread.threadId ?? null;
    if (!element || !threadId) return;
    const threadChanged = activeThread.current !== threadId;
    activeThread.current = threadId;
    if (threadChanged || followLatest.current) element.scrollTop = element.scrollHeight;
  }, [detail?.thread.threadId, items.length]);

  if (status === "error" && !detail) return <div className="state-card failure-card" role="alert"><h1>History unavailable</h1><p>{error}</p></div>;
  if (status === "loading" && !detail) return <div className="state-card"><h1>Loading conversation history…</h1></div>;
  if (!detail && optimisticExchanges.length === 0) return (
    <div className="state-card">
      <div className="state-kicker">Conversation</div>
      <h1>New conversation</h1>
      <p>Type a message below. The conversation will be created when you send it.</p>
    </div>
  );

  if (!detail) return (
    <div ref={view} className="timeline-view" tabIndex={0} aria-label="Thread timeline">
      <OptimisticProjection items={optimisticExchanges} onRestore={onRestoreOptimistic} />
    </div>
  );

  return (
    <div ref={view} className="timeline-view" tabIndex={0} aria-label="Thread timeline" onScroll={(event) => {
      const element = event.currentTarget;
      followLatest.current = element.scrollHeight - element.scrollTop - element.clientHeight < 96;
    }}>
      <div className="recovery-banner" role="alert" hidden={!detail.recoveryRequired || detail.turns.some((turn) => turn.recoveryRequired)}>Timeline consistency requires an authoritative reload.</div>
      {items.length === 0 && optimisticExchanges.length === 0 ? <div className="empty-timeline">This conversation has no messages yet. Use the composer below to begin.</div>
        : <TimelineBrowser
            detail={detail}
            optimisticExchanges={optimisticExchanges}
            onRestoreOptimistic={onRestoreOptimistic}
            onRestart={onRestart}
          />}
      {controls}
      {detail.timelineTruncated && detail.nextSequence !== null && (
        <div className="load-more"><button className="command-button" type="button" disabled={status === "loading"} onClick={onLoadMore}>{status === "loading" ? "Loading…" : "Load newer items"}</button></div>
      )}
    </div>
  );
}

function TimelineBrowser({
  detail,
  optimisticExchanges,
  onRestoreOptimistic,
  onRestart,
}: {
  detail: ThreadDetailData;
  optimisticExchanges: readonly OptimisticExchange[];
  onRestoreOptimistic?: (localId: string) => void;
  onRestart?: (turnId: string, turnRevision: number) => Promise<string | null>;
}) {
  const items = detail.timeline;
  const [mode, setMode] = useState<"conversation" | "activity">("conversation");
  const messages = projectConversationMessages(items.slice(-TIMELINE_WINDOW_SIZE));
  const conversationAvailable = messages.length > 0 || optimisticExchanges.length > 0;

  return (
    <div className="timeline-browser">
      <div className="conversation-view-actions">
        <button type="button" aria-pressed={mode === "activity"} onClick={() => setMode((current) => current === "activity" ? "conversation" : "activity")}>
          {mode === "activity" ? <MessageSquareText size={14} aria-hidden="true" /> : <Activity size={14} aria-hidden="true" />}
          {mode === "activity" ? "Back to conversation" : `Activity ${items.length}`}
        </button>
      </div>
      {!conversationAvailable && <div className="conversation-empty-note">No message records are available yet. Showing thread activity.</div>}
      {mode === "conversation" && conversationAvailable
        ? <ConversationProjection
            detail={detail}
            messages={messages}
            optimisticExchanges={optimisticExchanges}
            onRestoreOptimistic={onRestoreOptimistic}
            onRestart={onRestart}
          />
        : <TurnProjection key={mode} items={items} />}
    </div>
  );
}

function ConversationProjection({
  detail,
  messages,
  optimisticExchanges,
  onRestoreOptimistic,
  onRestart,
}: {
  detail: ThreadDetailData;
  messages: readonly TimelineItemData[];
  optimisticExchanges: readonly OptimisticExchange[];
  onRestoreOptimistic?: (localId: string) => void;
  onRestart?: (turnId: string, turnRevision: number) => Promise<string | null>;
}) {
  const turnById = new Map(detail.turns.map((turn) => [turn.turnId, turn]));
  const assistantTurns = new Set(messages
    .filter((item) => item.type === "assistant.message" || item.type === "assistant.final")
    .map((item) => item.turnId));
  const authoritativeClientIds = new Set(detail.turns
    .map((turn) => turn.clientMessageId)
    .filter((value): value is string => Boolean(value)));
  const local = optimisticExchanges.filter((item) =>
    !item.authorityId || !authoritativeClientIds.has(item.authorityId));

  return (
    <div className="turn-projection conversation-projection">
      <div className="timeline-items">
        {messages.map((item) => {
          const turn = turnById.get(item.turnId);
          if (item.type === "user.message") {
            return (
              <div className="conversation-exchange-part" key={item.itemId}>
                <TimelineItem item={item} projectionKind="message" />
                {turn && !assistantTurns.has(item.turnId)
                  ? <AssistantWorkingBlock turn={turn} item={null} onRestart={onRestart} />
                  : null}
              </div>
            );
          }
          return turn
            ? <AssistantWorkingBlock key={turn.provider.assistantMessageId} turn={turn} item={item} onRestart={onRestart} />
            : <TimelineItem key={item.itemId} item={item} projectionKind="message" />;
        })}
        <OptimisticProjection items={local} onRestore={onRestoreOptimistic} />
      </div>
    </div>
  );
}

function OptimisticProjection({
  items,
  onRestore,
}: {
  items: readonly OptimisticExchange[];
  onRestore?: (localId: string) => void;
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
          <article className={`conversation-message message-assistant assistant-working ${item.error ? "assistant-failed" : ""}`}>
            <div className="conversation-message-identity" aria-hidden="true"><Bot size={15} /></div>
            <div className="conversation-message-copy">
              <header className="conversation-message-meta"><strong>C-AICLI</strong></header>
              <div className="assistant-phase" role="status" aria-live="polite" aria-atomic="true">
                {item.error ?? "正在连接"}
              </div>
              {item.error && onRestore
                ? <button className="command-button" type="button" onClick={() => onRestore(item.localId)}>恢复输入</button>
                : null}
            </div>
          </article>
        </div>
      ))}
    </>
  );
}

function AssistantWorkingBlock({
  turn,
  item,
  onRestart,
}: {
  turn: TurnSummaryData;
  item: TimelineItemData | null;
  onRestart?: (turnId: string, turnRevision: number) => Promise<string | null>;
}) {
  const [actionMessage, setActionMessage] = useState<string | null>(null);
  const terminal = turn.status === "completed" || turn.status === "failed" || turn.status === "canceled";
  const content = item
    ? (item.redacted ? item.summary : item.payload.text || item.summary)
    : "";
  const phase = phaseText(turn);
  const duration = processingDuration(turn);
  const exhausted = turn.status === "failed" && turn.provider.retryExhausted;
  const safeError = turn.provider.safeErrorMessage ?? turn.errorCode ?? "Model connection failed.";

  async function retry() {
    if (!onRestart) return;
    setActionMessage(null);
    const result = await onRestart(turn.turnId, turn.revision);
    setActionMessage(result);
  }

  async function copyError() {
    try {
      await navigator.clipboard.writeText(safeError);
      setActionMessage("错误信息已复制");
    } catch {
      setActionMessage("无法复制错误信息");
    }
  }

  return (
    <article
      className={`conversation-message message-assistant assistant-working status-${turn.status}`}
      data-assistant-message-id={turn.provider.assistantMessageId}
      data-attempt={turn.provider.attempt}
    >
      <div className="conversation-message-identity" aria-hidden="true"><Bot size={15} /></div>
      <div className="conversation-message-copy">
        <header className="conversation-message-meta"><strong>C-AICLI</strong></header>
        {content && <div className="conversation-message-content">{content}</div>}
        <div className="assistant-phase" role="status" aria-live="polite" aria-atomic="true">{phase}</div>
        {terminal && duration ? <div className="assistant-duration">{duration}</div> : null}
        {exhausted && (
          <div className="assistant-recovery-actions">
            <button className="command-button" type="button" onClick={() => void retry()}><RefreshCw size={14} />重新尝试</button>
            <button className="command-button" type="button" onClick={() => void copyError()}><Copy size={14} />复制错误信息</button>
          </div>
        )}
        {actionMessage && <div className="inline-error" role="status">{actionMessage}</div>}
      </div>
    </article>
  );
}

function phaseText(turn: TurnSummaryData): string {
  if (turn.status === "completed") return "已完成";
  if (turn.status === "canceled") return "已停止";
  if (turn.status === "canceling") return "正在停止";
  if (turn.status === "failed" && turn.provider.retryExhausted) {
    return `连接失败，已重试 ${turn.provider.maxAdditionalRetries} 次`;
  }
  if (turn.status === "failed") return turn.provider.safeErrorMessage ?? "处理失败";
  if (turn.provider.phase === "retry-wait") {
    return `连接中断，正在重试 ${Math.min(turn.provider.attempt, turn.provider.maxAdditionalRetries)}/${turn.provider.maxAdditionalRetries}...`;
  }
  if (turn.provider.phase === "streaming") return "正在生成回复";
  if (turn.provider.phase === "thinking") return "正在思考";
  if (turn.provider.phase === "failed") return turn.provider.safeErrorMessage ?? "连接失败";
  return "正在连接";
}

function processingDuration(turn: TurnSummaryData): string | null {
  if (!turn.completedAtUtc) return null;
  const elapsed = new Date(turn.completedAtUtc).valueOf() - new Date(turn.createdAtUtc).valueOf();
  if (!Number.isFinite(elapsed) || elapsed < 0) return null;
  const seconds = (elapsed / 1000).toFixed(1);
  if (turn.status === "canceled") return `已停止 · 处理 ${seconds} 秒`;
  if (turn.status === "failed") {
    return `${turn.provider.retryExhausted ? "连接失败" : "处理失败"} · 处理 ${seconds} 秒`;
  }
  return `已处理 ${seconds} 秒`;
}

function TurnProjection({ items }: { items: readonly TimelineItemData[] }) {
  const [windowEnd, setWindowEnd] = useState(items.length);
  useEffect(() => setWindowEnd(items.length), [items.length]);
  const safeEnd = Math.min(windowEnd, items.length);
  const start = Math.max(0, safeEnd - TIMELINE_WINDOW_SIZE);
  const blocks = projectTimeline(items.slice(start, safeEnd));
  return (
    <div className="turn-projection">
      {items.length > TIMELINE_WINDOW_SIZE && (
        <div className="projection-window" role="status">
          <span>Showing records {start + 1}–{safeEnd} of {items.length}</span>
          <span>
            {start > 0 && <button type="button" onClick={() => setWindowEnd(Math.max(TIMELINE_WINDOW_SIZE, safeEnd - TIMELINE_WINDOW_SIZE))}>Earlier records</button>}
            {safeEnd < items.length && <button type="button" onClick={() => setWindowEnd(items.length)}>Latest records</button>}
          </span>
        </div>
      )}
      <div className="timeline-items">
        {blocks.map((block) => <TimelineProjectionBlock key={block.key} block={block} />)}
      </div>
    </div>
  );
}

function isConversationMessage(type: string): boolean {
  return type === "user.message" || type === "assistant.message" || type === "assistant.final";
}

export function projectConversationMessages(items: readonly TimelineItemData[]): readonly TimelineItemData[] {
  const messages: TimelineItemData[] = [];
  for (const item of items) {
    if (!isConversationMessage(item.type)) continue;
    const previous = messages.at(-1);
    const assistantUpdate = item.type === "assistant.message" || item.type === "assistant.final";
    const previousAssistant = previous?.type === "assistant.message" || previous?.type === "assistant.final";
    if (assistantUpdate && previousAssistant && previous.turnId === item.turnId) messages[messages.length - 1] = item;
    else messages.push(item);
  }
  return messages;
}
