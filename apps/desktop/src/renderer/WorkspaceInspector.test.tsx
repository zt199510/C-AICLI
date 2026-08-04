import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ReviewState } from "./desktop-state";
import type { ReviewCommands } from "./ReviewInspector";
import type { TerminalCommands } from "./TerminalPanel";
import {
  WorkspaceBottomPanel,
  type WorkspacePanel,
  WorkspaceSummaryOverlay,
  WorkspaceToolSidebar,
} from "./WorkspaceInspector";

const originalViewportWidth = window.innerWidth;

afterEach(() => {
  Object.defineProperty(window, "innerWidth", { configurable: true, value: originalViewportWidth });
});

describe("workspace inspector", () => {
  it("renders grouped vertical tools and supports roving keyboard navigation", async () => {
    const onPanel = vi.fn();
    renderInspector("changes", onPanel);

    expect(screen.getByRole("heading", { name: "Environment" })).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Results and evidence" })).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Sub-agents" })).toBeTruthy();

    const changes = screen.getByRole("button", { name: "Changes" });
    changes.focus();
    await userEvent.keyboard("{ArrowDown}");

    expect(onPanel).toHaveBeenCalledWith("local");
    expect(document.activeElement).toBe(screen.getByRole("button", { name: "Local" }));
    expect(changes.getAttribute("aria-controls")).toBe("workspace-bottom-panel");
    expect(screen.getByRole("region", { name: "Changes" })).toBeTruthy();
  });

  it("keeps Git write navigation disabled and non-mutating", async () => {
    const onPanel = vi.fn();
    renderInspector("changes", onPanel);

    const gitActions = screen.getByRole("button", { name: "Commit or push" });
    expect((gitActions as HTMLButtonElement).disabled).toBe(true);
    await userEvent.click(gitActions);
    expect(onPanel).not.toHaveBeenCalled();
  });

  it("distinguishes unavailable pull request data from an empty result", () => {
    renderInspector("pull-request", vi.fn());
    expect(screen.getByText("Pull request status unavailable")).toBeTruthy();
    expect(screen.getByText(/does not expose pull request status/)).toBeTruthy();
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
    expect(screen.getByRole("region", { name: "Terminal" })).toBeTruthy();
  });

  it("moves focus into a newly opened sidebar drawer and traps Tab within its tools", async () => {
    Object.defineProperty(window, "innerWidth", { configurable: true, value: 1024 });
    const props = surfaceProps("changes");
    const onPanel = vi.fn();
    const view = render(<WorkspaceToolSidebar {...props} onPanel={onPanel} visible={false} />);
    view.rerender(<WorkspaceToolSidebar {...props} onPanel={onPanel} visible />);

    expect(document.activeElement).toBe(screen.getByRole("button", { name: "Changes" }));
    const activity = screen.getByRole("button", { name: "Activity" });
    activity.focus();
    await userEvent.keyboard("{Tab}");
    expect(document.activeElement).toBe(screen.getByRole("button", { name: "Changes" }));
  });

  it("keeps the summary and bottom detail surfaces non-modal", () => {
    const props = surfaceProps("changes");
    render(<>
      <WorkspaceSummaryOverlay {...props} runtimeLabel="AppHost ready" />
      <WorkspaceBottomPanel {...props} />
    </>);

    const summary = document.querySelector("#workspace-summary-overlay")!;
    const bottom = screen.getByRole("region", { name: "Changes" });
    expect(summary.getAttribute("role")).not.toBe("dialog");
    expect(summary.getAttribute("aria-modal")).toBeNull();
    expect(bottom.getAttribute("role")).not.toBe("dialog");
    expect(bottom.getAttribute("aria-modal")).toBeNull();
  });
});

function renderInspector(
  activePanel: WorkspacePanel,
  onPanel: (panel: WorkspacePanel) => void,
  terminalCommands?: TerminalCommands,
) {
  const props = surfaceProps(activePanel, terminalCommands);
  return render(<>
    <WorkspaceToolSidebar {...props} onPanel={onPanel} />
    <WorkspaceBottomPanel {...props} />
  </>);
}

function surfaceProps(
  activePanel: WorkspacePanel,
  terminalCommands?: TerminalCommands,
) {
  return {
    activePanel,
    visible: true,
    review,
    workspaceReady: true,
    workspaceLabel: "D:/workspace",
    threadLabel: "Thread",
    turnLabel: "Turn",
    commands: {} as ReviewCommands,
    terminalCommands,
    onReport: vi.fn(),
    onArtifact: vi.fn(),
  };
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
