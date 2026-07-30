import { Activity, MessageSquareText } from "lucide-react";
import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import type { ThreadDetailData, TimelineItemData } from "../generated/desktop-contracts";
import type { QueryStatus } from "./desktop-state";
import { TimelineProjectionBlock } from "./TimelineProjectionBlock";
import { projectTimeline } from "./timeline-projection";

const TIMELINE_WINDOW_SIZE = 80;

export interface TimelineViewProps {
  detail: ThreadDetailData | null;
  status: QueryStatus;
  error: string | null;
  controls?: ReactNode;
  onLoadMore(): void;
}

export function TimelineView({ detail, status, error, controls, onLoadMore }: TimelineViewProps) {
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
  if (!detail) return (
    <div className="state-card">
      <div className="state-kicker">Conversation</div>
      <h1>New conversation</h1>
      <p>Type a message below. The conversation will be created when you send it.</p>
    </div>
  );

  const messageCount = items.filter((item) => isConversationMessage(item.type)).length;
  return (
    <div ref={view} className="timeline-view" tabIndex={0} aria-label="Thread timeline" onScroll={(event) => {
      const element = event.currentTarget;
      followLatest.current = element.scrollHeight - element.scrollTop - element.clientHeight < 96;
    }}>
      <div className="thread-context">
        <div><strong>{detail.thread.title || "Untitled thread"}</strong><span className={`status-chip status-${detail.thread.status.toLowerCase()}`}>{detail.thread.status}</span></div>
        <div>{detail.turns.length} turns · {detail.timeline.length} loaded items</div>
      </div>
      <div className="conversation-context-summary" aria-label="Conversation context">
        <span className="context-summary-label"><MessageSquareText size={15} aria-hidden="true" /> Conversation context</span>
        <span>{detail.turns.length} turns</span>
        <span>{messageCount} messages</span>
        <span>{detail.timeline.length} events loaded</span>
      </div>
      <div className="recovery-banner" role="alert" hidden={!detail.recoveryRequired}>Timeline consistency requires an authoritative reload.</div>
      {items.length === 0 ? <div className="empty-timeline">This conversation has no messages yet. Use the composer below to begin.</div>
        : <TimelineBrowser items={items} />}
      {controls}
      {detail.timelineTruncated && detail.nextSequence !== null && (
        <div className="load-more"><button className="command-button" type="button" disabled={status === "loading"} onClick={onLoadMore}>{status === "loading" ? "Loading…" : "Load newer items"}</button></div>
      )}
    </div>
  );
}

function TimelineBrowser({ items }: { items: readonly TimelineItemData[] }) {
  const [mode, setMode] = useState<"conversation" | "activity">("conversation");
  const messages = items.filter((item) => isConversationMessage(item.type));
  const conversationAvailable = messages.length > 0;
  const visible = mode === "conversation" && conversationAvailable ? messages : items;

  return (
    <div className="timeline-browser">
      <div className="timeline-mode-switcher" role="tablist" aria-label="Timeline view">
        <button type="button" role="tab" aria-selected={mode === "conversation"} onClick={() => setMode("conversation")} disabled={!conversationAvailable}><MessageSquareText size={15} aria-hidden="true" /> Conversation <span>{messages.length}</span></button>
        <button type="button" role="tab" aria-selected={mode === "activity"} onClick={() => setMode("activity")}><Activity size={15} aria-hidden="true" /> Activity <span>{items.length}</span></button>
      </div>
      {!conversationAvailable && <div className="conversation-empty-note">No message records are available yet. Showing thread activity.</div>}
      <TurnProjection key={mode} items={visible} />
    </div>
  );
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
