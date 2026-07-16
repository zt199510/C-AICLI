import type { InitializeResult, WorkspaceOpenResult } from "../generated/desktop-contracts";

export const IPC_CHANNELS = Object.freeze({
  initialize: "desktop:initialize",
  openWorkspace: "workspace:open",
  runtimeStatus: "runtime:status",
});

export type RuntimeState = "starting" | "ready" | "stopped" | "failed";

export interface RuntimeStatus {
  state: RuntimeState;
  detail: string;
}

export interface DesktopBridge {
  initialize(): Promise<InitializeResult>;
  openWorkspace(): Promise<WorkspaceOpenResult | null>;
  onRuntimeStatus(listener: (status: RuntimeStatus) => void): () => void;
}

export function isRuntimeStatus(value: unknown): value is RuntimeStatus {
  if (typeof value !== "object" || value === null) return false;
  const candidate = value as Partial<RuntimeStatus>;
  return (
    (candidate.state === "starting" ||
      candidate.state === "ready" ||
      candidate.state === "stopped" ||
      candidate.state === "failed") &&
    typeof candidate.detail === "string" &&
    candidate.detail.length <= 512
  );
}
