import { Archive, Check, Pencil, Plus, X } from "lucide-react";
import { useMemo, useState } from "react";
import type { ThreadSummaryData } from "../generated/desktop-contracts";
import type { QueryStatus } from "./desktop-state";

type Filter = "all" | "active" | "completed" | "failed" | "archived";

export interface ThreadSidebarProps {
  threads: readonly ThreadSummaryData[];
  status: QueryStatus;
  error: string | null;
  truncated: boolean;
  selectedThreadId: string | null;
  onSelect(threadId: string): void;
  onCreate(title: string): Promise<string | null>;
  onRename(threadId: string, revision: number, title: string): Promise<string | null>;
  onArchive(threadId: string, revision: number): Promise<string | null>;
}

export function ThreadSidebar(props: ThreadSidebarProps) {
  const [filter, setFilter] = useState<Filter>("all");
  const [creating, setCreating] = useState(false);
  const [createTitle, setCreateTitle] = useState("");
  const [editing, setEditing] = useState<string | null>(null);
  const [editTitle, setEditTitle] = useState("");
  const [confirmArchive, setConfirmArchive] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const [commandError, setCommandError] = useState<string | null>(null);
  const visible = useMemo(() => props.threads.filter((thread) => matchesFilter(thread, filter)), [filter, props.threads]);

  async function submit(operation: () => Promise<string | null>, complete: () => void) {
    setPending(true);
    setCommandError(null);
    const error = await operation();
    setPending(false);
    if (error) setCommandError(error);
    else complete();
  }

  return (
    <div className="thread-navigation">
      <div className="thread-actions">
        <select aria-label="Filter threads" value={filter} onChange={(event) => setFilter(event.target.value as Filter)}>
          <option value="all">All</option><option value="active">Active</option><option value="completed">Completed</option>
          <option value="failed">Failed</option><option value="archived">Archived</option>
        </select>
        <button className="icon-button" type="button" aria-label="Create thread" title="Create thread" onClick={() => setCreating(true)}>
          <Plus size={16} aria-hidden="true" />
        </button>
      </div>
      {creating && (
        <form className="thread-form" onSubmit={(event) => {
          event.preventDefault();
          if (!createTitle.trim() || pending) return;
          void submit(() => props.onCreate(createTitle.trim()), () => { setCreating(false); setCreateTitle(""); });
        }}>
          <label htmlFor="create-thread-title">Thread title</label>
          <input id="create-thread-title" autoFocus maxLength={512} value={createTitle} onChange={(event) => setCreateTitle(event.target.value)} />
          <span className="byte-count">{new TextEncoder().encode(createTitle).length}/512 bytes</span>
          <div className="form-actions"><button type="submit" disabled={pending || !createTitle.trim()}><Check size={14} />Create</button><button type="button" onClick={() => setCreating(false)}><X size={14} />Cancel</button></div>
        </form>
      )}
      {commandError && <div className="inline-error" role="alert">{commandError}</div>}
      {props.truncated && <div className="capped-banner">Showing the first 200 threads.</div>}
      {props.status === "loading" && props.threads.length === 0 ? <div className="empty-list">Loading threads…</div> : null}
      {props.error ? <div className="inline-error" role="alert">{props.error}</div> : null}
      {props.status !== "loading" && visible.length === 0 ? <div className="empty-list">No threads match this view.</div> : null}
      <div className="thread-list" role="list">
        {visible.map((entry) => (
          <div className={`thread-row ${props.selectedThreadId === entry.threadId ? "selected" : ""}`} role="listitem" key={entry.threadId}>
            {editing === entry.threadId ? (
              <form className="thread-form compact" onSubmit={(event) => {
                event.preventDefault();
                if (!editTitle.trim() || pending) return;
                void submit(() => props.onRename(entry.threadId, entry.revision, editTitle.trim()), () => setEditing(null));
              }}>
                <label className="sr-only" htmlFor={`rename-${entry.threadId}`}>Rename thread</label>
                <input id={`rename-${entry.threadId}`} autoFocus value={editTitle} maxLength={512} onChange={(event) => setEditTitle(event.target.value)} />
                <div className="form-actions"><button type="submit" aria-label="Save thread title"><Check size={14} /></button><button type="button" aria-label="Cancel rename" onClick={() => setEditing(null)}><X size={14} /></button></div>
              </form>
            ) : (
              <>
                <button className="thread-select" type="button" onClick={() => props.onSelect(entry.threadId)} aria-current={props.selectedThreadId === entry.threadId ? "true" : undefined}>
                  <span className="thread-title">{entry.title || "Untitled thread"}</span>
                  <span className="thread-meta"><span className={`status-chip status-${safeStatus(entry.status)}`}>{entry.status}</span><span>{entry.timelineItemCount} items</span></span>
                </button>
                <div className="row-actions">
                  <button className="icon-button" type="button" title="Rename thread" aria-label={`Rename ${entry.title}`} onClick={() => { setEditing(entry.threadId); setEditTitle(entry.title); }}><Pencil size={14} /></button>
                  {!entry.archivedAtUtc && <button className="icon-button" type="button" title="Archive thread" aria-label={`Archive ${entry.title}`} onClick={() => setConfirmArchive(entry.threadId)}><Archive size={14} /></button>}
                </div>
              </>
            )}
            {confirmArchive === entry.threadId && (
              <div className="archive-confirm" role="alertdialog" aria-label="Confirm archive">
                <span>Archive this thread?</span>
                <button type="button" disabled={pending} onClick={() => void submit(() => props.onArchive(entry.threadId, entry.revision), () => setConfirmArchive(null))}>Archive</button>
                <button type="button" onClick={() => setConfirmArchive(null)}>Cancel</button>
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

function matchesFilter(entry: ThreadSummaryData, filter: Filter): boolean {
  if (filter === "all") return true;
  if (filter === "archived") return Boolean(entry.archivedAtUtc) || entry.status.toLowerCase() === "archived";
  if (filter === "active") return !entry.archivedAtUtc && !["completed", "failed", "archived"].includes(entry.status.toLowerCase());
  return entry.status.toLowerCase() === filter;
}

function safeStatus(status: string): string {
  const normalized = status.toLowerCase();
  return ["active", "completed", "failed", "archived"].includes(normalized) ? normalized : "unknown";
}
