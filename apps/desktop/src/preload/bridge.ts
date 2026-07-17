import {
  isArtifactGetResult,
  isArtifactListResult,
  isChangesGetResult,
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
  isArchiveThreadCommand,
  isCreateThreadCommand,
  isGetArtifactCommand,
  isGetChangesCommand,
  isGetReportCommand,
  isGetThreadCommand,
  isRenameThreadCommand,
  isRuntimeStatus,
  type ArchiveThreadCommand,
  type CreateThreadCommand,
  type DesktopBridge,
  type GetArtifactCommand,
  type GetChangesCommand,
  type GetReportCommand,
  type GetThreadCommand,
  type RenameThreadCommand,
  type RuntimeStatus,
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
    return value;
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
