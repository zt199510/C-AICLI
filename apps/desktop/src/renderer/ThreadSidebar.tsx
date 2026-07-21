import { Archive, Check, Pencil, Plus, X } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
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
  const createTrigger = useRef<HTMLButtonElement>(null);
  const renameTriggers = useRef(new Map<string, HTMLButtonElement>());
  const archiveTriggers = useRef(new Map<string, HTMLButtonElement>());
  const pendingFocus = useRef<(() => HTMLElement | null) | null>(null);
  const visible = useMemo(() => props.threads.filter((thread) => matchesFilter(thread, filter)), [filter, props.threads]);

  async function submit(operation: () => Promise<string | null>, complete: () => void) {
    setPending(true);
    setCommandError(null);
    const error = await operation();
    setPending(false);
    if (error) setCommandError(error);
    else complete();
  }

  useEffect(() => {
    const resolve = pendingFocus.current;
    if (!resolve) return;
    pendingFocus.current = null;
    resolve()?.focus();
  }, [creating, editing, confirmArchive]);

  function closeCreate() { pendingFocus.current = () => createTrigger.current; setCreating(false); }
  function closeRename(threadId: string) { pendingFocus.current = () => renameTriggers.current.get(threadId) ?? null; setEditing(null); }
  function closeArchive(threadId: string) { pendingFocus.current = () => archiveTriggers.current.get(threadId) ?? null; setConfirmArchive(null); }

  return (
    <div className="thread-navigation">
      <div className="thread-actions">
        <select aria-label="Filter threads" value={filter} onChange={(event) => setFilter(event.target.value as Filter)}>
          <option value="all">All</option><option value="active">Active</option><option value="completed">Completed</option>
          <option value="failed">Failed</option><option value="archived">Archived</option>
        </select>
        <button ref={createTrigger} className="icon-button" type="button" aria-label="Create thread" title="Create thread" onClick={() => setCreating(true)}>
          <Plus size={16} aria-hidden="true" />
        </button>
      </div>
      {creating && (
        <form className="thread-form" onKeyDown={(event) => { if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); closeCreate(); } }} onSubmit={(event) => {
          event.preventDefault();
          if (!createTitle.trim() || pending) return;
          void submit(() => props.onCreate(createTitle.trim()), () => { setCreateTitle(""); closeCreate(); });
        }}>
          <label htmlFor="create-thread-title">Thread title</label>
          <input id="create-thread-title" autoFocus maxLength={512} value={createTitle} onChange={(event) => setCreateTitle(event.target.value)} />
          <span className="byte-count">{new TextEncoder().encode(createTitle).length}/512 bytes</span>
          <div className="form-actions"><button type="submit" disabled={pending || !createTitle.trim()}><Check size={14} aria-hidden="true" />Create</button><button type="button" onClick={closeCreate}><X size={14} aria-hidden="true" />Cancel</button></div>
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
              <form className="thread-form compact" onKeyDown={(event) => { if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); closeRename(entry.threadId); } }} onSubmit={(event) => {
                event.preventDefault();
                if (!editTitle.trim() || pending) return;
                void submit(() => props.onRename(entry.threadId, entry.revision, editTitle.trim()), () => closeRename(entry.threadId));
              }}>
                <label className="sr-only" htmlFor={`rename-${entry.threadId}`}>Rename thread</label>
                <input id={`rename-${entry.threadId}`} autoFocus value={editTitle} maxLength={512} onChange={(event) => setEditTitle(event.target.value)} />
                <div className="form-actions"><button type="submit" aria-label="Save thread title"><Check size={14} aria-hidden="true" /></button><button type="button" aria-label="Cancel rename" onClick={() => closeRename(entry.threadId)}><X size={14} aria-hidden="true" /></button></div>
              </form>
            ) : (
              <>
                <button className="thread-select" type="button" onClick={() => props.onSelect(entry.threadId)} aria-current={props.selectedThreadId === entry.threadId ? "true" : undefined}>
                  <span className="thread-title">{entry.title || "Untitled thread"}</span>
                  <span className="thread-meta"><span className={`status-chip status-${safeStatus(entry.status)}`}>{entry.status}</span><span>{entry.timelineItemCount} items</span></span>
                </button>
                <div className="row-actions">
                  <button ref={(value) => { if (value) renameTriggers.current.set(entry.threadId, value); else renameTriggers.current.delete(entry.threadId); }} className="icon-button" type="button" title="Rename thread" aria-label={`Rename ${entry.title}`} onClick={() => { setEditing(entry.threadId); setEditTitle(entry.title); }}><Pencil size={14} aria-hidden="true" /></button>
                  {!entry.archivedAtUtc && <button ref={(value) => { if (value) archiveTriggers.current.set(entry.threadId, value); else archiveTriggers.current.delete(entry.threadId); }} className="icon-button" type="button" title="Archive thread" aria-label={`Archive ${entry.title}`} onClick={() => setConfirmArchive(entry.threadId)}><Archive size={14} aria-hidden="true" /></button>}
                </div>
              </>
            )}
            {confirmArchive === entry.threadId && (
              <div className="archive-confirm" role="alertdialog" aria-modal="true" aria-labelledby={`archive-title-${entry.threadId}`} onKeyDown={(event) => { if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); closeArchive(entry.threadId); } }}>
                <span id={`archive-title-${entry.threadId}`}>Archive this thread?</span>
                <button autoFocus type="button" disabled={pending} onClick={() => void submit(() => props.onArchive(entry.threadId, entry.revision), () => closeArchive(entry.threadId))}>Archive</button>
                <button type="button" onClick={() => closeArchive(entry.threadId)}>Cancel</button>
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
