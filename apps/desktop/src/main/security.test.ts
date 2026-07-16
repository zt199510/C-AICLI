import { describe, expect, it } from "vitest";
import { IPC_CHANNELS, isRuntimeStatus } from "../shared/bridge-contract";
import { createWebPreferences } from "./security";

describe("desktop security baseline", () => {
  it("keeps renderer isolated from Node and process APIs", () => {
    const preferences = createWebPreferences("C:\\app\\preload.cjs");

    expect(preferences.contextIsolation).toBe(true);
    expect(preferences.nodeIntegration).toBe(false);
    expect(preferences.sandbox).toBe(true);
    expect(preferences.webSecurity).toBe(true);
    expect(preferences.allowRunningInsecureContent).toBe(false);
    expect(Object.isFrozen(preferences)).toBe(true);
  });

  it("exposes only the reviewed bridge channels", () => {
    expect(Object.values(IPC_CHANNELS)).toEqual([
      "desktop:initialize",
      "workspace:open",
      "runtime:status",
    ]);
    expect(isRuntimeStatus({ state: "ready", detail: "desktop-v1" })).toBe(true);
    expect(isRuntimeStatus({ state: "ready", detail: "x".repeat(513) })).toBe(false);
    expect(isRuntimeStatus({ state: "unknown", detail: "desktop-v1" })).toBe(false);
  });
});
