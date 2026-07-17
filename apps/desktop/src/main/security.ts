import type { App, Session, WebContents, WebPreferences } from "electron";

export const EXACT_DEVELOPMENT_URL = "http://127.0.0.1:5173/";

export function createWebPreferences(preloadPath: string, appIsPackaged = true): WebPreferences {
  return Object.freeze({
    preload: preloadPath,
    contextIsolation: true,
    nodeIntegration: false,
    sandbox: true,
    webSecurity: true,
    allowRunningInsecureContent: false,
    webviewTag: false,
    navigateOnDragDrop: false,
    devTools: !appIsPackaged,
    spellcheck: false,
  });
}

export function resolveDevelopmentUrl(value: string | undefined): string | null {
  if (value === undefined || value === "") return null;
  if (value !== EXACT_DEVELOPMENT_URL) throw new Error("Invalid development renderer URL.");
  return value;
}

export function applyNavigationPolicy(webContents: WebContents): void {
  webContents.setWindowOpenHandler(() => ({ action: "deny" }));
  webContents.on("will-navigate", (event, targetUrl) => {
    if (targetUrl !== webContents.getURL()) event.preventDefault();
  });
  webContents.on("will-attach-webview", (event) => event.preventDefault());
}

export function installSessionPolicy(session: Session, app: App): void {
  session.setPermissionRequestHandler((_webContents, _permission, callback) => callback(false));
  session.setPermissionCheckHandler(() => false);
  session.on("will-download", (event, item) => {
    event.preventDefault();
    item.cancel();
  });
  app.on("web-contents-created", (_event, contents) => applyNavigationPolicy(contents));
}
