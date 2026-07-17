import type { BrowserWindow, IpcMain, IpcMainInvokeEvent, OpenDialogOptions } from "electron";
import { isWorkspaceOpenResult } from "../generated/desktop-contracts";
import { IPC_CHANNELS, isRuntimeStatus } from "../shared/bridge-contract";
import type { AppHostRuntime } from "./apphost-runtime";
import { isRuntimeWindowAvailable } from "./window-lifecycle";

export interface DialogAdapter {
  showOpenDialog(window: BrowserWindow, options: OpenDialogOptions): Promise<{
    canceled: boolean;
    filePaths: string[];
  }>;
}

export interface DesktopIpcOptions {
  ipcMain: Pick<IpcMain, "handle" | "removeHandler">;
  runtime: Pick<AppHostRuntime, "getStatus" | "restart" | "openWorkspace">;
  dialog: DialogAdapter;
  getWindow(): BrowserWindow | null;
  isAllowedSender(event: IpcMainInvokeEvent): boolean;
}

export function registerDesktopIpc(options: DesktopIpcOptions): () => void {
  const assertCall = (event: IpcMainInvokeEvent, args: unknown[]) => {
    if (!options.isAllowedSender(event)) throw new Error("Untrusted IPC sender.");
    if (args.length !== 0) throw new Error("Unexpected IPC arguments.");
  };

  options.ipcMain.handle(IPC_CHANNELS.getRuntimeStatus, (event, ...args) => {
    assertCall(event, args);
    const value = options.runtime.getStatus();
    if (!isRuntimeStatus(value)) throw new Error("Invalid runtime status.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.restartRuntime, async (event, ...args) => {
    assertCall(event, args);
    const value = await options.runtime.restart();
    if (!isRuntimeStatus(value)) throw new Error("Invalid runtime status.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.openWorkspace, async (event, ...args) => {
    assertCall(event, args);
    const window = options.getWindow();
    if (!isRuntimeWindowAvailable(window)) throw new Error("Desktop window is unavailable.");
    if (options.runtime.getStatus().state !== "ready") throw new Error("AppHost is not ready.");
    const selection = await options.dialog.showOpenDialog(window, {
      properties: ["openDirectory", "dontAddToRecent"],
      title: "Open workspace",
    });
    if (selection.canceled || selection.filePaths.length === 0) return null;
    if (selection.filePaths.length !== 1) throw new Error("Invalid workspace selection.");
    const result = await options.runtime.openWorkspace(selection.filePaths[0] as string);
    if (!isWorkspaceOpenResult(result)) throw new Error("Invalid workspace result.");
    return result;
  });

  return () => {
    options.ipcMain.removeHandler(IPC_CHANNELS.getRuntimeStatus);
    options.ipcMain.removeHandler(IPC_CHANNELS.restartRuntime);
    options.ipcMain.removeHandler(IPC_CHANNELS.openWorkspace);
  };
}

export function isCurrentWindowSender(event: IpcMainInvokeEvent, window: BrowserWindow | null): boolean {
  return Boolean(
    window &&
    !window.isDestroyed() &&
    !window.webContents.isDestroyed() &&
    event.sender === window.webContents &&
    event.senderFrame === window.webContents.mainFrame,
  );
}
