import { describe, expect, it } from "vitest";
import type { ThreadSummaryData } from "../generated/desktop-contracts";
import {
  buildThreadSidebarGroups,
  filterThreads,
  getThreadStatusPresentation,
  groupThreadsByUpdatedAt,
  matchesThreadSearch,
  normalizeThreadSearchText,
  type ThreadStatus,
} from "./thread-sidebar-model";

describe("thread sidebar model", () => {
  it.each([
    ["idle", "空闲", "对话状态：空闲", "neutral", true],
    ["running", "运行中", "对话状态：运行中", "active", true],
    ["waiting-for-approval", "等待审批", "对话状态：等待审批", "attention", true],
    ["canceling", "正在取消", "对话状态：正在取消", "attention", true],
    ["canceled", "已取消", "对话状态：已取消", "muted", false],
    ["failed", "失败", "对话状态：失败", "danger", false],
    ["completed", "已完成", "对话状态：已完成", "success", false],
    ["archived", "已归档", "对话状态：已归档", "muted", false],
  ] as const)("maps %s to a Chinese accessible presentation", (status, label, ariaLabel, tone, active) => {
    expect(getThreadStatusPresentation(status)).toEqual({ status, label, ariaLabel, tone, active });
  });

  it("fails closed for an unknown status", () => {
    expect(getThreadStatusPresentation("active")).toEqual({
      status: "unknown",
      label: "未知",
      ariaLabel: "对话状态：未知",
      tone: "neutral",
      active: false,
    });
  });

  it("applies the reviewed filters and keeps archived records out of the default view", () => {
    const threads = [
      summary("idle", "idle"),
      summary("running", "running"),
      summary("approval", "waiting-for-approval"),
      summary("canceling", "canceling"),
      summary("canceled", "canceled"),
      summary("failed", "failed"),
      summary("completed", "completed"),
      summary("archived-status", "archived"),
      summary("archived-date", "completed", { archivedAtUtc: isoLocal(2026, 8, 2) }),
    ];

    expect(ids(filterThreads(threads))).toEqual([
      "idle", "running", "approval", "canceling", "canceled", "failed", "completed",
    ]);
    expect(ids(filterThreads(threads, "active"))).toEqual(["idle", "running", "approval", "canceling"]);
    expect(ids(filterThreads(threads, "completed"))).toEqual(["completed"]);
    expect(ids(filterThreads(threads, "failed"))).toEqual(["failed"]);
    expect(ids(filterThreads(threads, "archived"))).toEqual(["archived-status", "archived-date"]);
  });

  it("normalizes Unicode width, case, and whitespace for title-only search", () => {
    const target = summary("target", "idle", { title: "Review　 API   变更" });
    const other = summary("other", "idle", { title: "Inspect database" });

    expect(normalizeThreadSearchText("  ＲＥＶＩＥＷ　　API  ")).toBe("review api");
    expect(matchesThreadSearch(target, "  ｒｅｖｉｅｗ api ")).toBe(true);
    expect(matchesThreadSearch(target, "target")).toBe(false);
    expect(matchesThreadSearch(target, "   ")).toBe(true);
    expect(ids(filterThreads([target, other], "all", "ＡＰＩ 变更"))).toEqual(["target"]);
  });

  it("groups by local calendar days and sorts each group by updatedAtUtc descending", () => {
    const now = new Date(2026, 7, 3, 14, 0, 0);
    const threads = [
      summary("older", "idle", { updatedAtUtc: isoLocal(2026, 7, 27, 20) }),
      summary("today-old", "idle", { updatedAtUtc: isoLocal(2026, 8, 3, 8) }),
      summary("recent-new", "idle", { updatedAtUtc: isoLocal(2026, 8, 1, 18) }),
      summary("yesterday", "idle", { updatedAtUtc: isoLocal(2026, 8, 2, 12) }),
      summary("today-new", "idle", { updatedAtUtc: isoLocal(2026, 8, 3, 13) }),
      summary("recent-boundary", "idle", { updatedAtUtc: isoLocal(2026, 7, 28, 1) }),
    ];

    const groups = groupThreadsByUpdatedAt(threads, now);
    expect(groups.map(({ key, label }) => [key, label])).toEqual([
      ["today", "今天"],
      ["yesterday", "昨天"],
      ["recent7", "最近 7 天"],
      ["older", "更早"],
    ]);
    expect(groupIds(groups)).toEqual({
      today: ["today-new", "today-old"],
      yesterday: ["yesterday"],
      recent7: ["recent-new", "recent-boundary"],
      older: ["older"],
    });
  });

  it("preserves input order when updated timestamps are equal", () => {
    const timestamp = isoLocal(2026, 8, 3, 9);
    const groups = groupThreadsByUpdatedAt([
      summary("tie-b", "idle", { updatedAtUtc: timestamp }),
      summary("tie-a", "idle", { updatedAtUtc: timestamp }),
    ], new Date(2026, 7, 3, 14, 0, 0));

    expect(ids(groups[0]?.threads ?? [])).toEqual(["tie-b", "tie-a"]);
  });

  it("builds filtered and searched groups without mutating the source", () => {
    const now = new Date(2026, 7, 3, 14, 0, 0);
    const threads = [
      summary("match", "running", { title: "检查 API", updatedAtUtc: isoLocal(2026, 8, 3, 10) }),
      summary("wrong-title", "running", { title: "检查 UI", updatedAtUtc: isoLocal(2026, 8, 3, 12) }),
      summary("wrong-status", "completed", { title: "检查 API", updatedAtUtc: isoLocal(2026, 8, 3, 13) }),
      summary("archived", "archived", { title: "检查 API", updatedAtUtc: isoLocal(2026, 8, 3, 14) }),
    ];
    const originalOrder = ids(threads);

    const groups = buildThreadSidebarGroups(threads, { filter: "active", query: "api", now });

    expect(groupIds(groups)).toEqual({ today: ["match"] });
    expect(ids(threads)).toEqual(originalOrder);
  });
});

function summary(
  threadId: string,
  status: ThreadStatus,
  overrides: Partial<ThreadSummaryData> = {},
): ThreadSummaryData {
  const timestamp = isoLocal(2026, 8, 3, 10);
  return {
    threadId,
    revision: 1,
    workspaceId: "workspace-1",
    title: `Title ${threadId}`,
    status,
    createdAtUtc: timestamp,
    updatedAtUtc: timestamp,
    archivedAtUtc: null,
    turnCount: 1,
    timelineItemCount: 1,
    activeTurnId: null,
    origin: { kind: "desktop", sourceKind: null, sourceId: null, sourceFingerprint: null },
    ...overrides,
  };
}

function isoLocal(year: number, month: number, day: number, hour = 12): string {
  return new Date(year, month - 1, day, hour, 0, 0).toISOString();
}

function ids(threads: readonly ThreadSummaryData[]): readonly string[] {
  return threads.map((thread) => thread.threadId);
}

function groupIds(groups: readonly { readonly key: string; readonly threads: readonly ThreadSummaryData[] }[]) {
  return Object.fromEntries(groups.map((group) => [group.key, ids(group.threads)]));
}
