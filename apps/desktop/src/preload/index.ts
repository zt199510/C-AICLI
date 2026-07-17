import { contextBridge, ipcRenderer } from "electron";
import { createDesktopBridge } from "./bridge";

const bridge = createDesktopBridge({
  invoke: (channel, ...args) => ipcRenderer.invoke(channel, ...args),
  on: (channel, listener) => ipcRenderer.on(channel, listener),
  removeListener: (channel, listener) => ipcRenderer.removeListener(channel, listener),
});

contextBridge.exposeInMainWorld("caicli", bridge);
