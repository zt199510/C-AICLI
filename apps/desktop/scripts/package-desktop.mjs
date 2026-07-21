import { packager } from "@electron/packager";
import { readdir, readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const desktopRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const electronVersion = JSON.parse(await readFile(path.join(desktopRoot, "node_modules", "electron", "package.json"), "utf8")).version;
const electronArchiveName = `electron-v${electronVersion}-win32-x64.zip`;
const electronZipDir = process.env.CAICLI_ELECTRON_ZIP_DIR ?? await findArchiveDirectory(path.join(desktopRoot, ".electron-cache"), electronArchiveName);

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
    /^\/README\.md$/,
    /^\/index\.html$/,
    /^\/eslint\.config\.js$/,
    /^\/tsconfig\.json$/,
    /^\/vite\.config\.ts$/,
  ],
});

for (const appPath of appPaths) {
  process.stdout.write(`${appPath}\n`);
}

async function findArchiveDirectory(root, fileName) {
  try {
    for (const entry of await readdir(root, { withFileTypes: true })) {
      const target = path.join(root, entry.name);
      if (entry.isDirectory()) {
        const nested = await findArchiveDirectory(target, fileName);
        if (nested) return nested;
      } else if (entry.isFile() && entry.name === fileName) return root;
    }
  } catch (error) {
    if (error?.code !== "ENOENT") throw error;
  }
  return undefined;
}
