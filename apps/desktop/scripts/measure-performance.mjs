import { spawnSync } from "node:child_process";
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const scriptPath = fileURLToPath(import.meta.url);
const desktopRoot = path.resolve(path.dirname(scriptPath), "..");
const repoRoot = path.resolve(desktopRoot, "..", "..");

export function validateProfiles(profiles) {
  if (profiles.length !== 5) return { passed: false, reason: `Expected 5 profiles, found ${profiles.length}.` };
  for (const [index, profile] of profiles.entries()) {
    if (profile.schemaVersion !== 2 || profile.workload !== "week77-long-session-v2") return { passed: false, reason: `Profile ${index + 1} has an unsupported schema or workload.` };
    if (profile.status !== "Passed") return { passed: false, reason: `Profile ${index + 1} did not pass.` };
    if (profile.retention?.idleWorkingSetPercent > 15 || profile.retention?.idlePrivateBytesPercent > 15) return { passed: false, reason: `Profile ${index + 1} exceeded the 15% idle retention gate.` };
    if (profile.cleanup?.processDelta !== 0 || profile.cleanup?.tempDelta !== 0) return { passed: false, reason: `Profile ${index + 1} did not clean up owned state.` };
  }
  return { passed: true, reason: null };
}

export function percentChange(baseline, value) {
  return ((value - baseline) / baseline) * 100;
}

function run(command, args, options = {}) {
  return spawnSync(command, args, { cwd: desktopRoot, encoding: "utf8", stdio: options.capture ? "pipe" : "inherit", env: { ...process.env, ...options.env } });
}

function git(...args) {
  const result = run("git", ["-C", repoRoot, ...args], { capture: true });
  if (result.status !== 0) throw new Error(result.stderr || result.stdout || `git ${args.join(" ")} failed.`);
  return result.stdout.trim();
}

function sha256(filePath) {
  return crypto.createHash("sha256").update(fs.readFileSync(filePath)).digest("hex").toUpperCase();
}

function packageInventory(packageRoot) {
  let bytes = 0;
  let fileCount = 0;
  const visit = (directory) => {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      const resolved = path.join(directory, entry.name);
      if (entry.isDirectory()) visit(resolved);
      else if (entry.isFile()) { fileCount++; bytes += fs.statSync(resolved).size; }
    }
  };
  visit(packageRoot);
  return { fileCount, bytes };
}

