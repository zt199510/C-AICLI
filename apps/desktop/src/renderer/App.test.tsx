import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ChangesData, WorkspaceSnapshotData } from "../generated/desktop-contracts";
import { createRuntimeStatus, type DesktopBridge } from "../shared/bridge-contract";
import { App } from "./App";

describe("desktop shell", () => {
  beforeEach(() => {
    Object.defineProperty(window, "innerWidth", { value: 1440, configurable: true, writable: true });
    window.localStorage.clear();
    window.caicli = bridge();
  });

  it("reads the runtime snapshot without initializing AppHost from the renderer", async () => {
    const { container } = render(<App />);
    await waitFor(() => expect(container.querySelector(".app-shell")?.getAttribute("data-runtime-state")).toBe("ready"));
    expect(container.querySelector(".titlebar")).toBeNull();
    expect(window.caicli.getRuntimeStatus).toHaveBeenCalledOnce();
  });

  it("does not reload an already-ready selected review tab", async () => {
    const configured = bridge();
    configured.getWorkspaceSnapshot = vi.fn(async () => workspace);
    configured.getChanges = vi.fn(async () => ({
      schemaVersion: 1, succeeded: true, data: changes, error: null, diagnostics: [], truncated: true,
    }));
    window.caicli = configured;
    render(<App />);
    await waitFor(() => expect(configured.getChanges).toHaveBeenCalledOnce());
    const sidebar = screen.getByRole("button", { name: "Toggle workspace tool sidebar" });
    if (sidebar.getAttribute("aria-pressed") === "false") await userEvent.click(sidebar);
    await userEvent.click(screen.getByRole("tab", { name: "审阅" }));
    expect(configured.getChanges).toHaveBeenCalledOnce();
  });

  it("offers an explicit restart after failure", async () => {
    window.caicli = bridge(createRuntimeStatus("apphost-exited"));
    render(<App />);
    const restart = await screen.findByRole("button", { name: "Restart AppHost" });
    await userEvent.click(restart);
    expect(window.caicli.restartRuntime).toHaveBeenCalledOnce();
  });

  it("keeps three workspace panel toggles visible and prevents a cramped bottom-plus-right split", async () => {
    render(<App />);
    await userEvent.click(screen.getByRole("button", { name: "收起会话侧栏" }));
    expect(screen.getByRole("button", { name: "展开会话侧栏" })).toBeTruthy();
    const summary = screen.getByRole("button", { name: "Toggle workspace summary" });
    const bottom = screen.getByRole("button", { name: "Toggle workspace bottom panel" });
    const sidebar = screen.getByRole("button", { name: "Toggle workspace tool sidebar" });
    expect(summary.getAttribute("aria-controls")).toBe("workspace-summary-overlay");
    expect(bottom.getAttribute("aria-controls")).toBe("workspace-bottom-panel");
    expect(sidebar.getAttribute("aria-controls")).toBe("workspace-tool-sidebar");
    expect(summary.getAttribute("aria-pressed")).toBe("true");
    expect(bottom.getAttribute("aria-pressed")).toBe("false");
    expect(sidebar.getAttribute("aria-pressed")).toBe("true");

    await userEvent.click(summary);
    expect(summary.getAttribute("aria-pressed")).toBe("false");
    await userEvent.click(summary);
    await userEvent.click(bottom);
    expect(summary.getAttribute("aria-pressed")).toBe("true");
    expect(bottom.getAttribute("aria-pressed")).toBe("true");
    expect(sidebar.getAttribute("aria-pressed")).toBe("false");
    expect(document.querySelector("#workspace-summary-overlay")).toBeTruthy();
    const dock = document.querySelector("#workspace-bottom-panel");
    expect(dock).toBeTruthy();
    expect(dock?.parentElement?.classList.contains("task-surface")).toBe(true);
    expect(dock?.previousElementSibling?.classList.contains("composer")).toBe(true);
    expect(document.querySelector(".timeline-overlay-layer #workspace-bottom-panel")).toBeNull();

    await userEvent.click(sidebar);
    expect(bottom.getAttribute("aria-pressed")).toBe("false");
    expect(sidebar.getAttribute("aria-pressed")).toBe("true");
    expect(document.querySelector("#workspace-bottom-panel")).toBeNull();
  });

  it("exposes and handles the three workspace panel keyboard shortcuts", () => {
    render(<App />);
    const summary = screen.getByRole("button", { name: "Toggle workspace summary" });
    const bottom = screen.getByRole("button", { name: "Toggle workspace bottom panel" });
    const sidebar = screen.getByRole("button", { name: "Toggle workspace tool sidebar" });
    expect(summary.getAttribute("aria-keyshortcuts")).toBe("Control+Shift+1");
    expect(bottom.getAttribute("aria-keyshortcuts")).toBe("Control+Shift+2");
    expect(sidebar.getAttribute("aria-keyshortcuts")).toBe("Control+Shift+3");

    fireEvent.keyDown(window, { code: "Digit1", ctrlKey: true, shiftKey: true });
    fireEvent.keyDown(window, { code: "Digit2", ctrlKey: true, shiftKey: true });
    fireEvent.keyDown(window, { code: "Digit3", ctrlKey: true, shiftKey: true });
    expect(summary.getAttribute("aria-pressed")).toBe("false");
    expect(bottom.getAttribute("aria-pressed")).toBe("false");
    expect(sidebar.getAttribute("aria-pressed")).toBe("true");
  });

  it("opens the archived view from the collapsed conversation rail", async () => {
    render(<App />);
    await userEvent.click(screen.getByRole("button", { name: "收起会话侧栏" }));
    await userEvent.click(screen.getByRole("button", { name: "查看已归档对话" }));
    expect(screen.getByRole("button", { name: "筛选对话，当前：已归档" })).toBeTruthy();
    expect(document.activeElement).toBe(screen.getByRole("searchbox", { name: "搜索对话" }));

    await userEvent.click(screen.getByRole("button", { name: "收起会话侧栏" }));
    await userEvent.click(screen.getByRole("button", { name: "新建对话" }));
    await userEvent.click(screen.getByRole("button", { name: "展开会话侧栏" }));
    expect(screen.getByRole("button", { name: "筛选对话，当前：全部（不含已归档）" })).toBeTruthy();
  });

  it("uses a terminal-only bottom dock and a tabbed right workbench", async () => {
    render(<App />);
    expect(screen.queryByRole("region", { name: "User terminal" })).toBeNull();
    expect(screen.getByRole("tab", { name: "审阅" })).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "添加工作区工具" }));
    expect(screen.getByRole("menu", { name: "添加工作区工具" })).toBeTruthy();
    expect(screen.getByRole("menuitem", { name: "终端" })).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "Toggle workspace bottom panel" }));
    expect(screen.getByRole("button", { name: "Toggle workspace bottom panel" }).getAttribute("aria-pressed")).toBe("true");
    expect(document.activeElement).toBe(screen.getByRole("button", { name: "Toggle workspace bottom panel" }));
    expect(screen.getByRole("region", { name: "终端控制台" })).toBeTruthy();
    expect(screen.getByRole("region", { name: "User terminal" })).toBeTruthy();
    expect(document.querySelector('#workspace-bottom-panel [data-panel="changes"]')).toBeNull();
  });

  it("switches cleanly between the right workbench and the full-width terminal dock", async () => {
    render(<App />);
    const reviewTab = screen.getByRole("tab", { name: "审阅" });
    const bottom = screen.getByRole("button", { name: "Toggle workspace bottom panel" });
    const sidebar = screen.getByRole("button", { name: "Toggle workspace tool sidebar" });
    await userEvent.click(bottom);
    expect(screen.getByRole("region", { name: "终端控制台" })).toBeTruthy();
    expect(sidebar.getAttribute("aria-pressed")).toBe("false");
    expect(reviewTab.getAttribute("aria-selected")).toBe("true");
    await userEvent.click(sidebar);
    expect(screen.queryByRole("region", { name: "终端控制台" })).toBeNull();
    expect(sidebar.getAttribute("aria-pressed")).toBe("true");
    expect(reviewTab.getAttribute("aria-selected")).toBe("true");
  });

  it("closes non-modal surfaces in last-opened order and restores their triggers", async () => {
    render(<App />);
    const summary = screen.getByRole("button", { name: "Toggle workspace summary" });
    const bottom = screen.getByRole("button", { name: "Toggle workspace bottom panel" });
    await userEvent.click(bottom);

    document.querySelector<HTMLElement>("#workspace-bottom-panel .terminal-panel-close")?.focus();
    await userEvent.keyboard("{Escape}");
    expect(bottom.getAttribute("aria-pressed")).toBe("false");
    expect(summary.getAttribute("aria-pressed")).toBe("true");
    expect(document.activeElement).toBe(bottom);

    await userEvent.keyboard("{Escape}");
    expect(summary.getAttribute("aria-pressed")).toBe("false");
    expect(document.activeElement).toBe(summary);
  });

  it("supports keyboard resizing for the wide workspace inspector", async () => {
    render(<App />);
    const separator = screen.getByRole("separator", { name: "Resize workspace inspector" });
    expect(separator.getAttribute("aria-valuenow")).toBe("640");
    separator.focus();
    await userEvent.keyboard("{ArrowLeft}");
    expect(separator.getAttribute("aria-valuenow")).toBe("656");
  });

  it("supports keyboard resizing for the conversation sidebar", async () => {
    render(<App />);
    const separator = screen.getByRole("separator", { name: "调整会话侧栏宽度" });
    expect(separator.getAttribute("aria-valuenow")).toBe("288");
    separator.focus();
    await userEvent.keyboard("{ArrowRight}");
    expect(separator.getAttribute("aria-valuenow")).toBe("304");
    await userEvent.keyboard("{Home}");
    expect(separator.getAttribute("aria-valuenow")).toBe("288");
  });

  it("closes drawers with Escape and restores the shell trigger", async () => {
    render(<App />);
    const collapse = screen.getByRole("button", { name: "收起会话侧栏" });
    collapse.focus();
    await userEvent.keyboard("{Escape}");
    const show = screen.getByRole("button", { name: "展开会话侧栏" });
    expect(document.activeElement).toBe(show);
    expect(show.getAttribute("aria-controls")).toBe("threads-panel");
  });

  it("keeps narrow drawers closed and mutually exclusive", async () => {
    window.innerWidth = 760;
    render(<App />);
    expect(screen.getByRole("button", { name: "显示会话侧栏" })).toBeTruthy();
    const sidebar = screen.getByRole("button", { name: "Toggle workspace tool sidebar" });
    expect(sidebar.getAttribute("aria-pressed")).toBe("false");
    await userEvent.click(screen.getByRole("button", { name: "显示会话侧栏" }));
    expect(sidebar).toBeTruthy();
    await userEvent.click(sidebar);
    expect(screen.getByRole("button", { name: "显示会话侧栏" })).toBeTruthy();
    expect(document.querySelector("#threads-panel")?.getAttribute("aria-hidden")).toBe("true");
    expect(document.querySelector("#workspace-tool-sidebar")?.getAttribute("aria-hidden")).toBe("false");
  });
});

