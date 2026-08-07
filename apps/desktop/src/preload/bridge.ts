import {
  isArtifactGetResult,
  isArtifactListResult,
  isTerminalStateResult, isTerminalProfileListResult, isArtifactReviewResult, isArtifactExportResult, isGerberReviewResult,
  isChangesGetResult, isChangesMutateResult,
  isCatalogListResult,
  isComposerStateResult,
  isTurnExecutionStateResult,
  isSubagentResult,
  isContextSearchResult,
  isReportGetResult,
  isReportListResult,
  isThreadChangedParams,
  isThreadGetResult,
  isThreadListResult,
  isThreadSummaryResult,
  isWorkspaceOpenResult,
  isWorkspaceSnapshotData,
  type ThreadChangedParams,
} from "../generated/desktop-contracts";
import {
  IPC_CHANNELS,
  assertRuntimeStatus,
  isClearComposerCommand,
  isContextPickResult,
  isEnqueueComposerCommand,
  isGetComposerCommand,
  isListCatalogCommand,
  isSearchContextCommand,
  isArchiveThreadCommand,
  isCreateThreadCommand,
  isGetArtifactCommand,
  isGetChangesCommand, isMutateChangesCommand,
  isGetReportCommand,
  isGetThreadCommand,
  isRenameThreadCommand,
  isRuntimeStatus,
  isStartTurnCommand,
  isCancelTurnCommand,
  isResolveApprovalCommand,
  isListSubagentsCommand, isStartSubagentCommand, isSubagentMutationCommand, isResolveSubagentApprovalCommand,
  isResumeTurnCommand,
  isRestartTurnCommand,
  isGetSettingsCommand, isSetSettingsCommand, isDesktopSettingsSnapshot,
  isContextPickCommand,
  isOpenTerminalCommand, isInputTerminalCommand, isResizeTerminalCommand, isTerminalMutationCommand,
  isGetTerminalCommand, isArtifactReviewCommand, isGerberReviewCommand, isGerberDecisionCommand,
  type ArchiveThreadCommand,
  type CreateThreadCommand,
  type DesktopBridge,
  type GetArtifactCommand,
  type GetChangesCommand,
  type MutateChangesCommand,
  type GetReportCommand,
  type GetThreadCommand,
  type ClearComposerCommand,
  type EnqueueComposerCommand,
  type GetComposerCommand,
  type ListCatalogCommand,
  type SearchContextCommand,
  type RenameThreadCommand,
  type RuntimeStatus,
  type StartTurnCommand,
  type CancelTurnCommand,
  type ResolveApprovalCommand,
  type ListSubagentsCommand, type StartSubagentCommand, type SubagentMutationCommand, type ResolveSubagentApprovalCommand,
  type ResumeTurnCommand,
  type RestartTurnCommand,
  type GetSettingsCommand, type SetSettingsCommand,
  type OpenTerminalCommand, type InputTerminalCommand, type ResizeTerminalCommand, type TerminalMutationCommand,
  type GetTerminalCommand, type ArtifactReviewCommand, type GerberReviewCommand, type GerberDecisionCommand,
} from "../shared/bridge-contract";

export interface IpcRendererAdapter {
  invoke(channel: string, ...args: unknown[]): Promise<unknown>;
  on(channel: string, listener: (event: unknown, value: unknown) => void): void;
  removeListener(channel: string, listener: (event: unknown, value: unknown) => void): void;
}

