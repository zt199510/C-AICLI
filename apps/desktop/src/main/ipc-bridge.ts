import { randomUUID } from "node:crypto";
import type { BrowserWindow, IpcMain, IpcMainInvokeEvent, OpenDialogOptions, SaveDialogOptions } from "electron";
import {
  isArtifactGetResult,
  isArtifactListResult,
  isChangesGetResult, isChangesMutateResult,
  isCatalogListResult,
  isComposerStateResult,
  isTurnExecutionStateResult,
  isSubagentResult,
  isContextResolveResult,
  isContextSearchResult,
  isReportGetResult,
  isReportListResult,
  isThreadGetResult,
  isThreadListResult,
  isThreadSummaryResult,
  isWorkspaceOpenResult,
  isWorkspaceSnapshotData,
  isTerminalStateResult, isTerminalProfileListResult, isArtifactReviewResult, isArtifactExportResult, isGerberReviewResult,
} from "../generated/desktop-contracts";
import {
  IPC_CHANNELS,
  isArchiveThreadCommand,
  isClearComposerCommand,
  isCreateThreadCommand,
  isEnqueueComposerCommand,
  isGetComposerCommand,
  isGetArtifactCommand,
  isGetChangesCommand, isMutateChangesCommand,
  isGetReportCommand,
  isGetThreadCommand,
  isRenameThreadCommand,
  isListCatalogCommand,
  isSearchContextCommand,
  isRuntimeStatus,
  isStartTurnCommand,
  isCancelTurnCommand,
  isResolveApprovalCommand,
  isListSubagentsCommand, isStartSubagentCommand, isSubagentMutationCommand, isResolveSubagentApprovalCommand,
  isResumeTurnCommand,
  isRestartTurnCommand,
  isOpenTerminalCommand, isInputTerminalCommand, isResizeTerminalCommand, isTerminalMutationCommand,
  isGetTerminalCommand, isArtifactReviewCommand, isGerberReviewCommand, isGerberDecisionCommand,
  isGetSettingsCommand, isSetSettingsCommand, isDesktopSettingsSnapshot,
  isContextPickCommand,
} from "../shared/bridge-contract";
import type { AppHostRuntime } from "./apphost-runtime";
import { LocalSettingsStore } from "./local-settings-store";
import { isRuntimeWindowAvailable } from "./window-lifecycle";

export interface DialogAdapter {
  showOpenDialog(window: BrowserWindow, options: OpenDialogOptions): Promise<{
    canceled: boolean;
    filePaths: string[];
  }>;
  showSaveDialog(window: BrowserWindow, options: SaveDialogOptions): Promise<{ canceled: boolean; filePath?: string }>;
}

export interface DesktopIpcOptions {
  ipcMain: Pick<IpcMain, "handle" | "removeHandler">;
  runtime: Pick<AppHostRuntime,
    "getStatus" | "restart" | "openWorkspace" | "getWorkspaceSnapshot" |
    "listThreads" | "getThread" | "createThread" | "renameThread" | "archiveThread" |
    "getChanges" | "mutateChanges" | "listReports" | "getReport" | "listArtifacts" | "getArtifact" |
    "listCatalog" | "searchContext" | "resolveContext" | "getComposer" | "enqueueComposer" | "clearComposer" |
    "startTurn" | "cancelTurn" | "resolveApproval" | "resumeTurn" | "restartTurn" |
    "listSubagents" | "startSubagent" | "cancelSubagent" | "takeoverSubagent" | "resolveSubagentApproval" |
    "openTerminal" | "inputTerminal" | "resizeTerminal" | "cancelTerminal" | "closeTerminal" | "getTerminal" | "listTerminalProfiles" |
    "previewArtifact" | "exportArtifact" | "verifyArtifact" | "getGerberReview" | "getGerberPreview" | "acceptGerber" | "rejectGerber">;
  dialog: DialogAdapter;
  settingsStore?: Pick<LocalSettingsStore, "get" | "set">;
  getWindow(): BrowserWindow | null;
  isAllowedSender(event: IpcMainInvokeEvent): boolean;
}

