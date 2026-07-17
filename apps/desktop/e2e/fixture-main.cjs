const { app, BrowserWindow } = require("electron");
const path = require("node:path");
const os = require("node:os");

app.disableHardwareAcceleration();
app.setPath("userData", path.join(os.tmpdir(), `caicli-e2e-${process.pid}`));

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
