import { packager } from "@electron/packager";
import path from "node:path";
import { fileURLToPath } from "node:url";

const desktopRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const electronZipDir = process.env.CAICLI_ELECTRON_ZIP_DIR;

const appPaths = await packager({
  dir: desktopRoot,
  out: path.join(desktopRoot, "out"),
  overwrite: true,
  platform: "win32",
  arch: "x64",
  name: "C-AICLI Desktop",
  executableName: "caicli-desktop",
  appVersion: "0.6.0",
  ...(electronZipDir ? { electronZipDir } : {}),
  asar: true,
  prune: true,
  extraResource: [path.join(desktopRoot, "resources", "apphost")],
  ignore: [
    /^\/\.electron-cache($|\/)/,
    /^\/out($|\/)/,
    /^\/node_modules($|\/)/,
    /^\/resources($|\/)/,
    /^\/dist\/.*\.map$/,
    /^\/e2e($|\/)/,
    /^\/playwright-report($|\/)/,
    /^\/test-results($|\/)/,
    /^\/playwright\.config\.ts$/,
    /^\/scripts($|\/)/,
    /^\/src($|\/)/,
    /^\/eslint\.config\.js$/,
    /^\/tsconfig\.json$/,
    /^\/vite\.config\.ts$/,
  ],
});

for (const appPath of appPaths) {
  process.stdout.write(`${appPath}\n`);
}
