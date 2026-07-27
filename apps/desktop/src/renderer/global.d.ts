import type { DesktopBridge } from "../shared/bridge-contract";
import type { MemoryDiagnosticCounter } from "./memory-diagnostics";

declare global {
  interface Window {
    caicli: DesktopBridge;
    caicliMemoryDiagnostics?: {
      increment(counter: MemoryDiagnosticCounter): void;
    };
  }
}

export {};
