import { spawnSync } from "node:child_process";
import crypto from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const scriptPath = fileURLToPath(import.meta.url);
const desktopRoot = path.resolve(path.dirname(scriptPath), "..");
const repoRoot = path.resolve(desktopRoot, "..", "..");
const artifactsRoot = path.join(repoRoot, "artifacts");

export function evaluateRun(kind, exitCode, stats, cleanup) {
  const expectedCount = kind === "accessibility" ? 2 : 8;
  const testCount = Number(stats?.expected ?? 0);
  const failures = Number(stats?.unexpected ?? 0) + Number(stats?.flaky ?? 0);
  const passed = exitCode === 0 && testCount === expectedCount && failures === 0 && Number(stats?.skipped ?? 0) === 0 && cleanup.processDelta === 0 && cleanup.tempDelta === 0;
  return { passed, expectedCount, testCount, failures };
}

function parseArguments(argv) {
  const values = new Map();
  for (let index = 0; index < argv.length; index++) {
    const name = argv[index];
    if (!name?.startsWith("--")) throw new Error(`Unexpected argument: ${name}`);
    const value = argv[++index];
    if (!value || value.startsWith("--")) throw new Error(`Missing value for ${name}.`);
    values.set(name, value);
  }
  const kind = values.get("--kind") ?? "smoke";
  if (kind !== "smoke" && kind !== "accessibility") throw new Error("--kind must be smoke or accessibility.");
  return { kind, packageRoot: values.get("--package-root"), outputPath: values.get("--output") };
}

function pathWithin(root, candidate) {
  const relative = path.relative(path.resolve(root), path.resolve(candidate));
  return relative === "" || (!relative.startsWith(`..${path.sep}`) && relative !== ".." && !path.isAbsolute(relative));
}

function run(command, args, options = {}) {
  return spawnSync(command, args, { cwd: desktopRoot, encoding: "utf8", stdio: options.capture ? "pipe" : "inherit", windowsHide: true, env: { ...process.env, ...options.env } });
}

function git(...args) {
  const result = run("git", ["-C", repoRoot, ...args], { capture: true });
  if (result.status !== 0) throw new Error(`git ${args.join(" ")} failed.`);
  return result.stdout.trim();
}

function sha256(filePath) {
  return crypto.createHash("sha256").update(fs.readFileSync(filePath)).digest("hex").toUpperCase();
}

function inventory(root) {
  const values = [];
  const visit = (directory) => {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true }).sort((left, right) => left.name.localeCompare(right.name))) {
      const resolved = path.join(directory, entry.name);
      if (entry.isSymbolicLink()) throw new Error("Desktop evidence refuses package reparse/symlink entries.");
      if (entry.isDirectory()) visit(resolved);
      else if (entry.isFile()) values.push({ path: path.relative(root, resolved).replaceAll(path.sep, "/"), size: fs.statSync(resolved).size, sha256: sha256(resolved) });
    }
  };
  visit(root);
  return values;
}

function inventorySha256(values) {
  const body = values.map((value) => `${value.path}\0${value.size}\0${value.sha256}\n`).join("");
  return crypto.createHash("sha256").update(body).digest("hex").toUpperCase();
}

function processCounts() {
  if (process.platform !== "win32") return { desktop: 0, appHost: 0 };
  const command = "$d=@(Get-Process -Name 'caicli-desktop' -ErrorAction SilentlyContinue).Count;$a=@(Get-Process -Name 'CSharpAiCli.AppHost' -ErrorAction SilentlyContinue).Count;@{desktop=$d;appHost=$a}|ConvertTo-Json -Compress";
  const result = run("powershell.exe", ["-NoProfile", "-Command", command], { capture: true });
  if (result.status !== 0) throw new Error("Could not capture Desktop process inventory.");
  return JSON.parse(result.stdout);
}

function tempInventory() {
  return new Set(fs.readdirSync(os.tmpdir()).filter((name) => name.startsWith("caicli-week75-")));
}

function findCandidateRoot(packageRoot) {
  let current = path.resolve(packageRoot);
  while (pathWithin(artifactsRoot, current) && current !== artifactsRoot) {
    if (fs.existsSync(path.join(current, "release-manifest.json"))) return current;
    current = path.dirname(current);
  }
  return null;
}

