import { contextBridge, ipcRenderer } from "electron";
import {
  IPC_CHANNELS,
  isRuntimeStatus,
  type DesktopBridge,
  type RuntimeStatus,
} from "../shared/bridge-contract";

const bridge: DesktopBridge = Object.freeze({
  initialize: () => ipcRenderer.invoke(IPC_CHANNELS.initialize),
  openWorkspace: () => ipcRenderer.invoke(IPC_CHANNELS.openWorkspace),
  onRuntimeStatus: (listener: (status: RuntimeStatus) => void) => {
    const wrapped = (_event: Electron.IpcRendererEvent, value: unknown) => {
      if (isRuntimeStatus(value)) listener(value);
    };
    ipcRenderer.on(IPC_CHANNELS.runtimeStatus, wrapped);
    return () => ipcRenderer.removeListener(IPC_CHANNELS.runtimeStatus, wrapped);
  },
});

contextBridge.exposeInMainWorld("caicli", bridge);
