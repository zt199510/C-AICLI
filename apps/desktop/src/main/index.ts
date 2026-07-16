import path from "node:path";
import { app, BrowserWindow, dialog, ipcMain } from "electron";
import type { InitializeResult, WorkspaceOpenResult } from "../generated/desktop-contracts";
import { DESKTOP_METHODS, SCHEMA_VERSION } from "../generated/desktop-contracts";
import { IPC_CHANNELS, type RuntimeStatus } from "../shared/bridge-contract";
import { AppHostClient } from "./apphost-client";
import { resolveAppHostLaunch } from "./apphost-launch";
import { applyNavigationPolicy, createWebPreferences } from "./security";
import { isRuntimeWindowAvailable, sendRuntimeStatus } from "./window-lifecycle";

let mainWindow: BrowserWindow | null = null;
let initializeResult: InitializeResult | null = null;
let initializePromise: Promise<InitializeResult> | null = null;
let shutdownStarted = false;
const appHost = new AppHostClient();

function publishStatus(status: RuntimeStatus): void {
  sendRuntimeStatus(mainWindow, IPC_CHANNELS.runtimeStatus, status);
}

function createWindow(): BrowserWindow {
  const preloadPath = path.join(app.getAppPath(), "dist", "preload", "index.cjs");
  const window = new BrowserWindow({
    width: 1440,
    height: 900,
    minWidth: 900,
    minHeight: 620,
    show: false,
    backgroundColor: "#f4f5f6",
    webPreferences: createWebPreferences(preloadPath),
  });
  window.removeMenu();
  applyNavigationPolicy(window.webContents);
  window.once("ready-to-show", () => {
    if (!window.isDestroyed()) window.show();
  });
  window.once("closed", () => {
    if (mainWindow === window) mainWindow = null;
  });

  const developmentUrl = process.env.VITE_DEV_SERVER_URL;
  if (developmentUrl) void window.loadURL(developmentUrl);
  else void window.loadFile(path.join(app.getAppPath(), "dist", "renderer", "index.html"));
  return window;
}

function registerBridge(): void {
  ipcMain.handle(IPC_CHANNELS.initialize, () => {
    if (initializeResult) return initializeResult;
    if (initializePromise) return initializePromise;
    throw new Error("AppHost is not ready.");
  });
  ipcMain.handle(IPC_CHANNELS.openWorkspace, async (): Promise<WorkspaceOpenResult | null> => {
    if (!isRuntimeWindowAvailable(mainWindow) || !initializeResult) {
      throw new Error("AppHost is not ready.");
    }
    const selection = await dialog.showOpenDialog(mainWindow, {
      properties: ["openDirectory", "dontAddToRecent"],
      title: "Open workspace",
    });
    if (selection.canceled || selection.filePaths.length !== 1) return null;
    return appHost.request<WorkspaceOpenResult>(DESKTOP_METHODS.WorkspaceOpenMethod, {
      schemaVersion: SCHEMA_VERSION,
      path: selection.filePaths[0],
    });
  });
}

appHost.on("exit", () => {
  initializeResult = null;
  initializePromise = null;
  publishStatus({ state: "stopped", detail: "AppHost stopped" });
});

app.whenReady().then(async () => {
  if (process.env.CAICLI_DESKTOP_SMOKE === "1") {
    try {
      const initialized = await appHost.start(
        resolveAppHostLaunch({
          appIsPackaged: app.isPackaged,
          appPath: app.getAppPath(),
          resourcesPath: process.resourcesPath,
          environment: process.env,
        }),
      );
      if (initialized.protocolVersion !== "desktop-v1") throw new Error("Unexpected protocol version.");
      await appHost.stop();
      app.exit(0);
    } catch (error) {
      const detail = error instanceof Error ? error.message.slice(0, 512) : "unknown error";
      console.error(`desktop smoke failed: ${detail}`);
      await appHost.stop();
      app.exit(2);
    }
    return;
  }

  registerBridge();
  mainWindow = createWindow();
  publishStatus({ state: "starting", detail: "Starting AppHost" });
  try {
    initializePromise = appHost.start(
      resolveAppHostLaunch({
        appIsPackaged: app.isPackaged,
        appPath: app.getAppPath(),
        resourcesPath: process.resourcesPath,
        environment: process.env,
      }),
    );
    initializeResult = await initializePromise;
    publishStatus({ state: "ready", detail: initializeResult.protocolVersion });
  } catch {
    publishStatus({ state: "failed", detail: "AppHost failed to start" });
  } finally {
    initializePromise = null;
  }

  const autoExitMilliseconds = Number.parseInt(
    process.env.CAICLI_DESKTOP_AUTO_EXIT_MS ?? "",
    10,
  );
  if (Number.isSafeInteger(autoExitMilliseconds) && autoExitMilliseconds >= 1000) {
    setTimeout(() => app.quit(), autoExitMilliseconds).unref();
  }

  const closeWindowMilliseconds = Number.parseInt(
    process.env.CAICLI_DESKTOP_CLOSE_WINDOW_MS ?? "",
    10,
  );
  if (Number.isSafeInteger(closeWindowMilliseconds) && closeWindowMilliseconds >= 1000) {
    setTimeout(() => {
      if (isRuntimeWindowAvailable(mainWindow)) mainWindow.close();
    }, closeWindowMilliseconds).unref();
  }
});

app.on("window-all-closed", () => app.quit());
app.on("before-quit", (event) => {
  if (!shutdownStarted && appHost.isRunning()) {
    shutdownStarted = true;
    event.preventDefault();
    void appHost.stop().finally(() => {
      initializeResult = null;
      app.quit();
    });
  }
});
