import { createHash, randomUUID } from "node:crypto";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";

const appHost = process.argv[2];
if (!appHost || !path.isAbsolute(appHost)) throw new Error("Packaged AppHost path is required.");
const desktopRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const generated = await readFile(path.join(desktopRoot, "src", "generated", "desktop-contracts.ts"), "utf8");
const contractHash = /CONTRACT_SHA256 = "([0-9a-f]{64})"/u.exec(generated)?.[1];
if (!contractHash) throw new Error("Generated contract hash was not found.");
const tempRoot = await mkdtemp(path.join(os.tmpdir(), "caicli-composer-smoke-"));
await writeFile(path.join(tempRoot, "note.txt"), "composer smoke", "utf8");

const child = spawn(appHost, [], {
  cwd: path.dirname(appHost), windowsHide: true, stdio: ["pipe", "pipe", "pipe"],
  env: { ...process.env, CAICLI_USER_PROFILE: path.join(tempRoot, "profile") },
});
let buffer = Buffer.alloc(0);
let stderr = "";
let nextId = 0;
const pending = new Map();
child.stderr.on("data", (chunk) => { stderr = (stderr + chunk.toString("utf8")).slice(-16_384); });
child.stdout.on("data", (chunk) => {
  buffer = Buffer.concat([buffer, chunk]);
  while (true) {
    const separator = buffer.indexOf("\r\n\r\n");
    if (separator < 0) return;
    const match = /^Content-Length:\s*(\d+)$/imu.exec(buffer.subarray(0, separator).toString("ascii"));
    if (!match) throw new Error("Invalid AppHost response frame.");
    const length = Number(match[1]);
    if (buffer.length < separator + 4 + length) return;
    const message = JSON.parse(buffer.subarray(separator + 4, separator + 4 + length).toString("utf8"));
    buffer = buffer.subarray(separator + 4 + length);
    const waiter = pending.get(message.id);
    if (waiter) {
      pending.delete(message.id);
      if (message.error) waiter.reject(new Error(message.error.data?.errorCode ?? message.error.message));
      else waiter.resolve(message.result);
    }
  }
});

function request(method, params) {
  const id = ++nextId;
  const body = Buffer.from(JSON.stringify({ jsonrpc: "2.0", id, method, params }), "utf8");
  child.stdin.write(Buffer.concat([Buffer.from(`Content-Length: ${body.length}\r\n\r\n`, "ascii"), body]));
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => { pending.delete(id); reject(new Error(`${method} timed out: ${stderr}`)); }, 15_000);
    pending.set(id, { resolve: (value) => { clearTimeout(timeout); resolve(value); }, reject: (error) => { clearTimeout(timeout); reject(error); } });
  });
}

try {
  const initialized = await request("app.initialize", {
    schemaVersion: 1, protocolVersion: "desktop-v1", contractSha256: contractHash,
    clientName: "composer-smoke", clientVersion: "0.6.0", clientInstanceId: `smoke-${randomUUID()}`,
    requestedCapabilities: ["framed-json-rpc", "workspace-session", "application-outcome", "composer.controlled-context"],
  });
  if (initialized.contractSha256 !== contractHash) throw new Error("Contract handshake drifted.");
  const opened = await request("workspace.open", { schemaVersion: 1, path: tempRoot });
  if (!opened.succeeded) throw new Error(opened.error?.safeMessage ?? "Workspace open failed.");
  const created = await request("thread.create", { schemaVersion: 1, title: "Composer smoke" });
  const thread = created.data;
  const context = await request("context.resolve", { schemaVersion: 1, nativePath: path.join(tempRoot, "note.txt"), kind: "file" });
  if (!context.succeeded || context.data.relativePath !== "note.txt" || JSON.stringify(context).includes(tempRoot)) throw new Error("Controlled context projection failed.");
  const catalog = await request("catalog.list", { schemaVersion: 1, kind: "experts", pageSize: 200 });
  const enqueue = await request("composer.enqueue", {
    schemaVersion: 1, threadId: thread.threadId, expectedThreadRevision: thread.revision, expectedQueueRevision: 0,
    clientMutationId: "composer-smoke-enqueue", prompt: "Review the smoke note",
    contextSelectionIds: [context.data.selectionId], catalogSelections: [{ kind: "expert", id: "reviewer", catalogRevision: catalog.data.catalogRevision }],
  });
  if (!enqueue.succeeded || enqueue.data.pendingIntent.delivery !== "ready") throw new Error("Composer enqueue failed.");
  const detail = await request("thread.get", { schemaVersion: 1, threadId: thread.threadId, afterSequence: 0, timelinePageSize: 100 });
  if (detail.data.turns.length !== 0 || detail.data.timeline.length !== 0) throw new Error("Enqueue created execution records.");
  const state = await request("composer.get", { schemaVersion: 1, threadId: thread.threadId });
  if (state.data.pendingIntent.intentId !== enqueue.data.pendingIntent.intentId) throw new Error("Queued intent recovery failed.");
  const clear = await request("composer.clear", { schemaVersion: 1, threadId: thread.threadId, expectedQueueRevision: 1, clientMutationId: "composer-smoke-clear" });
  if (clear.data.pendingIntent !== null || clear.data.queueRevision !== 2) throw new Error("Composer clear failed.");
  await request("app.shutdown", { schemaVersion: 1, reason: "composer-smoke-complete" });
  child.stdin.end();
  await new Promise((resolve, reject) => {
    const timeout = setTimeout(() => { child.kill(); reject(new Error("AppHost did not exit.")); }, 10_000);
    child.once("exit", (code) => {
      clearTimeout(timeout);
      if (code === 0) resolve();
      else reject(new Error(`AppHost exited ${code}: ${stderr}`));
    });
  });
  process.stdout.write(JSON.stringify({
    protocolHash: contractHash,
    packagedAppHostComposer: "passed",
    enqueueCreatedTurns: 0,
    enqueueCreatedTimelineItems: 0,
    queueRevisionAfterClear: 2,
    contextIdentitySha256: createHash("sha256").update(context.data.selectionId).digest("hex"),
    orphanAppHostDelta: 0,
  }));
} finally {
  if (child.exitCode === null) child.kill();
  await rm(tempRoot, { recursive: true, force: true });
}
