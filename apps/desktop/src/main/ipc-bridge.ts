import type { BrowserWindow, IpcMain, IpcMainInvokeEvent, OpenDialogOptions } from "electron";
import {
  isArtifactGetResult,
  isArtifactListResult,
  isChangesGetResult,
  isReportGetResult,
  isReportListResult,
  isThreadGetResult,
  isThreadListResult,
  isThreadSummaryResult,
  isWorkspaceOpenResult,
  isWorkspaceSnapshotData,
} from "../generated/desktop-contracts";
import {
  IPC_CHANNELS,
  isArchiveThreadCommand,
  isCreateThreadCommand,
  isGetArtifactCommand,
  isGetChangesCommand,
  isGetReportCommand,
  isGetThreadCommand,
  isRenameThreadCommand,
  isRuntimeStatus,
} from "../shared/bridge-contract";
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
  runtime: Pick<AppHostRuntime,
    "getStatus" | "restart" | "openWorkspace" | "getWorkspaceSnapshot" |
    "listThreads" | "getThread" | "createThread" | "renameThread" | "archiveThread" |
    "getChanges" | "listReports" | "getReport" | "listArtifacts" | "getArtifact">;
  dialog: DialogAdapter;
  getWindow(): BrowserWindow | null;
  isAllowedSender(event: IpcMainInvokeEvent): boolean;
}

export function registerDesktopIpc(options: DesktopIpcOptions): () => void {
  const assertCall = (event: IpcMainInvokeEvent, args: unknown[]) => {
    if (!options.isAllowedSender(event)) throw new Error("Untrusted IPC sender.");
    if (args.length !== 0) throw new Error("Unexpected IPC arguments.");
  };
  const assertOne = <T>(
    event: IpcMainInvokeEvent,
    args: unknown[],
    validator: (value: unknown) => value is T,
  ): T => {
    if (!options.isAllowedSender(event)) throw new Error("Untrusted IPC sender.");
    if (args.length !== 1 || !validator(args[0])) throw new Error("Invalid IPC arguments.");
    return args[0];
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
  options.ipcMain.handle(IPC_CHANNELS.getWorkspaceSnapshot, (event, ...args) => {
    assertCall(event, args);
    const value = options.runtime.getWorkspaceSnapshot();
    if (value !== null && !isWorkspaceSnapshotData(value)) throw new Error("Invalid workspace snapshot.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.listThreads, async (event, ...args) => {
    assertCall(event, args);
    const value = await options.runtime.listThreads();
    if (!isThreadListResult(value)) throw new Error("Invalid thread list result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.getThread, async (event, ...args) => {
    const command = assertOne(event, args, isGetThreadCommand);
    const value = await options.runtime.getThread(command.threadId, command.afterSequence);
    if (!isThreadGetResult(value)) throw new Error("Invalid thread result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.createThread, async (event, ...args) => {
    const command = assertOne(event, args, isCreateThreadCommand);
    const value = await options.runtime.createThread(command.title);
    if (!isThreadSummaryResult(value)) throw new Error("Invalid thread mutation result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.renameThread, async (event, ...args) => {
    const command = assertOne(event, args, isRenameThreadCommand);
    const value = await options.runtime.renameThread(command.threadId, command.expectedRevision, command.title);
    if (!isThreadSummaryResult(value)) throw new Error("Invalid thread mutation result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.archiveThread, async (event, ...args) => {
    const command = assertOne(event, args, isArchiveThreadCommand);
    const value = await options.runtime.archiveThread(command.threadId, command.expectedRevision);
    if (!isThreadSummaryResult(value)) throw new Error("Invalid thread mutation result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.getChanges, async (event, ...args) => {
    const command = assertOne(event, args, isGetChangesCommand);
    const value = await options.runtime.getChanges(command.sessionName);
    if (!isChangesGetResult(value)) throw new Error("Invalid changes result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.listReports, async (event, ...args) => {
    assertCall(event, args);
    const value = await options.runtime.listReports();
    if (!isReportListResult(value)) throw new Error("Invalid report list result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.getReport, async (event, ...args) => {
    const command = assertOne(event, args, isGetReportCommand);
    const value = await options.runtime.getReport(command.reportId);
    if (!isReportGetResult(value)) throw new Error("Invalid report result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.listArtifacts, async (event, ...args) => {
    assertCall(event, args);
    const value = await options.runtime.listArtifacts();
    if (!isArtifactListResult(value)) throw new Error("Invalid artifact list result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.getArtifact, async (event, ...args) => {
    const command = assertOne(event, args, isGetArtifactCommand);
    const value = await options.runtime.getArtifact(command.artifactId);
    if (!isArtifactGetResult(value)) throw new Error("Invalid artifact result.");
    return value;
  });

  return () => {
    for (const channel of [
      IPC_CHANNELS.getRuntimeStatus,
      IPC_CHANNELS.restartRuntime,
      IPC_CHANNELS.openWorkspace,
      IPC_CHANNELS.getWorkspaceSnapshot,
      IPC_CHANNELS.listThreads,
      IPC_CHANNELS.getThread,
      IPC_CHANNELS.createThread,
      IPC_CHANNELS.renameThread,
      IPC_CHANNELS.archiveThread,
      IPC_CHANNELS.getChanges,
      IPC_CHANNELS.listReports,
      IPC_CHANNELS.getReport,
      IPC_CHANNELS.listArtifacts,
      IPC_CHANNELS.getArtifact,
    ]) options.ipcMain.removeHandler(channel);
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
