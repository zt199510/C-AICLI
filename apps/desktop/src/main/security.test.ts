import { describe, expect, it } from "vitest";
import { IPC_CHANNELS, createRuntimeStatus, isRuntimeStatus } from "../shared/bridge-contract";
import { createWebPreferences } from "./security";

describe("desktop security baseline", () => {
  it("keeps renderer isolated from Node and process APIs", () => {
    const preferences = createWebPreferences("C:\\app\\preload.cjs");

    expect(preferences.contextIsolation).toBe(true);
    expect(preferences.nodeIntegration).toBe(false);
    expect(preferences.sandbox).toBe(true);
    expect(preferences.webSecurity).toBe(true);
    expect(preferences.allowRunningInsecureContent).toBe(false);
    expect(preferences.webviewTag).toBe(false);
    expect(preferences.navigateOnDragDrop).toBe(false);
    expect(preferences.devTools).toBe(false);
    expect(createWebPreferences("preload", false).devTools).toBe(true);
    expect(Object.isFrozen(preferences)).toBe(true);
  });

  it("exposes only the reviewed bridge channels", () => {
    expect(Object.values(IPC_CHANNELS)).toEqual([
      "runtime:get-status",
      "runtime:restart",
      "workspace:open",
      "runtime:status",
    ]);
    const ready = createRuntimeStatus("runtime-ready");
    expect(isRuntimeStatus(ready)).toBe(true);
    expect(isRuntimeStatus({ ...ready, unknown: true })).toBe(false);
    expect(isRuntimeStatus({ ...ready, message: "x".repeat(257) })).toBe(false);
    expect(isRuntimeStatus({ ...ready, state: "failed" })).toBe(false);
    expect(isRuntimeStatus({ ...ready, canRestart: true })).toBe(false);
  });
});
