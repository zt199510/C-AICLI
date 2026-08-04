import {
  Archive,
  Check,
  CircleAlert,
  CircleSlash2,
  Ellipsis,
  Filter,
  Folder,
  LoaderCircle,
  MessageSquare,
  PanelLeftClose,
  Pencil,
  Plus,
  Search,
  X,
} from "lucide-react";
import { useEffect, useMemo, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from "react";
import type { ThreadSummaryData } from "../generated/desktop-contracts";
import type { QueryStatus } from "./desktop-state";
import {
  buildThreadSidebarGroups,
  getThreadStatusPresentation,
  isThreadArchived,
  type ThreadFilter,
} from "./thread-sidebar-model";

const FILTER_LABELS: Readonly<Record<ThreadFilter, string>> = {
  all: "全部",
  active: "进行中",
  completed: "已完成",
  failed: "失败",
  archived: "已归档",
};

const FILTERS = Object.keys(FILTER_LABELS) as readonly ThreadFilter[];

type PendingOperation = { readonly threadId: string; readonly kind: "rename" | "archive" };
type CommandError = { readonly threadId: string; readonly message: string };

export interface ThreadSidebarProps {
  threads: readonly ThreadSummaryData[];
  status: QueryStatus;
  error: string | null;
  truncated: boolean;
  selectedThreadId: string | null;
  workspacePath: string | null;
  visible?: boolean;
  filter?: ThreadFilter;
  onFilterChange?(filter: ThreadFilter): void;
  onExpand?(): void;
  onCollapse(): void;
  onSelect(threadId: string): void;
  onNew(): void;
  onRename(threadId: string, revision: number, title: string): Promise<string | null>;
  onArchive(threadId: string, revision: number): Promise<string | null>;
}

export function ThreadSidebar(props: ThreadSidebarProps) {
  const [uncontrolledFilter, setUncontrolledFilter] = useState<ThreadFilter>("all");
  const [query, setQuery] = useState("");
  const [filterOpen, setFilterOpen] = useState(false);
  const [menuOpen, setMenuOpen] = useState<string | null>(null);
  const [editing, setEditing] = useState<string | null>(null);
  const [editTitle, setEditTitle] = useState("");
  const [confirmArchive, setConfirmArchive] = useState<string | null>(null);
  const [pending, setPending] = useState<PendingOperation | null>(null);
  const [commandError, setCommandError] = useState<CommandError | null>(null);
  const [groupingNow, setGroupingNow] = useState(() => new Date());
  const searchRef = useRef<HTMLInputElement>(null);
  const historyRef = useRef<HTMLDivElement>(null);
  const filterTriggerRef = useRef<HTMLButtonElement>(null);
  const firstFilterItemRef = useRef<HTMLButtonElement>(null);
  const rowButtons = useRef(new Map<string, HTMLButtonElement>());
  const menuTriggers = useRef(new Map<string, HTMLButtonElement>());
  const firstMenuItems = useRef(new Map<string, HTMLButtonElement>());
  const pendingFocus = useRef<(() => HTMLElement | null) | null>(null);
  const operationToken = useRef(0);
  const operationActive = useRef(false);
  const wasVisible = useRef(props.visible ?? true);
  const filter = props.filter ?? uncontrolledFilter;
  const visible = props.visible ?? true;

  const groups = useMemo(
    () => buildThreadSidebarGroups(props.threads, { filter, query, now: groupingNow }),
    [filter, groupingNow, props.threads, query],
  );
  const visibleThreads = useMemo(() => groups.flatMap((group) => group.threads), [groups]);
  const archivedCount = useMemo(() => props.threads.filter(isThreadArchived).length, [props.threads]);
  const workspaceName = workspaceDisplayName(props.workspacePath);

  useEffect(() => {
    const scheduleNextDay = () => {
      const now = new Date();
      const nextDay = new Date(now);
      nextDay.setHours(24, 0, 0, 50);
      return window.setTimeout(() => setGroupingNow(new Date()), nextDay.getTime() - now.getTime());
    };
    const timer = scheduleNextDay();
    return () => window.clearTimeout(timer);
  }, [groupingNow]);

  useEffect(() => {
    const previouslyVisible = wasVisible.current;
    wasVisible.current = visible;
    if (visible && !previouslyVisible) searchRef.current?.focus();
    if (!visible) {
      setFilterOpen(false);
      setMenuOpen(null);
      setEditing(null);
      setConfirmArchive(null);
    }
  }, [visible]);

  useEffect(() => {
    const resolve = pendingFocus.current;
    if (!resolve) return;
    pendingFocus.current = null;
    resolve()?.focus();
  }, [confirmArchive, editing, filterOpen, menuOpen]);

  useEffect(() => {
    if (!menuOpen) return;
    const firstItem = firstMenuItems.current.get(menuOpen);
    firstItem?.focus();
    const menu = firstItem?.closest<HTMLElement>(".thread-row-menu");
    const scroller = historyRef.current;
    if (!menu || !scroller) return;
    const menuRect = menu.getBoundingClientRect();
    const scrollerRect = scroller.getBoundingClientRect();
    if (scrollerRect.height <= 0) return;
    if (menuRect.bottom > scrollerRect.bottom - 8) scroller.scrollTop += menuRect.bottom - scrollerRect.bottom + 8;
    else if (menuRect.top < scrollerRect.top + 8) scroller.scrollTop -= scrollerRect.top - menuRect.top + 8;
  }, [menuOpen]);

  useEffect(() => {
    if (filterOpen) firstFilterItemRef.current?.focus();
  }, [filterOpen]);

  useEffect(() => {
    const focusSearch = (event: globalThis.KeyboardEvent) => {
      if (event.ctrlKey || event.metaKey) {
        if (event.key.toLowerCase() === "k") {
          event.preventDefault();
          if (visible) searchRef.current?.focus();
          else props.onExpand?.();
        } else if (event.key.toLowerCase() === "n") {
          event.preventDefault();
          startNewConversation();
        }
      }
    };
    window.addEventListener("keydown", focusSearch);
    return () => window.removeEventListener("keydown", focusSearch);
  }, [props.onExpand, props.onFilterChange, props.onNew, visible]);

  useEffect(() => {
    if (menuOpen && !visibleThreads.some((thread) => thread.threadId === menuOpen)) setMenuOpen(null);
  }, [menuOpen, visibleThreads]);

  async function submit(entry: ThreadSummaryData, kind: PendingOperation["kind"], operation: () => Promise<string | null>, complete: () => void) {
    if (pending || operationActive.current) return;
    operationActive.current = true;
    const token = ++operationToken.current;
    setPending({ threadId: entry.threadId, kind });
    setCommandError(null);
    try {
      const error = await operation();
      if (operationToken.current !== token) return;
      if (error) setCommandError({ threadId: entry.threadId, message: error });
      else complete();
    } catch {
      if (operationToken.current === token) setCommandError({ threadId: entry.threadId, message: "操作失败，请重试。" });
    } finally {
      if (operationToken.current === token) {
        operationActive.current = false;
        setPending(null);
      }
    }
  }

  function updateFilter(next: ThreadFilter) {
    setUncontrolledFilter(next);
    props.onFilterChange?.(next);
  }

  function startNewConversation() {
    setQuery("");
    updateFilter("all");
    setMenuOpen(null);
    props.onNew();
  }

  function queueFocus(resolve: () => HTMLElement | null) {
    pendingFocus.current = resolve;
  }

  function closeMenu(threadId: string, restoreFocus = true) {
    if (restoreFocus) queueFocus(() => menuTriggers.current.get(threadId) ?? null);
    setMenuOpen(null);
  }

  function openMenu(threadId: string) {
    if (pending) return;
    setFilterOpen(false);
    setEditing(null);
    setConfirmArchive(null);
    setMenuOpen(threadId);
  }

  function beginRename(entry: ThreadSummaryData) {
    setMenuOpen(null);
    setConfirmArchive(null);
    setEditing(entry.threadId);
    setEditTitle(entry.title);
    setCommandError(null);
  }

  function closeRename(threadId: string) {
    queueFocus(() => menuTriggers.current.get(threadId) ?? rowButtons.current.get(threadId) ?? null);
    setEditing(null);
  }

  function beginArchive(entry: ThreadSummaryData) {
    setMenuOpen(null);
    setEditing(null);
    setConfirmArchive(entry.threadId);
    setCommandError(null);
  }

  function closeArchive(threadId: string) {
    queueFocus(() => menuTriggers.current.get(threadId) ?? rowButtons.current.get(threadId) ?? null);
    setConfirmArchive(null);
  }

  function moveRowFocus(threadId: string, direction: -1 | 1) {
    const index = visibleThreads.findIndex((entry) => entry.threadId === threadId);
    if (index < 0) return;
    const next = visibleThreads[index + direction];
    if (next) rowButtons.current.get(next.threadId)?.focus();
  }

  function handleRowKeyDown(event: ReactKeyboardEvent<HTMLButtonElement>, entry: ThreadSummaryData) {
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      moveRowFocus(entry.threadId, event.key === "ArrowDown" ? 1 : -1);
    } else if (event.key === "F10" && event.shiftKey) {
      event.preventDefault();
      openMenu(entry.threadId);
    } else if (event.key === "F2") {
      event.preventDefault();
      beginRename(entry);
    }
  }

  function selectFilter(next: ThreadFilter) {
    updateFilter(next);
    setFilterOpen(false);
    queueFocus(() => filterTriggerRef.current);
  }

  return (
    <div className="thread-navigation" aria-busy={props.status === "loading"}>
      <header className="thread-sidebar-header">
        <div className="thread-sidebar-identity">
          <span className="thread-sidebar-mark" aria-hidden="true">C</span>
          <span className="thread-sidebar-brand-copy">
            <strong>C-AICLI</strong>
            <span title={props.workspacePath ?? "未打开工作区"}>{props.workspacePath ?? "未打开工作区"}</span>
          </span>
        </div>
        <button className="icon-button" type="button" title="收起会话侧栏" aria-label="收起会话侧栏" onClick={props.onCollapse}>
          <PanelLeftClose size={17} aria-hidden="true" />
        </button>
      </header>

      <div className="thread-sidebar-primary">
        <button className="new-thread-button" type="button" aria-label="新建对话" onClick={startNewConversation}>
          <Plus size={16} aria-hidden="true" />
          <span>新建对话</span>
          <kbd>Ctrl N</kbd>
        </button>

        <div className="thread-search-row">
          <Search className="thread-search-icon" size={15} aria-hidden="true" />
          <input
            ref={searchRef}
            type="search"
            aria-label="搜索对话"
            placeholder="搜索对话"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "Escape" && query) { event.preventDefault(); setQuery(""); }
              else if (event.key === "ArrowDown" && visibleThreads.length > 0) {
                event.preventDefault();
                const firstThread = visibleThreads.at(0);
                if (firstThread) rowButtons.current.get(firstThread.threadId)?.focus();
              }
            }}
          />
          {query ? (
            <button className="thread-search-clear" type="button" aria-label="清除搜索" onClick={() => { setQuery(""); searchRef.current?.focus(); }}>
              <X size={13} aria-hidden="true" />
            </button>
          ) : null}
          <button
            ref={filterTriggerRef}
            className={`thread-filter-trigger ${filter !== "all" ? "is-active" : ""}`}
            type="button"
            aria-label={`筛选对话，当前：${filter === "all" ? "全部（不含已归档）" : FILTER_LABELS[filter]}`}
            aria-haspopup="menu"
            aria-expanded={filterOpen}
            onClick={() => { setMenuOpen(null); setFilterOpen((open) => !open); }}
          >
            <Filter size={14} aria-hidden="true" />
          </button>
          {filterOpen ? (
            <div
              className="thread-filter-menu"
              role="menu"
              aria-label="筛选对话"
              onKeyDown={(event) => handleMenuKeys(event, () => {
                setFilterOpen(false);
                queueFocus(() => filterTriggerRef.current);
              })}
              onBlur={(event) => {
                if (!event.currentTarget.contains(event.relatedTarget)) setFilterOpen(false);
              }}
            >
              {FILTERS.map((entry) => (
                <button
                  key={entry}
                  ref={entry === FILTERS[0] ? firstFilterItemRef : undefined}
                  type="button"
                  role="menuitemradio"
                  aria-checked={filter === entry}
                  onClick={() => selectFilter(entry)}
                >
                  <span>{FILTER_LABELS[entry]}</span>
                  {filter === entry ? <Check size={14} aria-hidden="true" /> : null}
                </button>
              ))}
            </div>
          ) : null}
        </div>
      </div>

      {props.truncated ? <div className="capped-banner" role="status">当前仅显示最近 200 条对话，搜索结果可能不完整。</div> : null}
      {props.error ? <div className="inline-error" role="alert">{props.error}</div> : null}
      <div className="sr-only" role="status" aria-live="polite" aria-atomic="true">
        当前显示 {visibleThreads.length} 条对话
      </div>

      <div ref={historyRef} className="thread-history-scroll">
        <div className="thread-workspace-heading">
          <span><Folder size={16} aria-hidden="true" /><strong>{workspaceName}</strong></span>
        </div>

        {props.status === "loading" && props.threads.length === 0 ? <LoadingRows /> : null}
        {props.status === "ready" && visibleThreads.length === 0 ? (
          <EmptyHistory
            filter={filter}
            query={query}
            onClear={() => { setQuery(""); updateFilter("all"); searchRef.current?.focus(); }}
            onNew={startNewConversation}
          />
        ) : null}

        {groups.map((group) => (
          <section className="thread-date-group" key={group.key} aria-labelledby={`thread-date-${group.key}`}>
            <h2 id={`thread-date-${group.key}`}>{group.label}</h2>
            <div className="thread-list" role="list">
              {group.threads.map((entry) => {
                const selected = props.selectedThreadId === entry.threadId;
                const rowPending = pending?.threadId === entry.threadId;
                const archived = isThreadArchived(entry);
                return (
                  <div
                    className={`thread-row ${selected ? "selected" : ""}`}
                    role="listitem"
                    key={entry.threadId}
                    onContextMenu={(event) => { event.preventDefault(); openMenu(entry.threadId); }}
                  >
                    {editing === entry.threadId ? (
                      <form className="thread-form compact" onKeyDown={(event) => {
                        if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); closeRename(entry.threadId); }
                      }} onSubmit={(event) => {
                        event.preventDefault();
                        const title = editTitle.trim();
                        if (!title || rowPending) return;
                        void submit(entry, "rename", () => props.onRename(entry.threadId, entry.revision, title), () => closeRename(entry.threadId));
                      }}>
                        <label className="sr-only" htmlFor={`rename-${entry.threadId}`}>重命名对话</label>
                        <input id={`rename-${entry.threadId}`} autoFocus value={editTitle} maxLength={512} onChange={(event) => setEditTitle(event.target.value)} />
                        <div className="form-actions">
                          <button type="submit" disabled={rowPending} aria-label="保存对话标题"><Check size={14} aria-hidden="true" /></button>
                          <button type="button" aria-label="取消重命名" onClick={() => closeRename(entry.threadId)}><X size={14} aria-hidden="true" /></button>
                        </div>
                      </form>
                    ) : (
                      <>
                        <button
                          ref={(value) => setMapRef(rowButtons.current, entry.threadId, value)}
                          className="thread-select"
                          type="button"
                          title={`${entry.title} · ${getThreadStatusPresentation(archived ? "archived" : entry.status).label}`}
                          onClick={() => props.onSelect(entry.threadId)}
                          onKeyDown={(event) => handleRowKeyDown(event, entry)}
                          aria-current={selected ? "page" : undefined}
                        >
                          <MessageSquare size={15} aria-hidden="true" />
                          <span className="thread-title">{entry.title || "未命名对话"}</span>
                          <ThreadStatus entry={entry} />
                        </button>
                        <button
                          ref={(value) => setMapRef(menuTriggers.current, entry.threadId, value)}
                          className="thread-more-button"
                          type="button"
                          disabled={Boolean(pending)}
                          aria-label={`更多操作：${entry.title || "未命名对话"}`}
                          aria-haspopup="menu"
                          aria-expanded={menuOpen === entry.threadId}
                          onClick={() => menuOpen === entry.threadId ? closeMenu(entry.threadId) : openMenu(entry.threadId)}
                        >
                          <Ellipsis size={15} aria-hidden="true" />
                        </button>
                      </>
                    )}

                    {menuOpen === entry.threadId ? (
                      <div
                        className="thread-row-menu"
                        role="menu"
                        aria-label={`对话操作：${entry.title}`}
                        onKeyDown={(event) => handleMenuKeys(event, () => closeMenu(entry.threadId))}
                        onBlur={(event) => {
                          if (!event.currentTarget.contains(event.relatedTarget)) setMenuOpen(null);
                        }}
                      >
                        <button ref={(value) => setMapRef(firstMenuItems.current, entry.threadId, value)} type="button" role="menuitem" onClick={() => beginRename(entry)}>
                          <Pencil size={14} aria-hidden="true" />重命名
                        </button>
                        {!archived ? (
                          <button type="button" role="menuitem" onClick={() => beginArchive(entry)}>
                            <Archive size={14} aria-hidden="true" />归档
                          </button>
                        ) : null}
                      </div>
                    ) : null}

                    {confirmArchive === entry.threadId ? (
                      <div className="archive-confirm" role="alertdialog" aria-labelledby={`archive-title-${entry.threadId}`} onKeyDown={(event) => {
                        if (event.key === "Escape") {
                          event.preventDefault();
                          event.stopPropagation();
                          closeArchive(entry.threadId);
                        }
                      }}>
                        <div>
                          <strong id={`archive-title-${entry.threadId}`}>归档“{entry.title || "未命名对话"}”？</strong>
                          <span>归档后仍可在“已归档”中查看。</span>
                        </div>
                        <div className="archive-confirm-actions">
                          <button autoFocus type="button" disabled={rowPending} onClick={() => void submit(entry, "archive", () => props.onArchive(entry.threadId, entry.revision), () => closeArchive(entry.threadId))}>确认归档</button>
                          <button type="button" disabled={rowPending} onClick={() => closeArchive(entry.threadId)}>取消</button>
                        </div>
                      </div>
                    ) : null}

                    {commandError?.threadId === entry.threadId ? <div className="thread-command-error" role="alert">{commandError.message}</div> : null}
                  </div>
                );
              })}
            </div>
          </section>
        ))}
      </div>

      <footer className="thread-sidebar-footer">
        <button
          className={filter === "archived" ? "is-selected" : ""}
          type="button"
          aria-pressed={filter === "archived"}
          onClick={() => { setQuery(""); updateFilter("archived"); setFilterOpen(false); }}
        >
          <Archive size={16} aria-hidden="true" />
          <span>已归档</span>
          {archivedCount > 0 ? <span className="thread-footer-count">{archivedCount}</span> : null}
        </button>
      </footer>
    </div>
  );
}

