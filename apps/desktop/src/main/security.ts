import type { WebContents, WebPreferences } from "electron";

export function createWebPreferences(preloadPath: string): WebPreferences {
  return Object.freeze({
    preload: preloadPath,
    contextIsolation: true,
    nodeIntegration: false,
    sandbox: true,
    webSecurity: true,
    allowRunningInsecureContent: false,
    spellcheck: false,
  });
}

export function applyNavigationPolicy(webContents: WebContents): void {
  webContents.setWindowOpenHandler(() => ({ action: "deny" }));
  webContents.on("will-navigate", (event, targetUrl) => {
    if (targetUrl !== webContents.getURL()) {
      event.preventDefault();
    }
  });
}
