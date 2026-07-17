import type { BrowserWindow, IpcMain, IpcMainInvokeEvent, OpenDialogOptions } from "electron";
import {
  isArtifactGetResult,
  isArtifactListResult,
  isChangesGetResult,
  isCatalogListResult,
  isComposerStateResult,
  isContextResolveResult,
  isContextSearchResult,
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
  isClearComposerCommand,
  isCreateThreadCommand,
  isEnqueueComposerCommand,
  isGetComposerCommand,
  isGetArtifactCommand,
  isGetChangesCommand,
  isGetReportCommand,
  isGetThreadCommand,
  isRenameThreadCommand,
  isListCatalogCommand,
  isSearchContextCommand,
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
    "getChanges" | "listReports" | "getReport" | "listArtifacts" | "getArtifact" |
    "listCatalog" | "searchContext" | "resolveContext" | "getComposer" | "enqueueComposer" | "clearComposer">;
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
  options.ipcMain.handle(IPC_CHANNELS.listCatalog, async (event, ...args) => {
    const command = assertOne(event, args, isListCatalogCommand);
    const value = await options.runtime.listCatalog(command.kind);
    if (!isCatalogListResult(value)) throw new Error("Invalid catalog result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.searchContext, async (event, ...args) => {
    const command = assertOne(event, args, isSearchContextCommand);
    const value = await options.runtime.searchContext(command.query);
    if (!isContextSearchResult(value)) throw new Error("Invalid context search result.");
    return value;
  });
  const pickContext = async (event: IpcMainInvokeEvent, args: unknown[], kind: "file" | "folder") => {
    assertCall(event, args);
    const window = options.getWindow();
    if (!isRuntimeWindowAvailable(window)) throw new Error("Desktop window is unavailable.");
    if (options.runtime.getStatus().state !== "ready") throw new Error("AppHost is not ready.");
    const selection = await options.dialog.showOpenDialog(window, {
      properties: [kind === "file" ? "openFile" : "openDirectory", "dontAddToRecent"],
      title: kind === "file" ? "Attach workspace file" : "Attach workspace folder",
    });
    if (selection.canceled) return { schemaVersion: 1 as const, canceled: true, result: null };
    if (selection.filePaths.length !== 1 || !selection.filePaths[0]) throw new Error("Invalid context selection.");
    if (options.getWindow() !== window || !isRuntimeWindowAvailable(window)) throw new Error("Desktop window is unavailable.");
    const result = await options.runtime.resolveContext(selection.filePaths[0], kind);
    if (!isContextResolveResult(result)) throw new Error("Invalid context resolve result.");
    return { schemaVersion: 1 as const, canceled: false, result };
  };
  options.ipcMain.handle(IPC_CHANNELS.pickFile, (event, ...args) => pickContext(event, args, "file"));
  options.ipcMain.handle(IPC_CHANNELS.pickFolder, (event, ...args) => pickContext(event, args, "folder"));
  options.ipcMain.handle(IPC_CHANNELS.getComposer, async (event, ...args) => {
    const command = assertOne(event, args, isGetComposerCommand);
    const value = await options.runtime.getComposer(command.threadId);
    if (!isComposerStateResult(value)) throw new Error("Invalid composer result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.enqueueComposer, async (event, ...args) => {
    const command = assertOne(event, args, isEnqueueComposerCommand);
    const value = await options.runtime.enqueueComposer(command);
    if (!isComposerStateResult(value)) throw new Error("Invalid composer result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.clearComposer, async (event, ...args) => {
    const command = assertOne(event, args, isClearComposerCommand);
    const value = await options.runtime.clearComposer(command.threadId, command.expectedQueueRevision, command.clientMutationId);
    if (!isComposerStateResult(value)) throw new Error("Invalid composer result.");
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
      IPC_CHANNELS.listCatalog,
      IPC_CHANNELS.searchContext,
      IPC_CHANNELS.pickFile,
      IPC_CHANNELS.pickFolder,
      IPC_CHANNELS.getComposer,
      IPC_CHANNELS.enqueueComposer,
      IPC_CHANNELS.clearComposer,
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
