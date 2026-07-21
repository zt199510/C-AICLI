import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createRuntimeStatus, type DesktopBridge } from "../shared/bridge-contract";
import { App } from "./App";

describe("desktop shell", () => {
  beforeEach(() => {
    Object.defineProperty(window, "innerWidth", { value: 1440, configurable: true, writable: true });
    window.caicli = bridge();
  });

  it("reads the runtime snapshot without initializing AppHost from the renderer", async () => {
    render(<App />);
    expect(await screen.findByText("AppHost ready")).toBeTruthy();
    expect(window.caicli.getRuntimeStatus).toHaveBeenCalledOnce();
  });

  it("offers an explicit restart after failure", async () => {
    window.caicli = bridge(createRuntimeStatus("apphost-exited"));
    render(<App />);
    const restart = await screen.findByRole("button", { name: "Restart AppHost" });
    await userEvent.click(restart);
    expect(window.caicli.restartRuntime).toHaveBeenCalledOnce();
  });

  it("opens and collapses shell panels", async () => {
    render(<App />);
    await userEvent.click(screen.getByRole("button", { name: "Collapse threads" }));
    expect(screen.getByRole("button", { name: "Show threads" })).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "Close review inspector" }));
    expect(screen.getByRole("button", { name: "Show review inspector" })).toBeTruthy();
  });

  it("closes drawers with Escape and restores the shell trigger", async () => {
    render(<App />);
    const collapse = screen.getByRole("button", { name: "Collapse threads" });
    collapse.focus();
    await userEvent.keyboard("{Escape}");
    const show = screen.getByRole("button", { name: "Show threads" });
    expect(document.activeElement).toBe(show);
    expect(show.getAttribute("aria-controls")).toBe("threads-panel");
  });

  it("keeps narrow drawers closed and mutually exclusive", async () => {
    window.innerWidth = 760;
    render(<App />);
    expect(screen.getByRole("button", { name: "Show threads" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Show review inspector" })).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "Show threads" }));
    expect(screen.queryByRole("button", { name: "Show review inspector" })).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "Show review inspector" }));
    expect(screen.getByRole("button", { name: "Show threads" })).toBeTruthy();
  });
});

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
    resumeTurn: vi.fn(async () => { throw new Error("unused"); }),
    restartTurn: vi.fn(async () => { throw new Error("unused"); }),
    onRuntimeStatus: vi.fn(() => () => undefined),
    onThreadChanged: vi.fn(() => () => undefined),
  };
}
