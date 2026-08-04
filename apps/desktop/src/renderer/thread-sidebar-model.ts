import type { ThreadSummaryData } from "../generated/desktop-contracts";

export const THREAD_STATUSES = [
  "idle",
  "running",
  "waiting-for-approval",
  "canceling",
  "canceled",
  "failed",
  "completed",
  "archived",
] as const;

export type ThreadStatus = (typeof THREAD_STATUSES)[number];
export type ThreadStatusTone = "neutral" | "active" | "attention" | "muted" | "danger" | "success";

export interface ThreadStatusPresentation {
  readonly status: ThreadStatus | "unknown";
  readonly label: string;
  readonly ariaLabel: string;
  readonly tone: ThreadStatusTone;
  readonly active: boolean;
}

export const THREAD_FILTERS = ["all", "active", "completed", "failed", "archived"] as const;
export type ThreadFilter = (typeof THREAD_FILTERS)[number];

export const THREAD_DATE_GROUPS = ["today", "yesterday", "recent7", "older"] as const;
export type ThreadDateGroupKey = (typeof THREAD_DATE_GROUPS)[number];

export interface ThreadDateGroup {
  readonly key: ThreadDateGroupKey;
  readonly label: string;
  readonly threads: readonly ThreadSummaryData[];
}

export interface ThreadSidebarModelOptions {
  readonly filter?: ThreadFilter;
  readonly query?: string;
  readonly now?: Date;
}

const knownStatuses = new Set<string>(THREAD_STATUSES);
const activeStatuses = new Set<ThreadStatus>(["idle", "running", "waiting-for-approval", "canceling"]);

const statusPresentations: Readonly<Record<ThreadStatus, ThreadStatusPresentation>> = Object.freeze({
  idle: { status: "idle", label: "空闲", ariaLabel: "对话状态：空闲", tone: "neutral", active: true },
  running: { status: "running", label: "运行中", ariaLabel: "对话状态：运行中", tone: "active", active: true },
  "waiting-for-approval": { status: "waiting-for-approval", label: "等待审批", ariaLabel: "对话状态：等待审批", tone: "attention", active: true },
  canceling: { status: "canceling", label: "正在取消", ariaLabel: "对话状态：正在取消", tone: "attention", active: true },
  canceled: { status: "canceled", label: "已取消", ariaLabel: "对话状态：已取消", tone: "muted", active: false },
  failed: { status: "failed", label: "失败", ariaLabel: "对话状态：失败", tone: "danger", active: false },
  completed: { status: "completed", label: "已完成", ariaLabel: "对话状态：已完成", tone: "success", active: false },
  archived: { status: "archived", label: "已归档", ariaLabel: "对话状态：已归档", tone: "muted", active: false },
});

const unknownStatusPresentation: ThreadStatusPresentation = Object.freeze({
  status: "unknown",
  label: "未知",
  ariaLabel: "对话状态：未知",
  tone: "neutral",
  active: false,
});

const groupLabels: Readonly<Record<ThreadDateGroupKey, string>> = Object.freeze({
  today: "今天",
  yesterday: "昨天",
  recent7: "最近 7 天",
  older: "更早",
});

export function normalizeThreadStatus(status: string): ThreadStatus | "unknown" {
  const normalized = status.trim().toLowerCase();
  return knownStatuses.has(normalized) ? normalized as ThreadStatus : "unknown";
}

export function getThreadStatusPresentation(status: string): ThreadStatusPresentation {
  const normalized = normalizeThreadStatus(status);
  return normalized === "unknown" ? unknownStatusPresentation : statusPresentations[normalized];
}

export function isThreadArchived(thread: ThreadSummaryData): boolean {
  return thread.archivedAtUtc !== null || normalizeThreadStatus(thread.status) === "archived";
}

export function matchesThreadFilter(thread: ThreadSummaryData, filter: ThreadFilter): boolean {
  const status = normalizeThreadStatus(thread.status);
  const archived = isThreadArchived(thread);
  if (filter === "all") return !archived;
  if (filter === "archived") return archived;
  if (archived || status === "unknown") return false;
  if (filter === "active") return activeStatuses.has(status);
  return status === filter;
}

export function normalizeThreadSearchText(value: string): string {
  return value.normalize("NFKC").trim().toLowerCase().replace(/\s+/gu, " ");
}

export function matchesThreadSearch(thread: ThreadSummaryData, query: string): boolean {
  const normalizedQuery = normalizeThreadSearchText(query);
  return normalizedQuery.length === 0 || normalizeThreadSearchText(thread.title).includes(normalizedQuery);
}

export function filterThreads(
  threads: readonly ThreadSummaryData[],
  filter: ThreadFilter = "all",
  query = "",
): readonly ThreadSummaryData[] {
  const normalizedQuery = normalizeThreadSearchText(query);
  return threads.filter((thread) =>
    matchesThreadFilter(thread, filter) &&
    (normalizedQuery.length === 0 || normalizeThreadSearchText(thread.title).includes(normalizedQuery)));
}

export function sortThreadsByUpdatedAt(threads: readonly ThreadSummaryData[]): readonly ThreadSummaryData[] {
  return threads
    .map((thread, index) => ({ thread, index, timestamp: updatedTimestamp(thread) }))
    .sort((left, right) => right.timestamp - left.timestamp || left.index - right.index)
    .map(({ thread }) => thread);
}

export function groupThreadsByUpdatedAt(
  threads: readonly ThreadSummaryData[],
  now = new Date(),
): readonly ThreadDateGroup[] {
  if (!Number.isFinite(now.getTime())) throw new RangeError("now must be a valid date.");

  const todayStart = localDayStart(now, 0);
  const yesterdayStart = localDayStart(now, -1);
  const recentStart = localDayStart(now, -6);
  const buckets = new Map<ThreadDateGroupKey, ThreadSummaryData[]>(
    THREAD_DATE_GROUPS.map((key) => [key, []]),
  );

  for (const thread of sortThreadsByUpdatedAt(threads)) {
    const timestamp = updatedTimestamp(thread);
    const key: ThreadDateGroupKey = !Number.isFinite(timestamp) || timestamp < recentStart
      ? "older"
      : timestamp < yesterdayStart
        ? "recent7"
        : timestamp < todayStart
          ? "yesterday"
          : "today";
    buckets.get(key)?.push(thread);
  }

  return THREAD_DATE_GROUPS.flatMap((key) => {
    const grouped = buckets.get(key) ?? [];
    return grouped.length === 0 ? [] : [{ key, label: groupLabels[key], threads: grouped }];
  });
}

export function buildThreadSidebarGroups(
  threads: readonly ThreadSummaryData[],
  options: ThreadSidebarModelOptions = {},
): readonly ThreadDateGroup[] {
  return groupThreadsByUpdatedAt(
    filterThreads(threads, options.filter ?? "all", options.query ?? ""),
    options.now ?? new Date(),
  );
}

function updatedTimestamp(thread: ThreadSummaryData): number {
  const timestamp = Date.parse(thread.updatedAtUtc);
  return Number.isFinite(timestamp) ? timestamp : Number.NEGATIVE_INFINITY;
}

function localDayStart(now: Date, dayOffset: number): number {
  return new Date(now.getFullYear(), now.getMonth(), now.getDate() + dayOffset).getTime();
}
