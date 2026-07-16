import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const desktopRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const html = await readFile(path.join(desktopRoot, "dist", "renderer", "index.html"), "utf8");

const required = [
  "default-src 'self'",
  "script-src 'self'",
  "style-src 'self'",
  "connect-src 'self'",
  "object-src 'none'",
  "base-uri 'none'",
  "frame-ancestors 'none'",
];
for (const directive of required) {
  if (!html.includes(directive)) throw new Error(`Production CSP is missing: ${directive}`);
}

const forbidden = ["http://", "https://", "ws://", "wss://", "'unsafe-eval'", "'unsafe-inline'"];
for (const value of forbidden) {
  if (html.includes(value)) throw new Error(`Production CSP contains forbidden value: ${value}`);
}
