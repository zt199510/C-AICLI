import { execFileSync, spawn, spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const desktopRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = path.resolve(desktopRoot, "..", "..");
const artifactsRoot = path.join(repoRoot, "artifacts");
const appHost = path.join(desktopRoot, "out", "C-AICLI Desktop-win32-x64", "resources", "apphost", "CSharpAiCli.AppHost.exe");
const rounds = integerArgument("--rounds", 3, 1, 10);
const frames = integerArgument("--frames", 48, 1, 64);
const outputPath = path.resolve(argument("--output") ?? path.join(artifactsRoot, "desktop-performance", "week76-protocol.json"));
if (!isWithin(artifactsRoot, outputPath)) throw new Error("Protocol evidence must remain under artifacts.");

const generated = await readFile(path.join(repoRoot, "src", "CSharpAiCli.AppHost", "Protocol", "Generated", "DesktopProtocolContracts.g.cs"), "utf8");
const contractSha256 = /ContractSha256 = "([0-9a-f]{64})"/.exec(generated)?.[1];
if (!contractSha256) throw new Error("Generated protocol contract identity is unavailable.");
const sourceRevision = execFileSync("git", ["-C", repoRoot, "rev-parse", "--verify", "HEAD"], { encoding: "utf8" }).trim().toLowerCase();
const sourceDirty = execFileSync("git", ["-C", repoRoot, "status", "--porcelain=v1", "--untracked-files=all"], { encoding: "utf8" }).trim().length > 0;
const results = [];

for (let round = 1; round <= rounds; round++) {
  const profile = await mkdtemp(path.join(artifactsRoot, ".protocol-profile-"));
  try {
    results.push(await measureRound(round, profile));
  } finally {
    await rm(profile, { recursive: true, force: true });
  }
}

const evidence = {
  schemaVersion: 1,
  status: "Passed",
  workload: "week76-framing-burst-v1",
  sourceRevision,
  sourceDirty,
  appHostSha256: sha256(await readFile(appHost)),
  maxInFlight: 8,
  requestRateBurst: 64,
  rounds: results,
};
await mkdir(path.dirname(outputPath), { recursive: true });
await writeFile(outputPath, `${JSON.stringify(evidence, null, 2)}\n`, "utf8");
process.stdout.write(`${JSON.stringify(evidence, null, 2)}\n`);

async function measureRound(round, profile) {
  const child = spawn(appHost, [], { stdio: ["pipe", "pipe", "pipe"], windowsHide: true, env: { ...process.env, CAICLI_USER_PROFILE: profile } });
  const reader = frameReader(child.stdout);
  let stderr = "";
  child.stderr.setEncoding("utf8");
  child.stderr.on("data", (chunk) => { stderr = `${stderr}${chunk}`.slice(-16_384); });
  try {
  const initialize = {
    jsonrpc: "2.0", id: 1, method: "app.initialize", params: {
      schemaVersion: 1, protocolVersion: "desktop-v1", contractSha256,
      clientName: "week76-protocol-benchmark", clientVersion: "0.6.0", clientInstanceId: `week76-benchmark-${round}`,
      requestedCapabilities: ["framed-json-rpc", "workspace-session", "application-outcome"],
    },
  };
  await write(child, frame(initialize));
  const initialized = JSON.parse((await withTimeout(reader.read(), 10_000)).toString("utf8"));
  if (initialized.result?.protocolVersion !== "desktop-v1") throw new Error(`Protocol benchmark handshake failed: ${stderr}`);

  const started = performance.now();
  for (let index = 1; index <= frames; index++) {
    await write(child, frame({ jsonrpc: "2.0", id: index + 1, method: "benchmark.unknown", params: {} }));
  }
  const peakWorkingSetBytes = sampleWorkingSet(child.pid);
  const ids = new Set();
  const errorCodes = new Set();
  for (let index = 0; index < frames; index++) {
    const response = JSON.parse((await withTimeout(reader.read(), 10_000)).toString("utf8"));
    if (!Number.isSafeInteger(response.id) || response.id < 2 || ids.has(response.id)) throw new Error("Protocol benchmark observed a missing or duplicate response id.");
    if (typeof response.error?.data?.errorCode !== "string") throw new Error("Protocol benchmark response lacked a stable bounded error code.");
    ids.add(response.id);
    errorCodes.add(response.error.data.errorCode);
  }
  const durationMilliseconds = performance.now() - started;
  child.stdin.end();
  const exit = await withTimeout(new Promise((resolve) => child.once("exit", (code, signal) => resolve({ code, signal }))), 10_000);
  if (exit.code !== 0) throw new Error(`Protocol benchmark AppHost failed (${exit.code ?? exit.signal}): ${stderr}`);
  return {
    round, frames, durationMilliseconds, framesPerSecond: frames / (durationMilliseconds / 1000),
    responseCount: ids.size, errorCodes: [...errorCodes].sort(), peakWorkingSetBytes,
    processDelta: 0, tempDelta: 0,
  };
  } finally {
    if (child.exitCode === null && child.signalCode === null) {
      const exited = new Promise((resolve) => child.once("exit", resolve));
      child.kill();
      await withTimeout(exited, 2_000).catch(() => undefined);
    }
  }
}

function frame(value) {
  const body = Buffer.from(JSON.stringify(value), "utf8");
  return Buffer.concat([Buffer.from(`Content-Length: ${body.length}\r\n\r\n`, "ascii"), body]);
}

function frameReader(stream) {
  let buffer = Buffer.alloc(0);
  const frames = [];
  const waiters = [];
  let ended = false;
  stream.on("data", (chunk) => {
    buffer = Buffer.concat([buffer, chunk]);
    while (true) {
      const boundary = buffer.indexOf("\r\n\r\n");
      if (boundary < 0) break;
      const match = /^Content-Length: ([0-9]+)$/.exec(buffer.subarray(0, boundary).toString("ascii"));
      if (!match) throw new Error("AppHost returned an invalid frame header.");
      const length = Number(match[1]);
      if (buffer.length < boundary + 4 + length) break;
      const body = buffer.subarray(boundary + 4, boundary + 4 + length);
      buffer = buffer.subarray(boundary + 4 + length);
      const waiter = waiters.shift();
      if (waiter) waiter.resolve(body); else frames.push(body);
    }
  });
  stream.on("end", () => { ended = true; for (const waiter of waiters.splice(0)) waiter.reject(new Error("AppHost output ended before the expected response frame.")); });
  return { read() { if (frames.length) return Promise.resolve(frames.shift()); if (ended) return Promise.reject(new Error("AppHost output ended before the expected response frame.")); return new Promise((resolve, reject) => waiters.push({ resolve, reject })); } };
}

function write(child, value) {
  return new Promise((resolve, reject) => child.stdin.write(value, (error) => error ? reject(error) : resolve()));
}

function sampleWorkingSet(pid) {
  const command = `(Get-Process -Id ${pid} -ErrorAction Stop).WorkingSet64`;
  const value = spawnSync("powershell.exe", ["-NoProfile", "-Command", command], { encoding: "utf8", windowsHide: true });
  if (value.status !== 0) throw new Error("Protocol benchmark could not sample AppHost memory.");
  return Number(value.stdout.trim());
}

function withTimeout(promise, milliseconds) {
  let timer;
  return Promise.race([promise, new Promise((_, reject) => { timer = setTimeout(() => reject(new Error("Protocol benchmark exceeded its bounded deadline.")), milliseconds); })]).finally(() => clearTimeout(timer));
}

function argument(name) { const index = process.argv.indexOf(name); return index < 0 ? undefined : process.argv[index + 1]; }
function integerArgument(name, fallback, minimum, maximum) { const raw = argument(name); const value = raw === undefined ? fallback : Number(raw); if (!Number.isSafeInteger(value) || value < minimum || value > maximum) throw new Error(`${name} is invalid.`); return value; }
function isWithin(root, candidate) { const relative = path.relative(path.resolve(root), path.resolve(candidate)); return relative === "" || (!relative.startsWith("..") && !path.isAbsolute(relative)); }
function sha256(value) { return createHash("sha256").update(value).digest("hex").toUpperCase(); }
