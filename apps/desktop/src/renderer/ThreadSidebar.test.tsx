import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { ThreadSummaryData } from "../generated/desktop-contracts";
import { ThreadSidebar } from "./ThreadSidebar";

describe("中文会话历史导航", () => {
  it("按本机日期分组、默认隐藏归档并选择现有对话", async () => {
    const select = vi.fn();
    renderSidebar({
      threads: [
        thread("running", "今天的工作", "thread-today", localDate(0)),
        thread("completed", "昨天的工作", "thread-yesterday", localDate(-1)),
        thread("archived", "归档对话", "thread-archived", localDate(-2), localDate(-2)),
      ],
      onSelect: select,
    });

    expect(screen.getByRole("heading", { name: "今天" })).toBeTruthy();
    expect(screen.getByRole("heading", { name: "昨天" })).toBeTruthy();
    expect(screen.queryByText("归档对话")).toBeNull();
    await userEvent.click(screen.getByTitle("今天的工作 · 运行中"));
    expect(select).toHaveBeenCalledWith("thread-today");
  });

  it("搜索标题、筛选合法状态并通过归档入口切换视图", async () => {
    renderSidebar({
      threads: [
        thread("running", "侧栏视觉重构", "thread-running", localDate(0)),
        thread("failed", "失败的协议检查", "thread-failed", localDate(0)),
        thread("canceled", "已取消的任务", "thread-canceled", localDate(-1)),
        thread("archived", "旧的归档讨论", "thread-archived", localDate(-2), localDate(-2)),
      ],
    });

    await userEvent.type(screen.getByRole("searchbox", { name: "搜索对话" }), "协议");
    expect(screen.getByText("失败的协议检查")).toBeTruthy();
    expect(screen.queryByText("侧栏视觉重构")).toBeNull();
    await userEvent.click(screen.getByRole("button", { name: "清除搜索" }));

    await userEvent.click(screen.getByRole("button", { name: /筛选对话/u }));
    await userEvent.click(screen.getByRole("menuitemradio", { name: "进行中" }));
    expect(screen.getByText("侧栏视觉重构")).toBeTruthy();
    expect(screen.queryByText("已取消的任务")).toBeNull();

    await userEvent.click(screen.getByRole("button", { name: /^已归档/u }));
    expect(screen.getByText("旧的归档讨论")).toBeTruthy();
    expect(screen.queryByText("侧栏视觉重构")).toBeNull();
  });

  it("支持搜索和新建对话快捷键", async () => {
    const start = vi.fn();
    renderSidebar({ threads: [thread("idle", "普通对话")], onNew: start });
    const search = screen.getByRole("searchbox", { name: "搜索对话" });

    await userEvent.keyboard("{Control>}k{/Control}");
    expect(document.activeElement).toBe(search);
    await userEvent.keyboard("{Control>}n{/Control}");
    expect(start).toHaveBeenCalledOnce();
  });

  it("侧栏隐藏时 Ctrl+K 会先展开，并在展开后聚焦搜索框", async () => {
    const expand = vi.fn();
    const { rerender } = renderSidebar({
      threads: [thread("idle", "普通对话")],
      visible: false,
      onExpand: expand,
    });
    const collapse = screen.getByRole("button", { name: "收起会话侧栏" });
    const search = screen.getByRole("searchbox", { name: "搜索对话" });
    collapse.focus();

    await userEvent.keyboard("{Control>}k{/Control}");
    expect(expand).toHaveBeenCalledOnce();
    expect(document.activeElement).toBe(collapse);

    rerender(sidebar({ threads: [thread("idle", "普通对话")], visible: true, onExpand: expand }));
    expect(document.activeElement).toBe(search);
  });

  it("菜单支持方向键、Home、End 与 Escape 导航", async () => {
    renderSidebar({ threads: [thread("completed", "键盘对话")] });
    const more = screen.getByRole("button", { name: "更多操作：键盘对话" });
    await userEvent.click(more);
    const rename = screen.getByRole("menuitem", { name: "重命名" });
    const archive = screen.getByRole("menuitem", { name: "归档" });
    expect(document.activeElement).toBe(rename);

    await userEvent.keyboard("{ArrowDown}");
    expect(document.activeElement).toBe(archive);
    await userEvent.keyboard("{Home}");
    expect(document.activeElement).toBe(rename);
    await userEvent.keyboard("{End}{Escape}");
    expect(screen.queryByRole("menu", { name: "对话操作：键盘对话" })).toBeNull();
    expect(document.activeElement).toBe(more);
  });

  it("通过统一菜单重命名和归档，并保持显式归档确认", async () => {
    const rename = vi.fn(async () => null);
    const archive = vi.fn(async () => null);
    renderSidebar({ threads: [thread("completed", "需要整理的对话")], onRename: rename, onArchive: archive });

    await userEvent.click(screen.getByRole("button", { name: "更多操作：需要整理的对话" }));
    await userEvent.click(screen.getByRole("menuitem", { name: "重命名" }));
    const title = screen.getByRole("textbox", { name: "重命名对话" });
    await userEvent.clear(title);
    await userEvent.type(title, "新的对话标题");
    await userEvent.click(screen.getByRole("button", { name: "保存对话标题" }));
    expect(rename).toHaveBeenCalledWith("thread-1", 1, "新的对话标题");

    await userEvent.click(screen.getByRole("button", { name: "更多操作：需要整理的对话" }));
    await userEvent.click(screen.getByRole("menuitem", { name: "归档" }));
    const dialog = screen.getByRole("alertdialog", { name: "归档“需要整理的对话”？" });
    expect(within(dialog).getByText("归档后仍可在“已归档”中查看。")).toBeTruthy();
    await userEvent.click(within(dialog).getByRole("button", { name: "确认归档" }));
    expect(archive).toHaveBeenCalledWith("thread-1", 1);
  });

  it("Escape 关闭菜单并把焦点归还更多按钮", async () => {
    renderSidebar({ threads: [thread("failed", "失败对话")] });
    const more = screen.getByRole("button", { name: "更多操作：失败对话" });
    await userEvent.click(more);
    expect(screen.getByRole("menu", { name: "对话操作：失败对话" })).toBeTruthy();
    await userEvent.keyboard("{Escape}");
    expect(screen.queryByRole("menu", { name: "对话操作：失败对话" })).toBeNull();
    expect(document.activeElement).toBe(more);
  });

  it("显示截断提示、中文空态和可访问状态文案", () => {
    const { rerender } = renderSidebar({ threads: [thread("waiting-for-approval", "等待确认")], truncated: true });
    expect(screen.getByText("当前仅显示最近 200 条对话，搜索结果可能不完整。")).toBeTruthy();
    expect(screen.getByText("对话状态：等待审批", { selector: ".sr-only" })).toBeTruthy();

    rerender(sidebar({ threads: [], truncated: false }));
    expect(screen.getByText("当前工作区还没有对话。")).toBeTruthy();
    expect(screen.getAllByRole("button", { name: "新建对话" })).toHaveLength(2);
  });

  it("加载失败时只显示错误，不叠加业务空态", () => {
    renderSidebar({ threads: [], status: "error", error: "会话加载失败" });
    expect(screen.getByRole("alert").textContent).toBe("会话加载失败");
    expect(screen.queryByText("当前工作区还没有对话。")).toBeNull();
  });
});

