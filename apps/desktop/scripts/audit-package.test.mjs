import { createPackage } from "@electron/asar";
import { afterEach, describe, expect, it } from "vitest";
import { mkdtemp, mkdir, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { auditDesktopPackage } from "./audit-package.mjs";

const roots = [];
afterEach(async () => { await Promise.all(roots.splice(0).map((root) => rm(root, { force: true, recursive: true }))); });

describe("packaged Desktop audit", () => {
  it("accepts the reviewed payload and rejects source/test leakage", async () => {
    const root = await fixture();
    await expect(auditDesktopPackage(root)).resolves.toMatchObject({ forbiddenPayloadCount: 0, reviewedInvokeChannels: 48, reviewedEventChannels: 2 });
    await mkdir(path.join(root, "src"));
    await writeFile(path.join(root, "src", "leak.ts"), "secret test hook");
    await expect(auditDesktopPackage(root)).rejects.toThrow(/Forbidden packaged payload/);
  });
});

async function fixture() {
  const root = await mkdtemp(path.join(os.tmpdir(), "caicli-package-audit-"));
  roots.push(root);
  const source = path.join(root, "asar-source");
  await mkdir(path.join(root, "resources", "apphost"), { recursive: true });
  await mkdir(path.join(source, "dist", "renderer", "assets"), { recursive: true });
  await mkdir(path.join(source, "dist", "preload"), { recursive: true });
  await mkdir(path.join(source, "dist", "main"), { recursive: true });
  for (const file of ["caicli-desktop.exe", "LICENSE", "LICENSES.chromium.html"]) await writeFile(path.join(root, file), file);
  await writeFile(path.join(root, "resources", "apphost", "CSharpAiCli.AppHost.exe"), "apphost");
  await writeFile(path.join(root, "resources", "apphost", "THIRD-PARTY-NOTICES-MAGICK.NET.txt"), "notice");
  await writeFile(path.join(source, "README.md"), "runtime");
  await writeFile(path.join(source, "THIRD_PARTY_NOTICES.md"), "notices");
  await writeFile(path.join(source, "package.json"), "{}");
  await writeFile(path.join(source, "dist", "main", "index.js"), "main");
  await writeFile(path.join(source, "dist", "preload", "index.cjs"), [
    "runtime:get-status", "runtime:restart", "runtime:status", "workspace:open", "workspace:get-snapshot",
    "thread:list", "thread:get", "thread:create", "thread:rename", "thread:archive", "thread:changed",
    "changes:get", "report:list", "report:get", "artifact:list", "artifact:get",
    "terminal:open", "terminal:input", "terminal:resize", "terminal:cancel", "terminal:close", "terminal:get",
    "artifact:preview", "artifact:export", "artifact:verify", "gerber:review:get", "gerber:preview", "gerber:accept", "gerber:reject",
    "catalog:list", "context:search", "context:pick-file", "context:pick-folder", "composer:get", "composer:enqueue", "composer:clear",
    "turn:start", "turn:cancel", "approval:resolve", "turn:resume", "turn:restart",
  ].join("\n"));
  await writeFile(path.join(source, "dist", "renderer", "assets", "index.js"), "renderer");
  await writeFile(path.join(source, "dist", "renderer", "index.html"), `<meta http-equiv="Content-Security-Policy" content="default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'">`);
  await createPackage(source, path.join(root, "resources", "app.asar"));
  await rm(source, { force: true, recursive: true });
  return root;
}
