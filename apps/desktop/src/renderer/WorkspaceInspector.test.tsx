import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { ReviewState } from "./desktop-state";
import type { ReviewCommands } from "./ReviewInspector";
import type { TerminalCommands } from "./TerminalPanel";
import { WorkspaceInspector, type WorkspacePanel } from "./WorkspaceInspector";

describe("workspace inspector", () => {
  it("connects its five tabs and supports keyboard navigation", async () => {
    const onPanel = vi.fn();
    renderInspector("changes", onPanel);

    const changes = screen.getByRole("tab", { name: "Changes" });
    changes.focus();
    await userEvent.keyboard("{ArrowRight}");

    expect(onPanel).toHaveBeenCalledWith("terminal");
    expect(changes.getAttribute("aria-controls")).toBe("context-panel-changes");
    expect(screen.getByRole("tabpanel").getAttribute("aria-labelledby")).toBe("context-tab-changes");
    expect(screen.getByRole("button", { name: "Close workspace inspector" })).toBeTruthy();
  });

  it("routes terminal actions through the supplied controller boundary", async () => {
    const openTerminal = vi.fn(async () => terminalResult("running"));
    renderInspector("terminal", vi.fn(), {
      openTerminal,
      inputTerminal: vi.fn(async () => terminalResult("running")),
      cancelTerminal: vi.fn(async () => terminalResult("exited", 130)),
      closeTerminal: vi.fn(async () => terminalResult("closed", 130)),
      getTerminal: vi.fn(async () => terminalResult("running")),
    });

    await userEvent.click(screen.getByRole("button", { name: "Open terminal" }));

    expect(openTerminal).toHaveBeenCalledOnce();
    expect(screen.getByRole("tabpanel").getAttribute("aria-labelledby")).toBe("context-tab-terminal");
  });
});

function renderInspector(
  activePanel: "changes" | "terminal",
  onPanel: (panel: WorkspacePanel) => void,
  terminalCommands?: TerminalCommands,
) {
  return render(<WorkspaceInspector
    activePanel={activePanel}
    visible
    review={review}
    workspaceReady
    workspaceLabel="D:/workspace"
    threadLabel="Thread"
    turnLabel="Turn"
    commands={{} as ReviewCommands}
    terminalCommands={terminalCommands}
    onPanel={onPanel}
    onReport={vi.fn()}
    onArtifact={vi.fn()}
    onClose={vi.fn()}
  />);
}

function terminalResult(status: "running" | "exited" | "closed", exitCode: number | null = null) {
  return {
    schemaVersion: 1 as const,
    succeeded: true,
    data: {
      sessionId: "terminal_0123456789abcdef",
      status,
      shellProfile: "system-default",
      output: "",
      cursor: 0,
      truncated: false,
      exitCode,
      startedAtUtc: "2026-07-18T00:00:00Z",
      exitedAtUtc: status === "running" ? null : "2026-07-18T00:00:01Z",
    },
    error: null,
    diagnostics: [],
    truncated: false,
  };
}

const review: ReviewState = {
  activeTab: "changes",
  status: "ready",
  error: null,
  changes: null,
  reports: [],
  selectedReport: null,
  artifacts: [],
  selectedArtifact: null,
  truncated: false,
};
