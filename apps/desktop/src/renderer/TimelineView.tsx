import { Activity, ArrowDown, MessageSquareText } from "lucide-react";
import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import type { ThreadDetailData, TimelineItemData } from "../generated/desktop-contracts";
import { ConversationStream, type ConversationMutationProps } from "./ConversationStream";
import { projectConversationBlocks } from "./conversation-block-projector";
import type { QueryStatus } from "./desktop-state";
import type { OptimisticExchange } from "./use-desktop-controller";
import { TimelineProjectionBlock } from "./TimelineProjectionBlock";
import { projectTimeline } from "./timeline-projection";

const TIMELINE_WINDOW_SIZE = 80;

export interface TimelineViewProps extends ConversationMutationProps {
  readonly detail: ThreadDetailData | null;
  readonly status: QueryStatus;
  readonly error: string | null;
  readonly controls?: ReactNode;
  readonly runtimeBanner?: ReactNode;
  readonly optimisticExchanges?: readonly OptimisticExchange[];
  readonly onRestoreOptimistic?: (localId: string) => void;
  readonly onLoadMore: () => void;
}

export function TimelineView({
  detail,
  status,
  error,
  controls,
  runtimeBanner,
  optimisticExchanges = [],
  onRestoreOptimistic,
  onApproval,
  onResume,
  onRestart,
  onLoadMore,
}: TimelineViewProps) {
  const items = detail?.timeline ?? [];
  const view = useRef<HTMLDivElement>(null);
  const activeThread = useRef<string | null>(null);
  const followLatest = useRef(true);
  const [showLatest, setShowLatest] = useState(false);

  useLayoutEffect(() => {
    const element = view.current;
    const threadId = detail?.thread.threadId ?? null;
    if (!element || !threadId) return;
    const threadChanged = activeThread.current !== threadId;
    activeThread.current = threadId;
    if (threadChanged || followLatest.current) {
      element.scrollTop = element.scrollHeight;
      followLatest.current = true;
      setShowLatest(false);
    }
  }, [detail?.thread.threadId, detail?.thread.revision, items.length]);

  if (status === "error" && !detail) {
    return <div className="state-card failure-card" role="alert"><h1>History unavailable</h1><p>{error}</p></div>;
  }
  if (status === "loading" && !detail) {
    return <div className="state-card"><h1>Loading conversation history…</h1></div>;
  }
  if (!detail && optimisticExchanges.length === 0) {
    return (
      <div className="state-card">
        {runtimeBanner}
        <div className="state-kicker">Conversation</div>
        <h1>New conversation</h1>
        <p>Type a message below. The conversation will be created when you send it.</p>
      </div>
    );
  }

  return (
    <div
      ref={view}
      className="timeline-view"
      tabIndex={0}
      aria-label="Thread timeline"
      onScroll={(event) => {
        const element = event.currentTarget;
        const nearLatest = element.scrollHeight - element.scrollTop - element.clientHeight < 96;
        followLatest.current = nearLatest;
        setShowLatest(!nearLatest);
      }}
    >
      {runtimeBanner}
      {detail ? (
        <div
          className="recovery-banner"
          role="alert"
          hidden={!detail.recoveryRequired || detail.turns.some((turn) => turn.recoveryRequired)}
        >
          Timeline consistency requires an authoritative reload.
        </div>
      ) : null}
      {!detail ? (
        <OptimisticOnly items={optimisticExchanges} onRestore={onRestoreOptimistic} />
      ) : items.length === 0 && optimisticExchanges.length === 0 ? (
        <div className="empty-timeline">This conversation has no messages yet. Use the composer below to begin.</div>
      ) : (
        <TimelineBrowser
          detail={detail}
          optimisticExchanges={optimisticExchanges}
          onRestoreOptimistic={onRestoreOptimistic}
          onApproval={onApproval}
          onResume={onResume}
          onRestart={onRestart}
        />
      )}
      {controls}
      {detail?.timelineTruncated && detail.nextSequence !== null ? (
        <div className="load-more">
          <button className="command-button" type="button" disabled={status === "loading"} onClick={onLoadMore}>
            {status === "loading" ? "Loading…" : "Load newer items"}
          </button>
        </div>
      ) : null}
      {showLatest ? (
        <button className="back-to-latest" type="button" onClick={() => {
          const element = view.current;
          if (!element) return;
          element.scrollTo({ top: element.scrollHeight, behavior: "smooth" });
          followLatest.current = true;
          setShowLatest(false);
        }}>
          <ArrowDown size={14} aria-hidden="true" />回到最新
        </button>
      ) : null}
    </div>
  );
}

