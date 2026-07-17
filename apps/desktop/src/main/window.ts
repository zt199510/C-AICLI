import path from "node:path";
import { BrowserWindow, type App } from "electron";
import { applyNavigationPolicy, createWebPreferences, resolveDevelopmentUrl } from "./security";

export interface CreateDesktopWindowOptions {
  app: App;
  developmentUrl?: string;
}

export function createDesktopWindow(options: CreateDesktopWindowOptions): BrowserWindow {
  const preloadPath = path.join(options.app.getAppPath(), "dist", "preload", "index.cjs");
  const window = new BrowserWindow({
    width: 1440,
    height: 900,
    minWidth: 760,
    minHeight: 560,
    show: false,
    backgroundColor: "#f4f5f6",
    webPreferences: createWebPreferences(preloadPath, options.app.isPackaged),
  });
  window.removeMenu();
  applyNavigationPolicy(window.webContents);
  window.once("ready-to-show", () => {
    if (!window.isDestroyed() && !window.webContents.isDestroyed()) window.show();
  });

  const developmentUrl = resolveDevelopmentUrl(options.developmentUrl);
  if (developmentUrl) void window.loadURL(developmentUrl);
  else void window.loadFile(path.join(options.app.getAppPath(), "dist", "renderer", "index.html"));
  return window;
}

export function focusDesktopWindow(window: BrowserWindow | null): boolean {
  if (!window || window.isDestroyed() || window.webContents.isDestroyed()) return false;
  if (window.isMinimized()) window.restore();
  window.focus();
  return true;
}
