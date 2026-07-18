const { app, BrowserWindow } = require("electron");
const path = require("node:path");
const os = require("node:os");
const { ipcMain } = require("electron");

let pendingIntent = null;
let queueRevision = 0;
const composerState = () => ({
  workspaceId: "fixture-workspace", threadId: "fixture-thread", threadRevision: 3, queueRevision, pendingIntent,
  effectiveModel: "gpt-fixture", modelSource: "fixture", approvalMode: "OnRequest", approvalModeSource: "fixture", controlledContext: true,
});
const ok = (data) => ({ schemaVersion: 1, succeeded: true, data, error: null, diagnostics: [], truncated: false });
ipcMain.handle("fixture:composer-get", () => ok(composerState()));
ipcMain.handle("fixture:composer-enqueue", (_event, command) => {
  if (pendingIntent || command.expectedQueueRevision !== queueRevision) return { schemaVersion: 1, succeeded: false, data: null, error: { code: "composer-conflict", category: "conflict", safeMessage: "Queue changed.", retryable: false }, diagnostics: [], truncated: false };
  queueRevision++;
  pendingIntent = { intentId: `intent-fixture-${queueRevision}`, delivery: "ready", createdAtUtc: "2026-07-17T00:00:00.000Z", contextCount: command.contextSelectionIds.length, catalogCount: command.catalogSelections.length };
  return ok(composerState());
});
ipcMain.handle("fixture:composer-clear", () => { queueRevision++; pendingIntent = null; return ok(composerState()); });

app.disableHardwareAcceleration();
const ownedRoot = process.env.CAICLI_E2E_ROOT;
app.setPath("userData", ownedRoot ? path.join(ownedRoot, "user-data") : path.join(os.tmpdir(), `caicli-e2e-${process.pid}`));

app.whenReady().then(() => {
  const window = new BrowserWindow({
    width: 1440,
    height: 900,
    show: true,
    webPreferences: {
      preload: path.join(__dirname, "fixture-preload.cjs"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false,
      webSecurity: true,
    },
  });
  window.loadFile(path.join(__dirname, "..", "dist", "renderer", "index.html"));
  window.on("closed", () => app.exit(0));
});

app.on("window-all-closed", () => app.exit(0));
