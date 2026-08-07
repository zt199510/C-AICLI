import fs from "node:fs/promises";
import { app, dialog, ipcMain, session } from "electron";
import type { BrowserWindow } from "electron";
import { IPC_CHANNELS } from "../shared/bridge-contract";
import { AppHostClient } from "./apphost-client";
import { resolveAppHostLaunch } from "./apphost-launch";
import { AppHostRuntime } from "./apphost-runtime";
import { isCurrentWindowSender, registerDesktopIpc } from "./ipc-bridge";
import { installSessionPolicy } from "./security";
import { createDesktopWindow, focusDesktopWindow } from "./window";
import { isRuntimeWindowAvailable, sendRuntimeStatus } from "./window-lifecycle";
import { LocalSettingsStore } from "./local-settings-store";

let mainWindow: BrowserWindow | null = null;
let shutdownStarted = false;
let disposeIpc: (() => void) | null = null;

const runtime = new AppHostRuntime({
  createClient: () => new AppHostClient(),
  resolveLaunch: () => resolveAppHostLaunch({
    appIsPackaged: app.isPackaged,
    appPath: app.getAppPath(),
    resourcesPath: process.resourcesPath,
    environment: process.env,
  }),
});

const hasInstanceLock = app.requestSingleInstanceLock();
if (!hasInstanceLock) app.quit();

app.on("second-instance", () => { focusDesktopWindow(mainWindow); });

runtime.subscribe((status) => {
  sendRuntimeStatus(mainWindow, IPC_CHANNELS.runtimeStatus, status);
});
runtime.subscribeThreadChanged((event) => {
  if (isRuntimeWindowAvailable(mainWindow)) mainWindow.webContents.send(IPC_CHANNELS.threadChanged, event);
});

if (hasInstanceLock) {
  void app.whenReady().then(async () => {
    installSessionPolicy(session.defaultSession, app);

    if (process.env.CAICLI_DESKTOP_SMOKE === "1") {
      const status = await runtime.start();
      await runtime.stop();
      app.exit(status.state === "ready" ? 0 : 2);
      return;
    }

    if (process.env.CAICLI_DESKTOP_CRASH_RESTART === "1") {
      const started = await runtime.start();
      if (started.state !== "ready") { app.exit(2); return; }
      runtime.forceTerminateForTest();
      const failed = await waitForRuntimeState("failed", 10_000);
      const restarted = failed ? await runtime.restart() : runtime.getStatus();
      await runtime.stop();
      app.exit(failed && restarted.state === "ready" ? 0 : 2);
      return;
    }

    mainWindow = createDesktopWindow({
      app,
      developmentUrl: process.env.VITE_DEV_SERVER_URL,
    });
    mainWindow.once("closed", () => { mainWindow = null; });
    disposeIpc = registerDesktopIpc({
      ipcMain,
      runtime,
      dialog,
      settingsStore: new LocalSettingsStore(app.getPath("userData") + "\\desktop-settings-v1.json"),
      getWindow: () => mainWindow,
      isAllowedSender: (event) => isCurrentWindowSender(event, mainWindow),
    });
    await runtime.start();
    await captureShellIfRequested();
    installSmokeTimers();
  });
}

app.on("window-all-closed", () => app.quit());
app.on("before-quit", (event) => {
  if (shutdownStarted) return;
  shutdownStarted = true;
  event.preventDefault();
  disposeIpc?.();
  disposeIpc = null;
  const deadline = setTimeout(() => app.exit(0), 2500);
  void runtime.stop().finally(() => {
    clearTimeout(deadline);
    app.quit();
  });
});

function installSmokeTimers(): void {
  const autoExitMilliseconds = parseSmokeDelay(process.env.CAICLI_DESKTOP_AUTO_EXIT_MS);
  if (autoExitMilliseconds !== null) setTimeout(() => app.quit(), autoExitMilliseconds).unref();

  const closeWindowMilliseconds = parseSmokeDelay(process.env.CAICLI_DESKTOP_CLOSE_WINDOW_MS);
  if (closeWindowMilliseconds !== null) {
    setTimeout(() => {
      if (isRuntimeWindowAvailable(mainWindow)) mainWindow.close();
    }, closeWindowMilliseconds).unref();
  }
}

function parseSmokeDelay(value: string | undefined): number | null {
  const parsed = Number.parseInt(value ?? "", 10);
  return Number.isSafeInteger(parsed) && parsed >= 1000 ? parsed : null;
}

function waitForRuntimeState(state: "failed", timeoutMs: number): Promise<boolean> {
  if (runtime.getStatus().state === state) return Promise.resolve(true);
  return new Promise((resolve) => {
    const timeout = setTimeout(() => { unsubscribe(); resolve(false); }, timeoutMs);
    const unsubscribe = runtime.subscribe((status) => {
      if (status.state !== state) return;
      clearTimeout(timeout);
      unsubscribe();
      resolve(true);
    });
  });
}

async function captureShellIfRequested(): Promise<void> {
  const capturePath = process.env.CAICLI_DESKTOP_CAPTURE_PATH;
  if (!capturePath || !isRuntimeWindowAvailable(mainWindow)) return;
  const window = mainWindow;
  const width = parseCaptureDimension(process.env.CAICLI_DESKTOP_CAPTURE_WIDTH, 1440);
  const height = parseCaptureDimension(process.env.CAICLI_DESKTOP_CAPTURE_HEIGHT, 900);
  window.setContentSize(width, height);
  if (window.webContents.isLoading()) {
    await new Promise<void>((resolve) => window.webContents.once("did-finish-load", () => resolve()));
  }
  const image = await window.webContents.capturePage();
  await fs.writeFile(capturePath, image.toPNG());
  app.quit();
}

function parseCaptureDimension(value: string | undefined, fallback: number): number {
  const parsed = Number.parseInt(value ?? "", 10);
  return Number.isSafeInteger(parsed) && parsed >= 320 && parsed <= 4096 ? parsed : fallback;
}
