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

  return (
    <div ref={view} className="timeline-view" tabIndex={0} aria-label="Thread timeline" onScroll={(event) => {
      const element = event.currentTarget;
      followLatest.current = element.scrollHeight - element.scrollTop - element.clientHeight < 96;
    }}>
      <div className="recovery-banner" role="alert" hidden={!detail.recoveryRequired || detail.turns.some((turn) => turn.recoveryRequired)}>Timeline consistency requires an authoritative reload.</div>
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
  const messages = projectConversationMessages(items);
  const conversationAvailable = messages.length > 0;
  const visible = mode === "conversation" && conversationAvailable ? messages : items;

  return (
    <div className="timeline-browser">
      <div className="conversation-view-actions">
        <button type="button" aria-pressed={mode === "activity"} onClick={() => setMode((current) => current === "activity" ? "conversation" : "activity")}>
          {mode === "activity" ? <MessageSquareText size={14} aria-hidden="true" /> : <Activity size={14} aria-hidden="true" />}
          {mode === "activity" ? "Back to conversation" : `Activity ${items.length}`}
        </button>
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
