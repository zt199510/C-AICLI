import { memo, useState } from "react";
import type { ThreadDetailData, TimelineItemData } from "../generated/desktop-contracts";
import type { QueryStatus } from "./desktop-state";
import { TimelineItem } from "./TimelineItem";

export interface TimelineViewProps {
  detail: ThreadDetailData | null;
  status: QueryStatus;
  error: string | null;
  onLoadMore(): void;
}

export const TimelineView = memo(function TimelineView({ detail, status, error, onLoadMore }: TimelineViewProps) {
  const items = detail?.timeline ?? [];

  if (status === "error" && !detail) return <div className="state-card failure-card" role="alert"><h1>History unavailable</h1><p>{error}</p></div>;
  if (status === "loading" && !detail) return <div className="state-card"><h1>Loading thread history…</h1></div>;
  if (!detail) return <div className="state-card"><h1>Select a thread</h1><p>Resume means reading its existing history. It does not start a turn.</p></div>;

  return (
    <div className="timeline-view" tabIndex={0} aria-label="Thread timeline">
      <div className="thread-context">
        <div><strong>{detail.thread.title || "Untitled thread"}</strong><span className={`status-chip status-${detail.thread.status.toLowerCase()}`}>{detail.thread.status}</span></div>
        <div>{detail.turns.length} turns · {detail.timeline.length} loaded items</div>
      </div>
      <div className="recovery-banner" role="alert" hidden={!detail.recoveryRequired}>Timeline consistency requires an authoritative reload.</div>
      {items.length === 0 ? <div className="empty-timeline">This thread has no timeline items.</div>
        : <TimelineTurnBrowser items={items} />}
      {detail.timelineTruncated && detail.nextSequence !== null && (
        <div className="load-more"><button className="command-button" type="button" disabled={status === "loading"} onClick={onLoadMore}>{status === "loading" ? "Loading…" : "Load newer items"}</button></div>
      )}
    </div>
  );
});

function TimelineTurnBrowser({ items }: { items: readonly TimelineItemData[] }) {
  const groups = groupByTurn(items);
  const [browserOpen, setBrowserOpen] = useState(false);
  const [selectedTurnId, setSelectedTurnId] = useState<string | null>(null);
  const selected = groups.find((group) => group.turnId === selectedTurnId) ?? null;
  const latest = items.at(-1)!;
  return (
    <div className="timeline-turn-browser">
      <button className="timeline-turn-browser-toggle timeline-turn-toggle" type="button" aria-expanded={browserOpen} onClick={() => setBrowserOpen((value) => !value)}>
        Browse {groups.length} turns · {items.length} events · {latest.summary}
      </button>
      {browserOpen && <div className="timeline-turn-options">{groups.map((group, index) => (
        <button className="timeline-turn-option" type="button" key={group.turnId} aria-pressed={selectedTurnId === group.turnId} onClick={() => setSelectedTurnId(group.turnId)}>
          Turn {index + 1} · {group.items.length} events · {group.items.at(-1)!.summary}
        </button>
      ))}</div>}
      {selected && <div className="timeline-items">{selected.items.map((item) => <TimelineItem key={item.itemId} item={item} />)}</div>}
    </div>
  );
}

function groupByTurn(items: readonly TimelineItemData[]) {
  const groups = new Map<string, TimelineItemData[]>();
  for (const item of items) {
    const group = groups.get(item.turnId);
    if (group) group.push(item);
    else groups.set(item.turnId, [item]);
  }
  return [...groups].map(([turnId, groupedItems]) => ({ turnId, items: groupedItems as readonly TimelineItemData[] }));
}
