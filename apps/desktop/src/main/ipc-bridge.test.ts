import { describe, expect, it, vi } from "vitest";
import { createRuntimeStatus, IPC_CHANNELS } from "../shared/bridge-contract";
import { registerDesktopIpc } from "./ipc-bridge";

describe("desktop IPC registry", () => {
  it("registers exactly three invoke handlers and disposes them", () => {
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
    };
    const dispose = registerDesktopIpc({
      ipcMain: ipcMain as never,
      runtime,
      dialog: { showOpenDialog: vi.fn() },
      getWindow: () => null,
      isAllowedSender: () => true,
    });
    expect([...handlers.keys()]).toEqual([
      IPC_CHANNELS.getRuntimeStatus,
      IPC_CHANNELS.restartRuntime,
      IPC_CHANNELS.openWorkspace,
    ]);
    dispose();
    expect(removeHandler).toHaveBeenCalledTimes(3);
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
    },
    dialog: { showOpenDialog: async () => ({ canceled: true, filePaths: [] }) },
    getWindow: () => null,
    isAllowedSender: () => options.allowed,
  });
  return handlers;
}
