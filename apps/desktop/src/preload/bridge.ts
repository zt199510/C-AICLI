import { isWorkspaceOpenResult } from "../generated/desktop-contracts";
import {
  IPC_CHANNELS,
  assertRuntimeStatus,
  isRuntimeStatus,
  type DesktopBridge,
  type RuntimeStatus,
} from "../shared/bridge-contract";

export interface IpcRendererAdapter {
  invoke(channel: string, ...args: unknown[]): Promise<unknown>;
  on(channel: string, listener: (event: unknown, value: unknown) => void): void;
  removeListener(channel: string, listener: (event: unknown, value: unknown) => void): void;
}

export function createDesktopBridge(ipc: IpcRendererAdapter): DesktopBridge {
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
    onRuntimeStatus(listener: (status: RuntimeStatus) => void) {
      const wrapped = (_event: unknown, value: unknown) => {
        if (isRuntimeStatus(value)) listener(value);
      };
      ipc.on(IPC_CHANNELS.runtimeStatus, wrapped);
      return () => ipc.removeListener(IPC_CHANNELS.runtimeStatus, wrapped);
    },
  });
}
