import { describe, expect, it, vi } from "vitest";
import { createRuntimeStatus, IPC_CHANNELS } from "../shared/bridge-contract";
import { createDesktopBridge, type IpcRendererAdapter } from "./bridge";

describe("preload bridge", () => {
  it("is frozen and invokes only reviewed channels", async () => {
    const ready = createRuntimeStatus("runtime-ready");
    const invoke = vi.fn().mockResolvedValue(ready);
    const bridge = createDesktopBridge(adapter(invoke));
    await expect(bridge.getRuntimeStatus()).resolves.toEqual(ready);
    await expect(bridge.restartRuntime()).resolves.toEqual(ready);
    expect(invoke.mock.calls.map((call) => call[0])).toEqual([
      IPC_CHANNELS.getRuntimeStatus,
      IPC_CHANNELS.restartRuntime,
    ]);
    expect(Object.isFrozen(bridge)).toBe(true);
    expect(Object.keys(bridge).sort()).toEqual([
      "acceptGerber",
      "archiveThread",
      "cancelSubagent",
      "cancelTerminal",
      "cancelTurn",
      "clearComposer",
      "closeTerminal",
      "createThread",
      "enqueueComposer",
      "exportArtifact",
      "getArtifact",
      "getChanges",
      "getComposer",
      "getGerberPreview",
      "getGerberReview",
      "getReport",
      "getRuntimeStatus",
      "getSettings",
      "getTerminal",
      "getThread",
      "getWorkspaceSnapshot",
      "inputTerminal",
      "listArtifacts",
      "listCatalog",
      "listReports",
      "listSubagents",
      "listTerminalProfiles",
      "listThreads",
      "mutateChanges",
      "onRuntimeStatus",
      "onThreadChanged",
      "openTerminal",
      "openWorkspace",
      "pickFile",
      "pickFolder",
      "previewArtifact",
      "rejectGerber",
      "renameThread",
      "resizeTerminal",
      "resolveApproval",
      "resolveSubagentApproval",
      "restartRuntime",
      "restartTurn",
      "resumeTurn",
      "searchContext",
      "setSettings",
      "startSubagent",
      "startTurn",
      "takeoverSubagent",
      "verifyArtifact",
    ]);
  });

  it("rejects invalid invoke results", async () => {
    const bridge = createDesktopBridge(adapter(vi.fn().mockResolvedValue({ state: "ready" })));
    await expect(bridge.getRuntimeStatus()).rejects.toThrow("Invalid runtime status.");
  });

  it("filters status events and removes only its wrapper", () => {
    let wrapped: ((event: unknown, value: unknown) => void) | undefined;
    const removeListener = vi.fn();
    const listener = vi.fn();
    const ipc: IpcRendererAdapter = {
      invoke: vi.fn(),
      on: (_channel, candidate) => { wrapped = candidate; },
      removeListener,
    };
    const unsubscribe = createDesktopBridge(ipc).onRuntimeStatus(listener);
    wrapped?.({}, { state: "bad" });
    wrapped?.({}, createRuntimeStatus("runtime-ready"));
    expect(listener).toHaveBeenCalledOnce();
    unsubscribe();
    expect(removeListener).toHaveBeenCalledWith(IPC_CHANNELS.runtimeStatus, wrapped);
  });
});

function adapter(invoke: IpcRendererAdapter["invoke"]): IpcRendererAdapter {
  return { invoke, on: vi.fn(), removeListener: vi.fn() };
}
