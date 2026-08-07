import { describe, expect, it, vi } from "vitest";
import { IPC_CHANNELS, createRuntimeStatus, isRuntimeStatus } from "../shared/bridge-contract";
import type { App, Session, WebContents } from "electron";
import { applyNavigationPolicy, createWebPreferences, installSessionPolicy } from "./security";

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
      "workspace:get-snapshot",
      "thread:list",
      "thread:get",
      "thread:create",
      "thread:rename",
      "thread:archive",
      "thread:changed",
      "changes:get",
      "changes:mutate",
      "report:list",
      "report:get",
      "artifact:list",
      "artifact:get",
      "terminal:open", "terminal:input", "terminal:resize", "terminal:cancel", "terminal:close", "terminal:get", "terminal:profiles:get",
      "artifact:preview", "artifact:export", "artifact:verify",
      "gerber:review:get", "gerber:preview", "gerber:accept", "gerber:reject",
      "catalog:list",
      "context:search",
      "context:pick-file",
      "context:pick-folder",
      "composer:get",
      "composer:enqueue",
      "composer:clear",
      "turn:start",
      "turn:cancel",
      "approval:resolve",
      "subagent:list",
      "subagent:start",
      "subagent:cancel",
      "subagent:takeover",
      "subagent:approval:resolve",
      "turn:resume",
      "turn:restart",
      "runtime:status",
      "settings:get",
      "settings:set",
    ]);
    const ready = createRuntimeStatus("runtime-ready");
    expect(isRuntimeStatus(ready)).toBe(true);
    expect(isRuntimeStatus({ ...ready, unknown: true })).toBe(false);
    expect(isRuntimeStatus({ ...ready, message: "x".repeat(257) })).toBe(false);
    expect(isRuntimeStatus({ ...ready, state: "failed" })).toBe(false);
    expect(isRuntimeStatus({ ...ready, canRestart: true })).toBe(false);
  });

  it("denies new windows, cross-document navigation and webviews", () => {
    const handlers = new Map<string, (...args: unknown[]) => void>();
    const contents = {
      getURL: () => "file:///app/dist/renderer/index.html",
      setWindowOpenHandler: (handler: () => unknown) => handlers.set("window-open", handler),
      on: (name: string, handler: (...args: unknown[]) => void) => handlers.set(name, handler),
    } as unknown as WebContents;
    applyNavigationPolicy(contents);
    expect(handlers.get("window-open")?.()).toEqual({ action: "deny" });
    const external = { preventDefault: vi.fn() };
    handlers.get("will-navigate")?.(external, "https://example.invalid/");
    expect(external.preventDefault).toHaveBeenCalledOnce();
    const sameDocument = { preventDefault: vi.fn() };
    handlers.get("will-navigate")?.(sameDocument, "file:///app/dist/renderer/index.html");
    expect(sameDocument.preventDefault).not.toHaveBeenCalled();
    const webview = { preventDefault: vi.fn() };
    handlers.get("will-attach-webview")?.(webview);
    expect(webview.preventDefault).toHaveBeenCalledOnce();
  });

  it("denies permission requests, checks and downloads", () => {
    let requestPermission: ((...args: unknown[]) => void) | undefined;
    let checkPermission: ((...args: unknown[]) => boolean) | undefined;
    let download: ((...args: unknown[]) => void) | undefined;
    const session = {
      setPermissionRequestHandler: (handler: (...args: unknown[]) => void) => { requestPermission = handler; },
      setPermissionCheckHandler: (handler: (...args: unknown[]) => boolean) => { checkPermission = handler; },
      on: (_name: string, handler: (...args: unknown[]) => void) => { download = handler; },
    } as unknown as Session;
    const app = { on: vi.fn() } as unknown as App;
    installSessionPolicy(session, app);
    const permissionCallback = vi.fn();
    requestPermission?.({}, "clipboard-read", permissionCallback);
    expect(permissionCallback).toHaveBeenCalledWith(false);
    expect(checkPermission?.()).toBe(false);
    const event = { preventDefault: vi.fn() };
    const item = { cancel: vi.fn() };
    download?.(event, item);
    expect(event.preventDefault).toHaveBeenCalledOnce();
    expect(item.cancel).toHaveBeenCalledOnce();
  });
});