function main() {
  const artifactsRoot = path.join(repoRoot, "artifacts");
  const outputPath = path.resolve(process.env.CAICLI_PERFORMANCE_OUTPUT ?? path.join(artifactsRoot, "desktop-performance", "week77-performance.json"));
  if (!outputPath.toLowerCase().startsWith(`${artifactsRoot.toLowerCase()}${path.sep}`)) throw new Error("Performance evidence must remain under artifacts.");
  const runRoot = fs.mkdtempSync(path.join(artifactsRoot, ".week77-performance-"));
  const profileRoot = path.join(runRoot, "profiles");
  const packageBaselinePath = path.join(runRoot, "package-baseline.json");
  fs.mkdirSync(profileRoot, { recursive: true });

  const packageRoot = path.join(desktopRoot, "out", "C-AICLI Desktop-win32-x64");
  const appHostPath = path.join(packageRoot, "resources", "apphost", "CSharpAiCli.AppHost.exe");
  const asarPath = path.join(packageRoot, "resources", "app.asar");
  const desktopExePath = path.join(packageRoot, "caicli-desktop.exe");
  for (const required of [appHostPath, asarPath, desktopExePath]) if (!fs.existsSync(required)) throw new Error(`Packaged Desktop payload is missing: ${required}`);

  let packageBaselineExit = null;
  let playwrightExit = null;
  let packageBaseline = null;
  let profiles = [];
  let unexpectedFailure = null;
  try {
    const baseline = run("powershell", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", path.join(repoRoot, "tools", "Measure-DesktopBaseline.ps1"), "-PackageRoot", packageRoot, "-OutputPath", packageBaselinePath], { capture: true });
    packageBaselineExit = baseline.status;
    if (fs.existsSync(packageBaselinePath)) packageBaseline = JSON.parse(fs.readFileSync(packageBaselinePath, "utf8").replace(/^\uFEFF/, ""));

    const playwrightCli = path.join(desktopRoot, "node_modules", "@playwright", "test", "cli.js");
    const measured = run(process.execPath, [playwrightCli, "test", "long-session.spec.ts", "--project=unpacked", "--repeat-each=5", "--workers=1", "--retries=0"], { env: { CAICLI_WEEK77_EVIDENCE_DIR: profileRoot } });
    playwrightExit = measured.status;
    profiles = fs.readdirSync(profileRoot).filter((name) => /^long-session-\d+\.json$/.test(name)).sort().map((name) => JSON.parse(fs.readFileSync(path.join(profileRoot, name), "utf8")));
  } catch (error) {
    unexpectedFailure = error instanceof Error ? error.message : String(error);
  }

  const profileGate = validateProfiles(profiles);
  const revision = git("rev-parse", "--verify", "HEAD").toLowerCase();
  const sourceDirty = git("status", "--porcelain=v1", "--untracked-files=all").length > 0;
  const inventory = packageInventory(packageRoot);
  const packageGrowthPercent = percentChange(437851431, inventory.bytes);
  const asarBytes = fs.statSync(asarPath).size;
  const asarGrowthPercent = percentChange(2175389, asarBytes);
  const appHostBytes = fs.statSync(appHostPath).size;
  const appHostGrowthPercent = percentChange(79563784, appHostBytes);
  const packageGatePassed = packageBaselineExit === 0 && packageGrowthPercent <= 15 && asarGrowthPercent <= 15 && appHostGrowthPercent <= 15;
  const gatePassed = unexpectedFailure === null && playwrightExit === 0 && profileGate.passed && packageGatePassed;
  const status = gatePassed ? (sourceDirty ? "Measured" : "Passed") : "Failed";
  const result = {
    schemaVersion: 2,
    status,
    workload: "week77-performance-gate-v2",
    sourceRevision: revision,
    appHostSha256: sha256(appHostPath),
    source: { revision, dirty: sourceDirty },
    settings: { consecutiveIndependentProfiles: 5, workers: 1, retries: 0, idleRetentionLimitPercent: 15 },
    package: {
      ...inventory,
      growthPercent: packageGrowthPercent,
      appAsarBytes: asarBytes,
      appAsarGrowthPercent: asarGrowthPercent,
      appHostBytes,
      appHostGrowthPercent,
      desktopSha256: sha256(desktopExePath),
      appHostSha256: sha256(appHostPath),
    },
    packageBaseline,
    profiles,
    gates: { packageBaselineExit, playwrightExit, packageGatePassed, profileGatePassed: profileGate.passed, profileGateReason: profileGate.reason },
    failure: unexpectedFailure,
  };
  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.writeFileSync(outputPath, `${JSON.stringify(result, null, 2)}\n`, "utf8");
  fs.rmSync(runRoot, { recursive: true, force: true });
  process.stdout.write(`${JSON.stringify({ outputPath, status, sourceRevision: revision, sourceDirty, profileCount: profiles.length, packageGatePassed, profileGatePassed: profileGate.passed, profileRetention: profiles.map((profile) => ({ profile: profile.profile, idleWorkingSetPercent: profile.retention?.idleWorkingSetPercent, idlePrivateBytesPercent: profile.retention?.idlePrivateBytesPercent, processDelta: profile.cleanup?.processDelta, tempDelta: profile.cleanup?.tempDelta })) }, null, 2)}\n`);
  if (!gatePassed) process.exitCode = 1;
}

if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) main();