function ThreadStatus({ entry }: { readonly entry: ThreadSummaryData }) {
  const presentation = getThreadStatusPresentation(isThreadArchived(entry) ? "archived" : entry.status);
  const icon = presentation.status === "running" || presentation.status === "canceling"
    ? <LoaderCircle className="spin" size={14} aria-hidden="true" />
    : presentation.status === "waiting-for-approval" || presentation.status === "failed" || presentation.status === "unknown"
      ? <CircleAlert size={14} aria-hidden="true" />
      : presentation.status === "canceled"
        ? <CircleSlash2 size={14} aria-hidden="true" />
        : presentation.status === "archived"
          ? <Archive size={14} aria-hidden="true" />
          : null;
  return (
    <span className={`thread-status thread-status-${presentation.tone}`}>
      {icon}
      <span className="sr-only">{presentation.ariaLabel}</span>
    </span>
  );
}

function LoadingRows() {
  return (
    <div className="thread-loading" role="status" aria-label="正在加载对话">
      {[0, 1, 2, 3].map((value) => <span key={value} aria-hidden="true" />)}
    </div>
  );
}

function EmptyHistory({ filter, query, onClear, onNew }: {
  readonly filter: ThreadFilter;
  readonly query: string;
  readonly onClear: () => void;
  readonly onNew: () => void;
}) {
  const message = query
    ? `未找到与“${query}”匹配的对话。`
    : filter === "archived"
      ? "暂无已归档对话。"
      : filter !== "all"
        ? `暂无${FILTER_LABELS[filter]}对话。`
        : "当前工作区还没有对话。";
  return (
    <div className="empty-list">
      <span>{message}</span>
      <div>
        {query || filter !== "all" ? <button type="button" onClick={onClear}>清除筛选</button> : null}
        <button type="button" onClick={onNew}>新建对话</button>
      </div>
    </div>
  );
}

function handleMenuKeys(event: ReactKeyboardEvent<HTMLDivElement>, close: () => void) {
  if (event.key === "Escape") {
    event.preventDefault();
    close();
    return;
  }
  if (!["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) return;
  const controls = [...event.currentTarget.querySelectorAll<HTMLButtonElement>("button:not(:disabled)")];
  if (controls.length === 0) return;
  event.preventDefault();
  const current = controls.findIndex((control) => control === document.activeElement);
  if (event.key === "Home") controls.at(0)?.focus();
  else if (event.key === "End") controls.at(-1)?.focus();
  else {
    const offset = event.key === "ArrowDown" ? 1 : -1;
    const next = (Math.max(0, current) + offset + controls.length) % controls.length;
    controls[next]?.focus();
  }
}

function setMapRef<T extends HTMLElement>(map: Map<string, T>, key: string, value: T | null) {
  if (value) map.set(key, value);
  else map.delete(key);
}

function workspaceDisplayName(path: string | null): string {
  if (!path) return "当前工作区";
  return path.split(/[\\/]/u).filter(Boolean).at(-1) ?? "当前工作区";
}
