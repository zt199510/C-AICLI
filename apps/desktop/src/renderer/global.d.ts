import type { DesktopBridge } from "../shared/bridge-contract";

declare global {
  interface Window {
    caicli: DesktopBridge;
  }
}

export {};
