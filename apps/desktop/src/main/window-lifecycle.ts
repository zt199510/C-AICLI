import type { RuntimeStatus } from "../shared/bridge-contract";

export interface RuntimeStatusTarget {
  isDestroyed(): boolean;
  webContents: {
    isDestroyed(): boolean;
    send(channel: string, status: RuntimeStatus): void;
  };
}

export function isRuntimeWindowAvailable(
  target: RuntimeStatusTarget | null,
): target is RuntimeStatusTarget {
  return target !== null && !target.isDestroyed() && !target.webContents.isDestroyed();
}

export function sendRuntimeStatus(
  target: RuntimeStatusTarget | null,
  channel: string,
  status: RuntimeStatus,
): boolean {
  if (!isRuntimeWindowAvailable(target)) return false;
  target.webContents.send(channel, status);
  return true;
}
