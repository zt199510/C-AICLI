import { extractFile, listPackage, statFile } from "@electron/asar";
import { lstat, readdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const desktopRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

export async function auditDesktopPackage(packageRoot) {
  const root = path.resolve(packageRoot);
  const packageFiles = await walkPackage(root, root);
  const relativeFiles = packageFiles.map((file) => path.relative(root, file).replaceAll("\\", "/")).sort();
  const required = [
    "caicli-desktop.exe",
    "LICENSE",
    "LICENSES.chromium.html",
    "resources/app.asar",
    "resources/apphost/CSharpAiCli.AppHost.exe",
    "resources/apphost/THIRD-PARTY-NOTICES-MAGICK.NET.txt",
  ];
  for (const item of required) {
    if (!relativeFiles.includes(item)) throw new Error(`Packaged payload is missing: ${item}`);
  }
  const forbiddenPackage = [
    /(^|\/)\.git(\/|$)/i,
    /(^|\/)node_modules(\/|$)/i,
    /(^|\/)(?:e2e|test-results|playwright-report|scripts|src)(\/|$)/i,
    /(?:^|\/)(?:\.env(?:\..*)?|.*\.pdb|.*\.map|.*\.ts|.*\.tsx|.*\.log)$/i,
    /(?:^|\/)(?:playwright|vite|eslint)\.[^/]+$/i,
  ];
  for (const item of relativeFiles) {
    if (forbiddenPackage.some((pattern) => pattern.test(item))) throw new Error(`Forbidden packaged payload: ${item}`);
  }

  const asarPath = path.join(root, "resources", "app.asar");
  const entries = listPackage(asarPath, { isPack: false }).map(normalizeAsarPath).sort();
  const files = entries.filter((entry) => statFile(asarPath, nativeAsarPath(entry), false).files === undefined);
  for (const entry of entries) {
    const stat = statFile(asarPath, nativeAsarPath(entry), false);
    if ("link" in stat) throw new Error(`Packaged app.asar contains a link: ${entry}`);
    if (!isAllowedAsarPath(entry)) throw new Error(`Unexpected app.asar payload: ${entry}`);
    if (/(^|\/)(?:e2e|test-results|playwright-report|scripts|src|node_modules)(\/|$)|\.(?:map|ts|tsx|pdb|log)$/i.test(entry)) {
      throw new Error(`Forbidden app.asar payload: ${entry}`);
    }
  }

  const html = extractFile(asarPath, nativeAsarPath("dist/renderer/index.html")).toString("utf8");
  const preload = extractFile(asarPath, nativeAsarPath("dist/preload/index.cjs")).toString("utf8");
  const renderer = files.filter((entry) => /^dist\/renderer\/.*\.js$/i.test(entry))
    .map((entry) => extractFile(asarPath, nativeAsarPath(entry)).toString("utf8")).join("\n");
  assertProductionContent(html, preload, renderer);

  return Object.freeze({
    schemaVersion: 1,
    packageFileCount: relativeFiles.length,
    asarEntryCount: entries.length,
    reviewedInvokeChannels: 48,
    reviewedEventChannels: 2,
    requiredPayload: required,
    forbiddenPayloadCount: 0,
  });
}

function assertProductionContent(html, preload, renderer) {
  for (const directive of ["default-src 'self'", "script-src 'self'", "style-src 'self'", "connect-src 'self'", "object-src 'none'", "base-uri 'none'", "form-action 'none'", "frame-ancestors 'none'"]) {
    if (!html.includes(directive)) throw new Error(`Packaged CSP is missing: ${directive}`);
  }
  for (const value of ["http://", "https://", "ws://", "wss://", "localhost", "'unsafe-eval'", "'unsafe-inline'"]) {
    if (html.includes(value)) throw new Error(`Packaged HTML contains forbidden content: ${value}`);
  }
  for (const value of ["ipcRenderer", "shell.openExternal", "child_process", "node:fs", "desktop:request", "query(method", "localStorage", "indexedDB"]) {
    if (renderer.includes(value)) throw new Error(`Packaged Renderer contains forbidden surface: ${value}`);
  }
  for (const value of ["desktop:initialize", "desktop:request", "shell.openExternal", "node:fs", "child_process", "thread:delete", "artifact:bytes"]) {
    if (preload.includes(value)) throw new Error(`Packaged Preload contains forbidden surface: ${value}`);
  }
  const reviewedChannels = [
    "runtime:get-status", "runtime:restart", "runtime:status", "workspace:open", "workspace:get-snapshot",
    "thread:list", "thread:get", "thread:create", "thread:rename", "thread:archive", "thread:changed",
    "changes:get", "report:list", "report:get", "artifact:list", "artifact:get",
    "terminal:open", "terminal:input", "terminal:resize", "terminal:cancel", "terminal:close", "terminal:get",
    "artifact:preview", "artifact:export", "artifact:verify", "gerber:review:get", "gerber:preview", "gerber:accept", "gerber:reject",
    "catalog:list", "context:search", "context:pick-file", "context:pick-folder", "composer:get", "composer:enqueue", "composer:clear",
    "turn:start", "turn:cancel", "approval:resolve", "turn:resume", "turn:restart",
  ];
  if (new Set(reviewedChannels).size !== 41) throw new Error("Reviewed bridge inventory is inconsistent.");
  for (const channel of reviewedChannels) {
    if (!preload.includes(channel)) throw new Error(`Packaged Preload is missing reviewed channel: ${channel}`);
  }
}

function normalizeAsarPath(value) {
  return value.replaceAll("\\", "/").replace(/^\/+/, "");
}

function nativeAsarPath(value) {
  return value.replaceAll("/", path.sep);
}

function isAllowedAsarPath(value) {
  return value === "README.md" || value === "THIRD_PARTY_NOTICES.md" || value === "package.json" || value === "dist" || value.startsWith("dist/");
}

async function walkPackage(root, current) {
  const result = [];
  for (const entry of await readdir(current, { withFileTypes: true })) {
    const target = path.resolve(current, entry.name);
    if (target !== root && !target.startsWith(`${root}${path.sep}`)) throw new Error("Package path escaped its root.");
    const metadata = await lstat(target);
    if (metadata.isSymbolicLink()) throw new Error(`Packaged payload contains a link: ${path.relative(root, target)}`);
    if (metadata.isDirectory()) result.push(...await walkPackage(root, target));
    else if (metadata.isFile()) result.push(target);
    else throw new Error(`Packaged payload contains a non-regular entry: ${path.relative(root, target)}`);
  }
  return result;
}

if (path.resolve(process.argv[1] ?? "") === fileURLToPath(import.meta.url)) {
  const rootIndex = process.argv.indexOf("--package-root");
  const packageRoot = rootIndex >= 0 ? process.argv[rootIndex + 1] : path.join(desktopRoot, "out", "C-AICLI Desktop-win32-x64");
  if (!packageRoot) throw new Error("--package-root requires a value.");
  process.stdout.write(`${JSON.stringify(await auditDesktopPackage(packageRoot), null, 2)}\n`);
}
