import type { WorkspaceOpenResult } from "../generated/desktop-contracts";

export const IPC_CHANNELS = Object.freeze({
  getRuntimeStatus: "runtime:get-status",
  restartRuntime: "runtime:restart",
  openWorkspace: "workspace:open",
  runtimeStatus: "runtime:status",
});

export type RuntimeState =
  | "starting"
  | "ready"
  | "restarting"
  | "stopping"
  | "stopped"
  | "failed";

export type RuntimeCode =
  | "runtime-starting"
  | "runtime-ready"
  | "runtime-restarting"
  | "runtime-stopping"
  | "runtime-stopped"
  | "apphost-start-failed"
  | "apphost-exited"
  | "protocol-invalid"
  | "restart-failed";

export interface RuntimeStatus {
  schemaVersion: 1;
  state: RuntimeState;
  code: RuntimeCode;
  message: string;
  canRestart: boolean;
  protocolVersion: "desktop-v1" | null;
}

export interface DesktopBridge {
  getRuntimeStatus(): Promise<RuntimeStatus>;
  restartRuntime(): Promise<RuntimeStatus>;
  openWorkspace(): Promise<WorkspaceOpenResult | null>;
  onRuntimeStatus(listener: (status: RuntimeStatus) => void): () => void;
}

const allowedCombinations = Object.freeze({
  "runtime-starting": ["starting", "Starting AppHost", false, null],
  "runtime-ready": ["ready", "AppHost ready", false, "desktop-v1"],
  "runtime-restarting": ["restarting", "Restarting AppHost", false, null],
  "runtime-stopping": ["stopping", "Stopping AppHost", false, null],
  "runtime-stopped": ["stopped", "AppHost stopped", false, null],
  "apphost-start-failed": ["failed", "AppHost failed to start", true, null],
  "apphost-exited": ["failed", "AppHost stopped unexpectedly", true, null],
  "protocol-invalid": ["failed", "AppHost protocol validation failed", true, null],
  "restart-failed": ["failed", "AppHost restart failed", true, null],
} as const satisfies Record<RuntimeCode, readonly [RuntimeState, string, boolean, "desktop-v1" | null]>);

const statusKeys = Object.freeze([
  "schemaVersion",
  "state",
  "code",
  "message",
  "canRestart",
  "protocolVersion",
] as const);

export function createRuntimeStatus(code: RuntimeCode): RuntimeStatus {
  const [state, message, canRestart, protocolVersion] = allowedCombinations[code];
  return Object.freeze({
    schemaVersion: 1,
    state,
    code,
    message,
    canRestart,
    protocolVersion,
  });
}

export function isRuntimeStatus(value: unknown): value is RuntimeStatus {
  if (!isRecord(value)) return false;
  const keys = Object.keys(value).sort();
  if (keys.length !== statusKeys.length || !statusKeys.every((key) => keys.includes(key))) {
    return false;
  }
  if (value.schemaVersion !== 1 || typeof value.code !== "string") return false;
  if (!Object.hasOwn(allowedCombinations, value.code)) return false;
  if (typeof value.message !== "string" || new TextEncoder().encode(value.message).length > 256) {
    return false;
  }
  const [state, message, canRestart, protocolVersion] =
    allowedCombinations[value.code as RuntimeCode];
  return (
    value.state === state &&
    value.message === message &&
    value.canRestart === canRestart &&
    value.protocolVersion === protocolVersion
  );
}

export function assertRuntimeStatus(value: unknown): RuntimeStatus {
  if (!isRuntimeStatus(value)) throw new Error("Invalid runtime status.");
  return value;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