function TimelineBrowser({
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
  const [mode, setMode] = useState<"conversation" | "activity">("conversation");
  const projectedDetail = detail.timeline.length > TIMELINE_WINDOW_SIZE
    ? { ...detail, timeline: detail.timeline.slice(-TIMELINE_WINDOW_SIZE) }
    : detail;
  const blocks = projectConversationBlocks(projectedDetail);
  const conversationAvailable = blocks.some((block) =>
    block.kind === "user-message" || block.kind === "assistant-message") ||
    optimisticExchanges.length > 0;

  return (
    <div className="timeline-browser">
      <div className="conversation-view-actions">
        <button type="button" aria-pressed={mode === "activity"} onClick={() => setMode((current) => current === "activity" ? "conversation" : "activity")}>
          {mode === "activity" ? <MessageSquareText size={14} aria-hidden="true" /> : <Activity size={14} aria-hidden="true" />}
          {mode === "activity" ? "Back to conversation" : `Activity ${detail.timeline.length}`}
        </button>
      </div>
      {!conversationAvailable ? <div className="conversation-empty-note">No message records are available yet. Showing thread activity.</div> : null}
      {mode === "conversation" && conversationAvailable ? (
        <ConversationStream
          detail={projectedDetail}
          optimisticExchanges={optimisticExchanges}
          onRestoreOptimistic={onRestoreOptimistic}
          onApproval={onApproval}
          onResume={onResume}
          onRestart={onRestart}
        />
      ) : (
        <TurnProjection key={mode} items={detail.timeline} />
      )}
    </div>
  );
}

function OptimisticOnly({
  items,
  onRestore,
}: {
  readonly items: readonly OptimisticExchange[];
  readonly onRestore?: (localId: string) => void;
}) {
  const emptyDetail: ThreadDetailData = {
    thread: {
      threadId: "__optimistic__",
      revision: 0,
      workspaceId: "__optimistic__",
      title: "New conversation",
      status: "active",
      createdAtUtc: items[0]?.createdAtUtc ?? new Date(0).toISOString(),
      updatedAtUtc: items.at(-1)?.createdAtUtc ?? new Date(0).toISOString(),
      archivedAtUtc: null,
      turnCount: 0,
      timelineItemCount: 0,
      activeTurnId: null,
      origin: { kind: "desktop", sourceKind: null, sourceId: null, sourceFingerprint: null },
    },
    turns: [],
    timeline: [],
    nextSequence: null,
    timelineTruncated: false,
    recoveryRequired: false,
  };
  return (
    <ConversationStream
      detail={emptyDetail}
      optimisticExchanges={items}
      onRestoreOptimistic={onRestore}
    />
  );
}

function TurnProjection({ items }: { readonly items: readonly TimelineItemData[] }) {
  const [windowEnd, setWindowEnd] = useState(items.length);
  useEffect(() => setWindowEnd(items.length), [items.length]);
  const safeEnd = Math.min(windowEnd, items.length);
  const start = Math.max(0, safeEnd - TIMELINE_WINDOW_SIZE);
  const blocks = projectTimeline(items.slice(start, safeEnd));
  return (
    <div className="turn-projection">
      {items.length > TIMELINE_WINDOW_SIZE ? (
        <div className="projection-window" role="status">
          <span>Showing records {start + 1}–{safeEnd} of {items.length}</span>
          <span>
            {start > 0 ? <button type="button" onClick={() => setWindowEnd(Math.max(TIMELINE_WINDOW_SIZE, safeEnd - TIMELINE_WINDOW_SIZE))}>Earlier records</button> : null}
            {safeEnd < items.length ? <button type="button" onClick={() => setWindowEnd(items.length)}>Latest records</button> : null}
          </span>
        </div>
      ) : null}
      <div className="timeline-items">
        {blocks.map((block) => <TimelineProjectionBlock key={block.key} block={block} />)}
      </div>
    </div>
  );
}

export function projectConversationMessages(items: readonly TimelineItemData[]): readonly TimelineItemData[] {
  const messages: TimelineItemData[] = [];
  const assistantIndexByTurn = new Map<string, number>();
  for (const item of items) {
    if (item.type === "user.message") {
      messages.push(item);
      continue;
    }
    if (item.type !== "assistant.message" && item.type !== "assistant.final") continue;
    const existing = assistantIndexByTurn.get(item.turnId);
    if (existing === undefined) {
      assistantIndexByTurn.set(item.turnId, messages.length);
      messages.push(item);
    } else {
      messages[existing] = item;
    }
  }
  return messages;
}