export function createDesktopBridge(ipc: IpcRendererAdapter): DesktopBridge {
  const validated = async <T>(channel: string, validator: (value: unknown) => value is T, ...args: unknown[]): Promise<T> => {
    const value = await ipc.invoke(channel, ...args);
    if (!validator(value)) throw new Error("Invalid desktop result.");
    return deepFreeze(value);
  };
  return Object.freeze({
    async getRuntimeStatus() {
      return assertRuntimeStatus(await ipc.invoke(IPC_CHANNELS.getRuntimeStatus));
    },
    async restartRuntime() {
      return assertRuntimeStatus(await ipc.invoke(IPC_CHANNELS.restartRuntime));
    },
    async openWorkspace() {
      const value = await ipc.invoke(IPC_CHANNELS.openWorkspace);
      if (value === null) return null;
      if (!isWorkspaceOpenResult(value)) throw new Error("Invalid workspace result.");
      return value;
    },
    async getWorkspaceSnapshot() {
      const value = await ipc.invoke(IPC_CHANNELS.getWorkspaceSnapshot);
      if (value !== null && !isWorkspaceSnapshotData(value)) throw new Error("Invalid workspace snapshot.");
      return value;
    },
    listThreads: () => validated(IPC_CHANNELS.listThreads, isThreadListResult),
    getThread(command: GetThreadCommand) {
      if (!isGetThreadCommand(command)) return Promise.reject(new Error("Invalid thread command."));
      return validated(IPC_CHANNELS.getThread, isThreadGetResult, command);
    },
    createThread(command: CreateThreadCommand) {
      if (!isCreateThreadCommand(command)) return Promise.reject(new Error("Invalid create command."));
      return validated(IPC_CHANNELS.createThread, isThreadSummaryResult, command);
    },
    renameThread(command: RenameThreadCommand) {
      if (!isRenameThreadCommand(command)) return Promise.reject(new Error("Invalid rename command."));
      return validated(IPC_CHANNELS.renameThread, isThreadSummaryResult, command);
    },
    archiveThread(command: ArchiveThreadCommand) {
      if (!isArchiveThreadCommand(command)) return Promise.reject(new Error("Invalid archive command."));
      return validated(IPC_CHANNELS.archiveThread, isThreadSummaryResult, command);
    },
    getChanges(command: GetChangesCommand = {}) {
      if (!isGetChangesCommand(command)) return Promise.reject(new Error("Invalid changes command."));
      return validated(IPC_CHANNELS.getChanges, isChangesGetResult, command);
    },
    listReports: () => validated(IPC_CHANNELS.listReports, isReportListResult),
    getReport(command: GetReportCommand) {
      if (!isGetReportCommand(command)) return Promise.reject(new Error("Invalid report command."));
      return validated(IPC_CHANNELS.getReport, isReportGetResult, command);
    },
    listArtifacts: () => validated(IPC_CHANNELS.listArtifacts, isArtifactListResult),
    getArtifact(command: GetArtifactCommand) {
      if (!isGetArtifactCommand(command)) return Promise.reject(new Error("Invalid artifact command."));
      return validated(IPC_CHANNELS.getArtifact, isArtifactGetResult, command);
    },
    openTerminal(command: OpenTerminalCommand) {
      if (!isOpenTerminalCommand(command)) return Promise.reject(new Error("Invalid terminal open command."));
      return validated(IPC_CHANNELS.openTerminal, isTerminalStateResult, command);
    },
    inputTerminal(command: InputTerminalCommand) {
      if (!isInputTerminalCommand(command)) return Promise.reject(new Error("Invalid terminal input command."));
      return validated(IPC_CHANNELS.inputTerminal, isTerminalStateResult, command);
    },
    resizeTerminal(command: ResizeTerminalCommand) {
      if (!isResizeTerminalCommand(command)) return Promise.reject(new Error("Invalid terminal resize command."));
      return validated(IPC_CHANNELS.resizeTerminal, isTerminalStateResult, command);
    },
    cancelTerminal(command: TerminalMutationCommand) {
      if (!isTerminalMutationCommand(command)) return Promise.reject(new Error("Invalid terminal cancel command."));
      return validated(IPC_CHANNELS.cancelTerminal, isTerminalStateResult, command);
    },
    closeTerminal(command: TerminalMutationCommand) {
      if (!isTerminalMutationCommand(command)) return Promise.reject(new Error("Invalid terminal close command."));
      return validated(IPC_CHANNELS.closeTerminal, isTerminalStateResult, command);
    },
    getTerminal(command: GetTerminalCommand) {
      if (!isGetTerminalCommand(command)) return Promise.reject(new Error("Invalid terminal get command."));
      return validated(IPC_CHANNELS.getTerminal, isTerminalStateResult, command);
    },
    mutateChanges(command: MutateChangesCommand) {
      if (!isMutateChangesCommand(command)) return Promise.reject(new Error("Invalid changes mutation command."));
      return validated(IPC_CHANNELS.mutateChanges, isChangesMutateResult, command);
    },
    listTerminalProfiles: () => validated(IPC_CHANNELS.listTerminalProfiles, isTerminalProfileListResult),
    previewArtifact(command: ArtifactReviewCommand) {
      if (!isArtifactReviewCommand(command)) return Promise.reject(new Error("Invalid artifact preview command."));
      return validated(IPC_CHANNELS.previewArtifact, isArtifactReviewResult, command);
    },
    async exportArtifact(command: ArtifactReviewCommand) {
      if (!isArtifactReviewCommand(command)) throw new Error("Invalid artifact export command.");
      const value = await ipc.invoke(IPC_CHANNELS.exportArtifact, command);
      if (value === null) return null;
      if (!isArtifactExportResult(value)) throw new Error("Invalid artifact export result.");
      return deepFreeze(value);
    },
    verifyArtifact(command: ArtifactReviewCommand) {
      if (!isArtifactReviewCommand(command)) return Promise.reject(new Error("Invalid artifact verify command."));
      return validated(IPC_CHANNELS.verifyArtifact, isArtifactReviewResult, command);
    },
    getGerberReview(command: GerberReviewCommand) {
      if (!isGerberReviewCommand(command)) return Promise.reject(new Error("Invalid Gerber review command."));
      return validated(IPC_CHANNELS.getGerberReview, isGerberReviewResult, command);
    },
    getGerberPreview(command: GerberReviewCommand) {
      if (!isGerberReviewCommand(command)) return Promise.reject(new Error("Invalid Gerber preview command."));
      return validated(IPC_CHANNELS.getGerberPreview, isGerberReviewResult, command);
    },
    acceptGerber(command: GerberDecisionCommand) {
      if (!isGerberDecisionCommand(command)) return Promise.reject(new Error("Invalid Gerber decision command."));
      return validated(IPC_CHANNELS.acceptGerber, isGerberReviewResult, command);
    },
    rejectGerber(command: GerberDecisionCommand) {
      if (!isGerberDecisionCommand(command)) return Promise.reject(new Error("Invalid Gerber decision command."));
      return validated(IPC_CHANNELS.rejectGerber, isGerberReviewResult, command);
    },
    listCatalog(command: ListCatalogCommand) {
      if (!isListCatalogCommand(command)) return Promise.reject(new Error("Invalid catalog command."));
      return validated(IPC_CHANNELS.listCatalog, isCatalogListResult, command);
    },
    searchContext(command: SearchContextCommand) {
      if (!isSearchContextCommand(command)) return Promise.reject(new Error("Invalid context search command."));
      return validated(IPC_CHANNELS.searchContext, isContextSearchResult, command);
    },
    pickFile(command = {}) {
      if (!isContextPickCommand(command)) return Promise.reject(new Error("Invalid context picker command."));
      return validated(IPC_CHANNELS.pickFile, isContextPickResult, command);
    },
    pickFolder(command = {}) {
      if (!isContextPickCommand(command)) return Promise.reject(new Error("Invalid context picker command."));
      return validated(IPC_CHANNELS.pickFolder, isContextPickResult, command);
    },
    getComposer(command: GetComposerCommand) {
      if (!isGetComposerCommand(command)) return Promise.reject(new Error("Invalid composer command."));
      return validated(IPC_CHANNELS.getComposer, isComposerStateResult, command);
    },
    enqueueComposer(command: EnqueueComposerCommand) {
      if (!isEnqueueComposerCommand(command)) return Promise.reject(new Error("Invalid composer enqueue command."));
      return validated(IPC_CHANNELS.enqueueComposer, isComposerStateResult, command);
    },
    clearComposer(command: ClearComposerCommand) {
      if (!isClearComposerCommand(command)) return Promise.reject(new Error("Invalid composer clear command."));
      return validated(IPC_CHANNELS.clearComposer, isComposerStateResult, command);
    },
    startTurn(command: StartTurnCommand) {
      if (!isStartTurnCommand(command)) return Promise.reject(new Error("Invalid turn start command."));
      return validated(IPC_CHANNELS.startTurn, isTurnExecutionStateResult, command);
    },
    cancelTurn(command: CancelTurnCommand) {
      if (!isCancelTurnCommand(command)) return Promise.reject(new Error("Invalid turn cancel command."));
      return validated(IPC_CHANNELS.cancelTurn, isTurnExecutionStateResult, command);
    },
    resolveApproval(command: ResolveApprovalCommand) {
      if (!isResolveApprovalCommand(command)) return Promise.reject(new Error("Invalid approval command."));
      return validated(IPC_CHANNELS.resolveApproval, isTurnExecutionStateResult, command);
    },
    listSubagents(command: ListSubagentsCommand) {
      if (!isListSubagentsCommand(command)) return Promise.reject(new Error("Invalid Sub-agent list command."));
      return validated(IPC_CHANNELS.listSubagents, isSubagentResult, command);
    },
    startSubagent(command: StartSubagentCommand) {
      if (!isStartSubagentCommand(command)) return Promise.reject(new Error("Invalid Sub-agent start command."));
      return validated(IPC_CHANNELS.startSubagent, isSubagentResult, command);
    },
    cancelSubagent(command: SubagentMutationCommand) {
      if (!isSubagentMutationCommand(command)) return Promise.reject(new Error("Invalid Sub-agent cancel command."));
      return validated(IPC_CHANNELS.cancelSubagent, isSubagentResult, command);
    },
    takeoverSubagent(command: SubagentMutationCommand) {
      if (!isSubagentMutationCommand(command)) return Promise.reject(new Error("Invalid Sub-agent takeover command."));
      return validated(IPC_CHANNELS.takeoverSubagent, isSubagentResult, command);
    },
    resolveSubagentApproval(command: ResolveSubagentApprovalCommand) {
      if (!isResolveSubagentApprovalCommand(command)) return Promise.reject(new Error("Invalid Sub-agent approval command."));
      return validated(IPC_CHANNELS.resolveSubagentApproval, isSubagentResult, command);
    },
    resumeTurn(command: ResumeTurnCommand) {
      if (!isResumeTurnCommand(command)) return Promise.reject(new Error("Invalid turn resume command."));
      return validated(IPC_CHANNELS.resumeTurn, isTurnExecutionStateResult, command);
    },
    restartTurn(command: RestartTurnCommand) {
      if (!isRestartTurnCommand(command)) return Promise.reject(new Error("Invalid turn restart command."));
      return validated(IPC_CHANNELS.restartTurn, isTurnExecutionStateResult, command);
    },
    getSettings(command: GetSettingsCommand) {
      if (!isGetSettingsCommand(command)) return Promise.reject(new Error("Invalid settings command."));
      return validated(IPC_CHANNELS.getSettings, isDesktopSettingsSnapshot, command);
    },
    setSettings(command: SetSettingsCommand) {
      if (!isSetSettingsCommand(command)) return Promise.reject(new Error("Invalid settings command."));
      return validated(IPC_CHANNELS.setSettings, isDesktopSettingsSnapshot, command);
    },
    onRuntimeStatus(listener: (status: RuntimeStatus) => void) {
      const wrapped = (_event: unknown, value: unknown) => {
        if (isRuntimeStatus(value)) listener(value);
      };
      ipc.on(IPC_CHANNELS.runtimeStatus, wrapped);
      return () => ipc.removeListener(IPC_CHANNELS.runtimeStatus, wrapped);
    },
    onThreadChanged(listener: (event: ThreadChangedParams) => void) {
      const wrapped = (_event: unknown, value: unknown) => {
        if (isThreadChangedParams(value)) listener(value);
      };
      ipc.on(IPC_CHANNELS.threadChanged, wrapped);
      return () => ipc.removeListener(IPC_CHANNELS.threadChanged, wrapped);
    },
  });
}

function deepFreeze<T>(value: T): T {
  if (value !== null && typeof value === "object" && !Object.isFrozen(value)) {
    for (const item of Object.values(value as Record<string, unknown>)) deepFreeze(item);
    Object.freeze(value);
  }
  return value;
}
