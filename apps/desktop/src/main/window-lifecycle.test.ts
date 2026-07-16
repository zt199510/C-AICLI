import { describe, expect, it, vi } from "vitest";
import { sendRuntimeStatus, type RuntimeStatusTarget } from "./window-lifecycle";

function createTarget(windowDestroyed: boolean, webContentsDestroyed: boolean) {
  const send = vi.fn();
  const target: RuntimeStatusTarget = {
    isDestroyed: () => windowDestroyed,
    webContents: {
      isDestroyed: () => webContentsDestroyed,
      send,
    },
  };
  return { send, target };
}

describe("runtime window lifecycle", () => {
  it("sends status to a live window", () => {
    const { send, target } = createTarget(false, false);

    expect(sendRuntimeStatus(target, "runtime:status", { state: "ready", detail: "desktop-v1" }))
      .toBe(true);
    expect(send).toHaveBeenCalledOnce();
  });

  it("ignores a destroyed BrowserWindow", () => {
    const { send, target } = createTarget(true, false);

    expect(sendRuntimeStatus(target, "runtime:status", { state: "stopped", detail: "stopped" }))
      .toBe(false);
    expect(send).not.toHaveBeenCalled();
  });

  it("ignores destroyed webContents", () => {
    const { send, target } = createTarget(false, true);

    expect(sendRuntimeStatus(target, "runtime:status", { state: "stopped", detail: "stopped" }))
      .toBe(false);
    expect(send).not.toHaveBeenCalled();
  });
});