const workspace: WorkspaceSnapshotData = {
  workspaceId: "workspace-1", rootPath: "C:\\workspace", status: "ready",
  capabilities: { readOnlyQueries: true, gitQueries: true, localCatalogs: true, managedArtifacts: true, controlledContext: true },
  configuration: { hasApiKey: false, apiKeySource: "none", effectiveModel: "gpt-test", modelSource: "default", agentBackendSource: "default", approvalMode: "ask", approvalModeSource: "default", loadedSourceCount: 0 },
};

const changes: ChangesData = {
  status: "ready", exitCode: 0, gitStatusSummary: "M src/review.ts", gitStatusSucceeded: true, gitStatusErrorCode: null,
  dirty: true, diffStatSummary: "bounded", diffSucceeded: true, diffErrorCode: null, diffTruncated: true,
  changedFiles: [{ path: "src/review.ts", status: "M" }], sessionSource: null, sessionName: null, warnings: ["bounded"],
};

function bridge(status = createRuntimeStatus("runtime-ready")): DesktopBridge {
  return {
    getRuntimeStatus: vi.fn(async () => status),
    restartRuntime: vi.fn(async () => createRuntimeStatus("runtime-ready")),
    openWorkspace: vi.fn(async () => null),
    getWorkspaceSnapshot: vi.fn(async () => null),
    listThreads: vi.fn(async () => { throw new Error("unused"); }),
    getThread: vi.fn(async () => { throw new Error("unused"); }),
    createThread: vi.fn(async () => { throw new Error("unused"); }),
    renameThread: vi.fn(async () => { throw new Error("unused"); }),
    archiveThread: vi.fn(async () => { throw new Error("unused"); }),
    getChanges: vi.fn(async () => { throw new Error("unused"); }),
    mutateChanges: vi.fn(async () => { throw new Error("unused"); }),
    listReports: vi.fn(async () => { throw new Error("unused"); }),
    getReport: vi.fn(async () => { throw new Error("unused"); }),
    listArtifacts: vi.fn(async () => { throw new Error("unused"); }),
    getArtifact: vi.fn(async () => { throw new Error("unused"); }),
    openTerminal: vi.fn(async () => { throw new Error("unused"); }),
    inputTerminal: vi.fn(async () => { throw new Error("unused"); }),
    resizeTerminal: vi.fn(async () => { throw new Error("unused"); }),
    cancelTerminal: vi.fn(async () => { throw new Error("unused"); }),
    closeTerminal: vi.fn(async () => { throw new Error("unused"); }),
    getTerminal: vi.fn(async () => { throw new Error("unused"); }),
    listTerminalProfiles: vi.fn(async () => { throw new Error("unused"); }),
    previewArtifact: vi.fn(async () => { throw new Error("unused"); }),
    exportArtifact: vi.fn(async () => { throw new Error("unused"); }),
    verifyArtifact: vi.fn(async () => { throw new Error("unused"); }),
    getGerberReview: vi.fn(async () => { throw new Error("unused"); }),
    getGerberPreview: vi.fn(async () => { throw new Error("unused"); }),
    acceptGerber: vi.fn(async () => { throw new Error("unused"); }),
    rejectGerber: vi.fn(async () => { throw new Error("unused"); }),
    listCatalog: vi.fn(async () => { throw new Error("unused"); }),
    searchContext: vi.fn(async () => { throw new Error("unused"); }),
    pickFile: vi.fn(async () => { throw new Error("unused"); }),
    pickFolder: vi.fn(async () => { throw new Error("unused"); }),
    getComposer: vi.fn(async () => { throw new Error("unused"); }),
    enqueueComposer: vi.fn(async () => { throw new Error("unused"); }),
    clearComposer: vi.fn(async () => { throw new Error("unused"); }),
    startTurn: vi.fn(async () => { throw new Error("unused"); }),
    cancelTurn: vi.fn(async () => { throw new Error("unused"); }),
    resolveApproval: vi.fn(async () => { throw new Error("unused"); }),
    listSubagents: vi.fn(async () => { throw new Error("unused"); }),
    startSubagent: vi.fn(async () => { throw new Error("unused"); }),
    cancelSubagent: vi.fn(async () => { throw new Error("unused"); }),
    takeoverSubagent: vi.fn(async () => { throw new Error("unused"); }),
    resolveSubagentApproval: vi.fn(async () => { throw new Error("unused"); }),
    resumeTurn: vi.fn(async () => { throw new Error("unused"); }),
  restartTurn: vi.fn(async () => { throw new Error("unused"); }),
  getSettings: vi.fn(async () => ({ schemaVersion: 1 as const, user: { language: "zh-CN" as const, theme: "system" as const, defaultShell: "system-default" as const, model: "", approval: "on-request" as const, shortcuts: true, summaryDefault: true, bottomDefault: true, toolsDefault: true, gitBase: "main", navigationWidth: 288, inspectorWidth: 640, disabledTools: [] }, workspace: {} })),
  setSettings: vi.fn(async (command) => ({ schemaVersion: 1 as const, user: command.scope === "user" ? { language: "zh-CN" as const, theme: "system" as const, defaultShell: "system-default" as const, model: "", approval: "on-request" as const, shortcuts: true, summaryDefault: true, bottomDefault: true, toolsDefault: true, gitBase: "main", navigationWidth: 288, inspectorWidth: 640, disabledTools: [], ...command.value } : { language: "zh-CN" as const, theme: "system" as const, defaultShell: "system-default" as const, model: "", approval: "on-request" as const, shortcuts: true, summaryDefault: true, bottomDefault: true, toolsDefault: true, gitBase: "main", navigationWidth: 288, inspectorWidth: 640, disabledTools: [] }, workspace: command.scope === "workspace" ? command.value : {} })),
    onRuntimeStatus: vi.fn(() => () => undefined),
    onThreadChanged: vi.fn(() => () => undefined),
  };
}
