import { readdir, readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const desktopRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const rendererRoot = path.join(desktopRoot, "dist", "renderer");
const html = await readFile(path.join(rendererRoot, "index.html"), "utf8");

const required = [
  "default-src 'self'",
  "script-src 'self'",
  "style-src 'self'",
  "connect-src 'self'",
  "img-src 'self' data:",
  "object-src 'none'",
  "base-uri 'none'",
  "form-action 'none'",
  "frame-ancestors 'none'",
];
for (const directive of required) {
  if (!html.includes(directive)) throw new Error(`Production CSP is missing: ${directive}`);
}

const forbiddenHtml = [
  "http://", "https://", "ws://", "wss://", "localhost", "127.0.0.1",
  "'unsafe-eval'", "'unsafe-inline'",
];
for (const value of forbiddenHtml) {
  if (html.includes(value)) throw new Error(`Production CSP contains forbidden value: ${value}`);
}

const rendererFiles = (await walk(rendererRoot)).filter((file) => file.endsWith(".js"));
const rendererBundle = (await Promise.all(rendererFiles.map((file) => readFile(file, "utf8")))).join("\n");
const forbiddenRenderer = [
  "ipcRenderer", "shell.openExternal", "child_process", "node:fs", "node:process",
  "desktop:initialize", "thread.list", "thread.get", "catalog.list", "changes.get",
  "report.list", "artifact.list", "turn.start",
];
for (const value of forbiddenRenderer) {
  if (rendererBundle.includes(value)) throw new Error(`Renderer bundle contains forbidden surface: ${value}`);
}

const preload = await readFile(path.join(desktopRoot, "dist", "preload", "index.cjs"), "utf8");
const reviewedChannels = ["runtime:get-status", "runtime:restart", "workspace:open", "runtime:status"];
for (const channel of reviewedChannels) {
  if (!preload.includes(channel)) throw new Error(`Preload bundle is missing reviewed channel: ${channel}`);
}
for (const value of ["desktop:initialize", "shell.openExternal", "node:fs", "child_process"]) {
  if (preload.includes(value)) throw new Error(`Preload bundle contains forbidden surface: ${value}`);
}

async function walk(root) {
  const result = [];
  for (const entry of await readdir(root, { withFileTypes: true })) {
    const target = path.join(root, entry.name);
    if (entry.isDirectory()) result.push(...await walk(target));
    else result.push(target);
  }
  return result;
}
