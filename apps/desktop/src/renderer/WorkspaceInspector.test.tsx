import { render, screen, waitFor } from "@testing-library/react";
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
  it("renders a tabbed workbench and adds tools from the Codex-style plus menu", async () => {
    const onPanel = vi.fn();
    renderInspector("changes", onPanel);

    const reviewTab = screen.getByRole("tab", { name: "审阅" });
    expect(reviewTab.getAttribute("aria-controls")).toBe("workspace-panel-host");
    await userEvent.click(screen.getByRole("button", { name: "添加工作区工具" }));
    expect(screen.getByRole("menu", { name: "添加工作区工具" })).toBeTruthy();
    expect((screen.getByRole("menuitem", { name: "浏览器" }) as HTMLButtonElement).disabled).toBe(true);
    await userEvent.click(screen.getByRole("menuitem", { name: "更多 C-AICLI 工具" }));
    await userEvent.click(screen.getByRole("menuitem", { name: "Local" }));
    expect(onPanel).toHaveBeenCalledWith("local");
    expect(document.querySelector('#workspace-panel-host[data-panel="changes"]')).toBeTruthy();
  });

  it("opens Git write guidance without performing a mutation", async () => {
    const onPanel = vi.fn();
    renderInspector("changes", onPanel);

    await userEvent.click(screen.getByRole("button", { name: "添加工作区工具" }));
    await userEvent.click(screen.getByRole("menuitem", { name: "更多 C-AICLI 工具" }));
    const gitActions = screen.getByRole("menuitem", { name: "Commit or push" });
    expect((gitActions as HTMLButtonElement).disabled).toBe(false);
    await userEvent.click(gitActions);
    expect(onPanel).toHaveBeenCalledWith("git-actions");
  });

  it("asks for an authoritative Git refresh before showing pull request actions", () => {
    renderInspector("pull-request", vi.fn());
    expect(screen.getByText(/Refresh Changes to load authoritative Git identity/)).toBeTruthy();
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
    expect(screen.getByRole("region", { name: "User terminal" })).toBeTruthy();
  });

  it("moves focus into a newly opened sidebar drawer and traps Tab within its tools", async () => {
    Object.defineProperty(window, "innerWidth", { configurable: true, value: 1024 });
    const props = surfaceProps("changes");
    const onPanel = vi.fn();
    const view = render(<WorkspaceToolSidebar {...props} onPanel={onPanel} visible={false} />);
    view.rerender(<WorkspaceToolSidebar {...props} onPanel={onPanel} visible />);

    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("tab", { name: "审阅" })));
  });

  it("keeps the summary and bottom detail surfaces non-modal", () => {
    const props = surfaceProps("changes");
    render(<>
      <WorkspaceSummaryOverlay {...props} runtimeLabel="AppHost ready" />
      <WorkspaceBottomPanel {...props} />
    </>);

    const summary = document.querySelector("#workspace-summary-overlay")!;
    const bottom = document.querySelector("#workspace-bottom-panel")!;
    expect(summary.getAttribute("role")).not.toBe("dialog");
    expect(summary.getAttribute("aria-modal")).toBeNull();
    expect(bottom.getAttribute("role")).not.toBe("dialog");
    expect(bottom.getAttribute("aria-modal")).toBeNull();
  });

  it("exposes an explicit dock close control without changing the selected tool", async () => {
    const onClose = vi.fn();
    render(<WorkspaceBottomPanel {...surfaceProps("changes")} onClose={onClose} />);

    await userEvent.click(screen.getByRole("button", { name: "关闭底部面板" }));

    expect(onClose).toHaveBeenCalledOnce();
    expect(screen.getByRole("region", { name: "终端控制台" })).toBeTruthy();
  });
});

function renderInspector(
  activePanel: WorkspacePanel,
  onPanel: (panel: WorkspacePanel) => void,
  terminalCommands?: TerminalCommands,
) {
  const props = surfaceProps(activePanel, terminalCommands);
  return render(<WorkspaceToolSidebar {...props} onPanel={onPanel} />);
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
    openPanels: [activePanel],
    onClosePanel: vi.fn(),
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
