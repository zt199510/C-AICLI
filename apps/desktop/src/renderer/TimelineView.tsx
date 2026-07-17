import { useVirtualizer } from "@tanstack/react-virtual";
import { useRef, type RefObject } from "react";
import type { ThreadDetailData, TimelineItemData } from "../generated/desktop-contracts";
import type { QueryStatus } from "./desktop-state";
import { TimelineItem } from "./TimelineItem";

export interface TimelineViewProps {
  detail: ThreadDetailData | null;
  status: QueryStatus;
  error: string | null;
  onLoadMore(): void;
}

export function TimelineView({ detail, status, error, onLoadMore }: TimelineViewProps) {
  const scrollRef = useRef<HTMLDivElement>(null);
  const items = detail?.timeline ?? [];

  if (status === "error" && !detail) return <div className="state-card failure-card" role="alert"><h1>History unavailable</h1><p>{error}</p></div>;
  if (status === "loading" && !detail) return <div className="state-card"><h1>Loading thread history…</h1></div>;
  if (!detail) return <div className="state-card"><h1>Select a thread</h1><p>Resume means reading its existing history. It does not start a turn.</p></div>;

  return (
    <div className="timeline-view" ref={scrollRef} tabIndex={0} aria-label="Thread timeline">
      <div className="thread-context">
        <div><strong>{detail.thread.title || "Untitled thread"}</strong><span className={`status-chip status-${detail.thread.status.toLowerCase()}`}>{detail.thread.status}</span></div>
        <div>{detail.turns.length} turns · {detail.timeline.length} loaded items</div>
      </div>
      {detail.recoveryRequired && <div className="recovery-banner" role="alert">Timeline consistency requires an authoritative reload.</div>}
      {items.length === 0 ? <div className="empty-timeline">This thread has no timeline items.</div>
        : items.length <= 100 ? <div className="timeline-items">{items.map((item) => <TimelineItem key={item.itemId} item={item} />)}</div>
        : <VirtualTimelineRows items={items} scrollRef={scrollRef} />}
      {detail.timelineTruncated && detail.nextSequence !== null && (
        <div className="load-more"><button className="command-button" type="button" disabled={status === "loading"} onClick={onLoadMore}>{status === "loading" ? "Loading…" : "Load newer items"}</button></div>
      )}
    </div>
  );
}

function VirtualTimelineRows({ items, scrollRef }: { items: readonly TimelineItemData[]; scrollRef: RefObject<HTMLDivElement | null> }) {
  const virtualizer = useVirtualizer({
    count: items.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => 126,
    overscan: 8,
    initialRect: { width: 800, height: 600 },
    getItemKey: (index) => items[index]?.itemId ?? index,
  });
  return <div className="virtual-track" style={{ height: `${virtualizer.getTotalSize()}px` }}>
    {virtualizer.getVirtualItems().map((row) => {
      const item = items[row.index];
      if (!item) return null;
      return <div className="virtual-row" key={item.itemId} ref={virtualizer.measureElement} data-index={row.index} style={{ transform: `translateY(${row.start}px)` }}><TimelineItem item={item} /></div>;
    })}
  </div>;
}