export function registerDesktopIpc(options: DesktopIpcOptions): () => void {
  const settingsStore = options.settingsStore ?? new LocalSettingsStore(null);
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
    const command = assertOne(event, args, isContextPickCommand);
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
    const result = await options.runtime.resolveContext(selection.filePaths[0], kind, command.threadId);
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
  options.ipcMain.handle(IPC_CHANNELS.startTurn, async (event, ...args) => {
    const command = assertOne(event, args, isStartTurnCommand);
    const value = await options.runtime.startTurn(command);
    if (!isTurnExecutionStateResult(value)) throw new Error("Invalid turn start result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.cancelTurn, async (event, ...args) => {
    const command = assertOne(event, args, isCancelTurnCommand);
    const value = await options.runtime.cancelTurn(command);
    if (!isTurnExecutionStateResult(value)) throw new Error("Invalid turn cancel result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.resolveApproval, async (event, ...args) => {
    const command = assertOne(event, args, isResolveApprovalCommand);
    const value = await options.runtime.resolveApproval(command);
    if (!isTurnExecutionStateResult(value)) throw new Error("Invalid approval result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.resumeTurn, async (event, ...args) => {
    const command = assertOne(event, args, isResumeTurnCommand);
    const value = await options.runtime.resumeTurn(command);
    if (!isTurnExecutionStateResult(value)) throw new Error("Invalid turn resume result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.restartTurn, async (event, ...args) => {
    const command = assertOne(event, args, isRestartTurnCommand);
    const value = await options.runtime.restartTurn(command);
    if (!isTurnExecutionStateResult(value)) throw new Error("Invalid turn restart result.");
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
  options.ipcMain.handle(IPC_CHANNELS.openTerminal, async (event, ...args) => {
    const command = assertOne(event, args, isOpenTerminalCommand);
    const value = await options.runtime.openTerminal(command);
    if (!isTerminalStateResult(value)) throw new Error("Invalid terminal result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.inputTerminal, async (event, ...args) => {
    const command = assertOne(event, args, isInputTerminalCommand);
    const value = await options.runtime.inputTerminal(command);
    if (!isTerminalStateResult(value)) throw new Error("Invalid terminal result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.resizeTerminal, async (event, ...args) => {
    const command = assertOne(event, args, isResizeTerminalCommand);
    const value = await options.runtime.resizeTerminal(command);
    if (!isTerminalStateResult(value)) throw new Error("Invalid terminal result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.cancelTerminal, async (event, ...args) => {
    const command = assertOne(event, args, isTerminalMutationCommand);
    const value = await options.runtime.cancelTerminal(command);
    if (!isTerminalStateResult(value)) throw new Error("Invalid terminal result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.closeTerminal, async (event, ...args) => {
    const command = assertOne(event, args, isTerminalMutationCommand);
    const value = await options.runtime.closeTerminal(command);
    if (!isTerminalStateResult(value)) throw new Error("Invalid terminal result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.getTerminal, async (event, ...args) => {
    const command = assertOne(event, args, isGetTerminalCommand);
    const value = await options.runtime.getTerminal(command.sessionId, command.afterCursor);
    if (!isTerminalStateResult(value)) throw new Error("Invalid terminal result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.listSubagents, async (event, ...args) => {
    const command = assertOne(event, args, isListSubagentsCommand);
    const value = await options.runtime.listSubagents(command.parentThreadId);
    if (!isSubagentResult(value)) throw new Error("Invalid Sub-agent result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.startSubagent, async (event, ...args) => {
    const command = assertOne(event, args, isStartSubagentCommand);
    const value = await options.runtime.startSubagent(command);
    if (!isSubagentResult(value)) throw new Error("Invalid Sub-agent result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.cancelSubagent, async (event, ...args) => {
    const command = assertOne(event, args, isSubagentMutationCommand);
    const value = await options.runtime.cancelSubagent(command);
    if (!isSubagentResult(value)) throw new Error("Invalid Sub-agent result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.takeoverSubagent, async (event, ...args) => {
    const command = assertOne(event, args, isSubagentMutationCommand);
    const value = await options.runtime.takeoverSubagent(command);
    if (!isSubagentResult(value)) throw new Error("Invalid Sub-agent result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.resolveSubagentApproval, async (event, ...args) => {
    const command = assertOne(event, args, isResolveSubagentApprovalCommand);
    const value = await options.runtime.resolveSubagentApproval(command);
    if (!isSubagentResult(value)) throw new Error("Invalid Sub-agent result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.getSettings, (event, ...args) => {
    const command = assertOne(event, args, isGetSettingsCommand);
    const value = settingsStore.get(command.workspaceId);
    if (!isDesktopSettingsSnapshot(value)) throw new Error("Invalid settings result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.setSettings, (event, ...args) => {
    const command = assertOne(event, args, isSetSettingsCommand);
    const value = settingsStore.set(command);
    if (!isDesktopSettingsSnapshot(value)) throw new Error("Invalid settings result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.mutateChanges, async (event, ...args) => {
    const command = assertOne(event, args, isMutateChangesCommand);
    const value = await options.runtime.mutateChanges(command);
    if (!isChangesMutateResult(value)) throw new Error("Invalid changes mutation result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.listTerminalProfiles, async (event, ...args) => {
    assertCall(event, args);
    const value = await options.runtime.listTerminalProfiles();
    if (!isTerminalProfileListResult(value)) throw new Error("Invalid terminal profile result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.previewArtifact, async (event, ...args) => {
    const command = assertOne(event, args, isArtifactReviewCommand);
    const value = await options.runtime.previewArtifact(command.artifactId);
    if (!isArtifactReviewResult(value)) throw new Error("Invalid artifact preview result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.verifyArtifact, async (event, ...args) => {
    const command = assertOne(event, args, isArtifactReviewCommand);
    const value = await options.runtime.verifyArtifact(command.artifactId);
    if (!isArtifactReviewResult(value)) throw new Error("Invalid artifact verify result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.exportArtifact, async (event, ...args) => {
    const command = assertOne(event, args, isArtifactReviewCommand);
    const window = options.getWindow();
    if (!isRuntimeWindowAvailable(window)) throw new Error("Desktop window is unavailable.");
    const selection = await options.dialog.showSaveDialog(window, {
      title: "Export managed artifact", defaultPath: command.artifactId,
    });
    if (selection.canceled || !selection.filePath) return null;
    const value = await options.runtime.exportArtifact(command.artifactId, selection.filePath, `export-${randomUUID()}`);
    if (!isArtifactExportResult(value)) throw new Error("Invalid artifact export result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.getGerberReview, async (event, ...args) => {
    const command = assertOne(event, args, isGerberReviewCommand);
    const value = await options.runtime.getGerberReview(command.runId);
    if (!isGerberReviewResult(value)) throw new Error("Invalid Gerber review result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.getGerberPreview, async (event, ...args) => {
    const command = assertOne(event, args, isGerberReviewCommand);
    const value = await options.runtime.getGerberPreview(command.runId);
    if (!isGerberReviewResult(value)) throw new Error("Invalid Gerber preview result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.acceptGerber, async (event, ...args) => {
    const command = assertOne(event, args, isGerberDecisionCommand);
    const value = await options.runtime.acceptGerber(command);
    if (!isGerberReviewResult(value)) throw new Error("Invalid Gerber decision result.");
    return value;
  });
  options.ipcMain.handle(IPC_CHANNELS.rejectGerber, async (event, ...args) => {
    const command = assertOne(event, args, isGerberDecisionCommand);
    const value = await options.runtime.rejectGerber(command);
    if (!isGerberReviewResult(value)) throw new Error("Invalid Gerber decision result.");
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
      IPC_CHANNELS.startTurn,
      IPC_CHANNELS.cancelTurn,
      IPC_CHANNELS.resolveApproval,
      IPC_CHANNELS.listSubagents, IPC_CHANNELS.startSubagent, IPC_CHANNELS.cancelSubagent,
      IPC_CHANNELS.takeoverSubagent, IPC_CHANNELS.resolveSubagentApproval,
      IPC_CHANNELS.resumeTurn,
      IPC_CHANNELS.restartTurn,
      IPC_CHANNELS.getChanges,
      IPC_CHANNELS.mutateChanges,
      IPC_CHANNELS.listReports,
      IPC_CHANNELS.getReport,
      IPC_CHANNELS.listArtifacts,
      IPC_CHANNELS.getArtifact,
      IPC_CHANNELS.openTerminal, IPC_CHANNELS.inputTerminal, IPC_CHANNELS.resizeTerminal,
      IPC_CHANNELS.cancelTerminal, IPC_CHANNELS.closeTerminal, IPC_CHANNELS.getTerminal, IPC_CHANNELS.listTerminalProfiles,
      IPC_CHANNELS.getSettings, IPC_CHANNELS.setSettings,
      IPC_CHANNELS.previewArtifact, IPC_CHANNELS.exportArtifact, IPC_CHANNELS.verifyArtifact,
      IPC_CHANNELS.getGerberReview, IPC_CHANNELS.getGerberPreview, IPC_CHANNELS.acceptGerber, IPC_CHANNELS.rejectGerber,
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
