import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { ThreadSummaryData } from "../generated/desktop-contracts";
import { ThreadSidebar } from "./ThreadSidebar";

describe("thread metadata navigation", () => {
  it("filters loaded truth and selects an existing thread without starting a turn", async () => {
    const select = vi.fn();
    renderSidebar({ threads: [thread("active", null), thread("failed", null, "thread-2")] , onSelect: select });
    await userEvent.selectOptions(screen.getByRole("combobox", { name: "Filter threads" }), "failed");
    expect(screen.queryByText("Review thread")).toBeNull();
    await userEvent.click(screen.getByText("failed", { selector: ".status-chip" }).closest("button") as HTMLElement);
    expect(select).toHaveBeenCalledWith("thread-2");
  });

  it("uses explicit create and archive confirmation flows", async () => {
    const create = vi.fn(async () => null);
    const archive = vi.fn(async () => null);
    renderSidebar({ threads: [thread("completed", null)], onCreate: create, onArchive: archive });
    await userEvent.click(screen.getByRole("button", { name: "Create thread" }));
    await userEvent.type(screen.getByLabelText("Thread title"), "New review");
    await userEvent.click(screen.getByRole("button", { name: /^Create$/ }));
    expect(create).toHaveBeenCalledWith("New review");
    await userEvent.click(screen.getByRole("button", { name: "Archive Review thread" }));
    expect(screen.getByRole("alertdialog", { name: "Archive this thread?" })).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: /^Archive$/ }));
    expect(archive).toHaveBeenCalledWith("thread-1", 1);
  });

  it("closes confirmations with Escape and restores focus to the trigger", async () => {
    renderSidebar({ threads: [thread("completed", null)] });
    const archive = screen.getByRole("button", { name: "Archive Review thread" });
    await userEvent.click(archive);
    expect(screen.getByRole("alertdialog", { name: "Archive this thread?" })).toBeTruthy();
    await userEvent.keyboard("{Escape}");
    expect(screen.queryByRole("alertdialog")).toBeNull();
    expect(document.activeElement).toBe(archive);
  });
});

function renderSidebar(overrides: Partial<React.ComponentProps<typeof ThreadSidebar>>) {
  return render(<ThreadSidebar
    threads={[]}
    status="ready"
    error={null}
    truncated={false}
    selectedThreadId={null}
    onSelect={vi.fn()}
    onCreate={vi.fn(async () => null)}
    onRename={vi.fn(async () => null)}
    onArchive={vi.fn(async () => null)}
    {...overrides}
  />);
}

function thread(status: string, archivedAtUtc: string | null, threadId = "thread-1"): ThreadSummaryData {
  return {
    threadId, revision: 1, workspaceId: "workspace-1", title: threadId === "thread-1" ? "Review thread" : "Failed thread", status,
    createdAtUtc: "2026-07-17T00:00:00.000Z", updatedAtUtc: "2026-07-17T00:00:00.000Z", archivedAtUtc,
    turnCount: 1, timelineItemCount: 14, activeTurnId: null,
    origin: { kind: "desktop", sourceKind: null, sourceId: null, sourceFingerprint: null },
  };
}