function main() {
  const args = parseArguments(process.argv.slice(2));
  const defaultPackage = path.join(desktopRoot, "out", "C-AICLI Desktop-win32-x64");
  const packageRoot = path.resolve(args.packageRoot ?? defaultPackage);
  if (!pathWithin(path.join(desktopRoot, "out"), packageRoot) && !pathWithin(artifactsRoot, packageRoot)) throw new Error("Desktop evidence package must remain under apps/desktop/out or artifacts.");
  const outputPath = path.resolve(args.outputPath ?? path.join(artifactsRoot, "desktop-acceptance", args.kind === "accessibility" ? "week77-accessibility-automation.json" : "week77-packaged-smoke.json"));
  if (!pathWithin(artifactsRoot, outputPath) || outputPath === artifactsRoot) throw new Error("Desktop evidence output must remain under artifacts.");

  const desktopExe = path.join(packageRoot, "caicli-desktop.exe");
  const appHost = path.join(packageRoot, "resources", "apphost", "CSharpAiCli.AppHost.exe");
  for (const required of [desktopExe, appHost, path.join(packageRoot, "resources", "app.asar")]) if (!fs.existsSync(required)) throw new Error(`Desktop evidence payload is missing: ${required}`);

  const candidateRoot = findCandidateRoot(packageRoot);
  const candidateManifest = candidateRoot ? JSON.parse(fs.readFileSync(path.join(candidateRoot, "release-manifest.json"), "utf8").replace(/^\uFEFF/, "")) : null;
  const revision = git("rev-parse", "--verify", "HEAD").toLowerCase();
  const sourceDirty = git("status", "--porcelain=v1", "--untracked-files=all").length > 0;
  if (candidateManifest?.sourceRevision && candidateManifest.sourceRevision !== revision) throw new Error("Candidate source revision does not match the checked-out revision.");

  const runRoot = fs.mkdtempSync(path.join(artifactsRoot, `.week77-${args.kind}-`));
  const reporterPath = path.join(runRoot, "playwright-report.json");
  const beforeProcesses = processCounts();
  const beforeTemp = tempInventory();
  const playwrightCli = path.join(desktopRoot, "node_modules", "@playwright", "test", "cli.js");
  const testArgs = args.kind === "accessibility"
    ? [playwrightCli, "test", "week76-hardening.spec.ts", "--project=unpacked", "--project=packaged", "--workers=1", "--retries=0", "--reporter=json"]
    : [playwrightCli, "test", "--project=packaged", "--workers=1", "--retries=0", "--reporter=json"];
  const startedAtUtc = new Date().toISOString();
  const execution = run(process.execPath, testArgs, { env: { CAICLI_DESKTOP_PACKAGE_ROOT: packageRoot, PLAYWRIGHT_JSON_OUTPUT_FILE: reporterPath } });
  const completedAtUtc = new Date().toISOString();
  const afterProcesses = processCounts();
  const afterTemp = tempInventory();
  const report = fs.existsSync(reporterPath) ? JSON.parse(fs.readFileSync(reporterPath, "utf8")) : { stats: {} };
  const cleanup = {
    processDelta: (afterProcesses.desktop - beforeProcesses.desktop) + (afterProcesses.appHost - beforeProcesses.appHost),
    desktopProcessDelta: afterProcesses.desktop - beforeProcesses.desktop,
    appHostProcessDelta: afterProcesses.appHost - beforeProcesses.appHost,
    tempDelta: [...afterTemp].filter((name) => !beforeTemp.has(name)).length,
  };
  const gate = evaluateRun(args.kind, execution.status, report.stats, cleanup);
  const payloadInventory = inventory(packageRoot);
  const candidateArchive = candidateRoot ? fs.readdirSync(candidateRoot).find((name) => /^0\.6\.0-rc\.\d+-windows-x64\.zip$/.test(name)) : null;
  const testsPassed = gate.passed;
  const status = testsPassed ? (sourceDirty ? "Measured" : "Passed") : "Failed";
  const result = {
    schemaVersion: 2,
    type: args.kind === "accessibility" ? "week77-accessibility-automation-v1" : "week77-packaged-smoke-v1",
    status,
    sourceRevision: revision,
    appHostSha256: sha256(appHost),
    packageSha256: candidateArchive ? sha256(path.join(candidateRoot, candidateArchive)) : inventorySha256(payloadInventory),
    candidateId: candidateManifest?.candidateId ?? null,
    candidateArchive: candidateArchive ?? null,
    source: { revision, dirty: sourceDirty },
    run: { startedAtUtc, completedAtUtc, workers: 1, retries: 0, project: args.kind === "accessibility" ? "unpacked+packaged hardening" : "packaged" },
    results: { expectedCount: gate.expectedCount, passedCount: gate.testCount, failedCount: gate.failures, skippedCount: Number(report.stats?.skipped ?? 0), exitCode: execution.status },
    cleanup,
    payload: { fileCount: payloadInventory.length, bytes: payloadInventory.reduce((sum, value) => sum + value.size, 0), inventorySha256: inventorySha256(payloadInventory) },
  };
  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.writeFileSync(outputPath, `${JSON.stringify(result, null, 2)}\n`, "utf8");
  fs.rmSync(runRoot, { recursive: true, force: true });
  process.stdout.write(`${JSON.stringify({ outputPath, status, sourceRevision: revision, sourceDirty, results: result.results, cleanup, packageSha256: result.packageSha256, appHostSha256: result.appHostSha256 }, null, 2)}\n`);
  if (!testsPassed) process.exitCode = 1;
}

if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) main();
