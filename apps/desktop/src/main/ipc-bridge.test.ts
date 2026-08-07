import { describe, expect, it, vi } from "vitest";
import { createRuntimeStatus, IPC_CHANNELS } from "../shared/bridge-contract";
import { registerDesktopIpc } from "./ipc-bridge";

describe("desktop IPC registry", () => {
  it("registers exactly forty-eight invoke handlers and disposes them", () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>();
    const removeHandler = vi.fn((channel: string) => handlers.delete(channel));
    const ipcMain = {
      handle: vi.fn((channel: string, handler: (...args: unknown[]) => unknown) => handlers.set(channel, handler)),
      removeHandler,
    };
    const runtime = {
      getStatus: vi.fn(() => createRuntimeStatus("runtime-ready")),
      restart: vi.fn(async () => createRuntimeStatus("runtime-ready")),
      openWorkspace: vi.fn(),
      ...readOnlyRuntime(),
    };
    const dispose = registerDesktopIpc({
      ipcMain: ipcMain as never,
      runtime,
      dialog: { showOpenDialog: vi.fn(), showSaveDialog: vi.fn() },
      getWindow: () => null,
      isAllowedSender: () => true,
    });
    expect([...handlers.keys()]).toEqual([
      IPC_CHANNELS.getRuntimeStatus,
      IPC_CHANNELS.restartRuntime,
      IPC_CHANNELS.openWorkspace,
      IPC_CHANNELS.getWorkspaceSnapshot,
      IPC_CHANNELS.listThreads,
      IPC_CHANNELS.getThread,
      IPC_CHANNELS.createThread,
      IPC_CHANNELS.renameThread,
      IPC_CHANNELS.archiveThread,
      IPC_CHANNELS.listCatalog,
      IPC_CHANNELS.searchContext,
      IPC_CHANNELS.pickFile,
      IPC_CHANNELS.pickFolder,
      IPC_CHANNELS.getComposer,
      IPC_CHANNELS.enqueueComposer,
      IPC_CHANNELS.clearComposer,
      IPC_CHANNELS.startTurn,
      IPC_CHANNELS.cancelTurn,
      IPC_CHANNELS.resolveApproval,
      IPC_CHANNELS.resumeTurn,
      IPC_CHANNELS.restartTurn,
      IPC_CHANNELS.getChanges,
      IPC_CHANNELS.listReports,
      IPC_CHANNELS.getReport,
      IPC_CHANNELS.listArtifacts,
      IPC_CHANNELS.getArtifact,
      IPC_CHANNELS.openTerminal, IPC_CHANNELS.inputTerminal, IPC_CHANNELS.resizeTerminal,
      IPC_CHANNELS.cancelTerminal, IPC_CHANNELS.closeTerminal, IPC_CHANNELS.getTerminal,
      IPC_CHANNELS.listSubagents, IPC_CHANNELS.startSubagent, IPC_CHANNELS.cancelSubagent,
      IPC_CHANNELS.takeoverSubagent, IPC_CHANNELS.resolveSubagentApproval,
      IPC_CHANNELS.getSettings, IPC_CHANNELS.setSettings,
      IPC_CHANNELS.mutateChanges,
      IPC_CHANNELS.listTerminalProfiles,
      IPC_CHANNELS.previewArtifact, IPC_CHANNELS.verifyArtifact, IPC_CHANNELS.exportArtifact,
      IPC_CHANNELS.getGerberReview, IPC_CHANNELS.getGerberPreview, IPC_CHANNELS.acceptGerber, IPC_CHANNELS.rejectGerber,
    ]);
    dispose();
    expect(removeHandler).toHaveBeenCalledTimes(48);
  });

  it("rejects untrusted senders and unexpected arguments", async () => {
    const handlers = captureHandlers({ allowed: false });
    await expect(handlers.get(IPC_CHANNELS.getRuntimeStatus)?.({})).rejects.toThrow("Untrusted");
    const allowed = captureHandlers({ allowed: true });
    await expect(allowed.get(IPC_CHANNELS.getRuntimeStatus)?.({}, "path")).rejects.toThrow("Unexpected");
  });
});

function captureHandlers(options: { allowed: boolean }) {
  const handlers = new Map<string, (...args: unknown[]) => Promise<unknown>>();
  registerDesktopIpc({
    ipcMain: {
      handle: (channel: string, handler: (event: never, ...args: unknown[]) => unknown) =>
        handlers.set(channel, async (...args) => handler(args[0] as never, ...args.slice(1))),
      removeHandler: () => undefined,
    } as never,
    runtime: {
      getStatus: () => createRuntimeStatus("runtime-ready"),
      restart: async () => createRuntimeStatus("runtime-ready"),
      openWorkspace: async () => { throw new Error("unused"); },
      ...readOnlyRuntime(),
    },
    dialog: { showOpenDialog: async () => ({ canceled: true, filePaths: [] }), showSaveDialog: async () => ({ canceled: true }) },
    getWindow: () => null,
    isAllowedSender: () => options.allowed,
  });
  return handlers;
}

function readOnlyRuntime() {
  const unused = async () => { throw new Error("unused"); };
  return {
    getWorkspaceSnapshot: () => null,
    listThreads: unused,
    getThread: unused,
    createThread: unused,
    renameThread: unused,
    archiveThread: unused,
    getChanges: unused,
    mutateChanges: unused,
    listReports: unused,
    getReport: unused,
    listArtifacts: unused,
    getArtifact: unused,
    listCatalog: unused,
    searchContext: unused,
    resolveContext: unused,
    getComposer: unused,
    enqueueComposer: unused,
    clearComposer: unused,
    startTurn: unused,
    cancelTurn: unused,
    resolveApproval: unused,
    listSubagents: unused,
    startSubagent: unused,
    cancelSubagent: unused,
    takeoverSubagent: unused,
    resolveSubagentApproval: unused,
    resumeTurn: unused,
    restartTurn: unused,
    openTerminal: unused, inputTerminal: unused, resizeTerminal: unused,
    cancelTerminal: unused, closeTerminal: unused, getTerminal: unused,
    listTerminalProfiles: unused,
    previewArtifact: unused, exportArtifact: unused, verifyArtifact: unused,
    getGerberReview: unused, getGerberPreview: unused, acceptGerber: unused, rejectGerber: unused,
  };
}