function renderSidebar(overrides: Partial<React.ComponentProps<typeof ThreadSidebar>>) {
  return render(sidebar(overrides));
}

function sidebar(overrides: Partial<React.ComponentProps<typeof ThreadSidebar>>) {
  return <ThreadSidebar
    threads={[]}
    status="ready"
    error={null}
    truncated={false}
    selectedThreadId={null}
    workspacePath="D:\\AI\\C-AICLI"
    onCollapse={vi.fn()}
    onSelect={vi.fn()}
    onNew={vi.fn()}
    onRename={vi.fn(async () => null)}
    onArchive={vi.fn(async () => null)}
    {...overrides}
  />;
}

function thread(
  status: string,
  title: string,
  threadId = "thread-1",
  updatedAtUtc = localDate(0),
  archivedAtUtc: string | null = status === "archived" ? updatedAtUtc : null,
): ThreadSummaryData {
  return {
    threadId,
    revision: 1,
    workspaceId: "workspace-1",
    title,
    status,
    createdAtUtc: updatedAtUtc,
    updatedAtUtc,
    archivedAtUtc,
    turnCount: 1,
    timelineItemCount: 14,
    activeTurnId: status === "running" ? "turn-1" : null,
    origin: { kind: "desktop", sourceKind: null, sourceId: null, sourceFingerprint: null },
  };
}

function localDate(dayOffset: number): string {
  const value = new Date();
  value.setHours(12, 0, 0, 0);
  value.setDate(value.getDate() + dayOffset);
  return value.toISOString();
}
