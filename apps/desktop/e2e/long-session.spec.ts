import { _electron as electron, expect, test, type ElectronApplication } from "@playwright/test";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";

const desktopRoot = path.resolve(import.meta.dirname, "..");
const idleRecoverySeconds = 30;
const sampleIntervalSeconds = 5;
const settledSampleCount = 3;

test("long timeline, bounded diff, and terminal output survive repeated reloads", async ({ browserName }, testInfo) => {
  if (browserName !== "chromium") throw new Error("Electron tests require Chromium.");
  testInfo.setTimeout(150_000);
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "caicli-week77-long-session-"));
  let application: ElectronApplication | null = null;
  const reloadSamples: ProcessSnapshot[] = [];
  let warmBaseline: SamplingWindow | null = null;
  let postWorkloadIdle: SamplingWindow | null = null;
  let retention: RetentionResult | null = null;
  let primaryError: unknown = null;
  let cleanupError: unknown = null;
  let processDelta: number | null = null;
  let tempDelta: number | null = null;

  try {
    application = await electron.launch({
      args: ["--disable-gpu", "--in-process-gpu", path.join(desktopRoot, "e2e", "fixture-main.cjs")],
      env: { ...process.env, CAICLI_E2E_SCENARIO: "long-session", CAICLI_E2E_ROOT: root, APPDATA: path.join(root, "appdata") },
    });
    const page = await application.firstWindow();
    await expect(page.getByText("Fixture review thread")).toBeVisible();
    await page.locator(".thread-select").click({ force: true });
    await expect(page.getByRole("button", { name: "Activity 80" })).toBeVisible();
    await page.getByRole("button", { name: "Load newer items" }).click();
    await expect(page.getByRole("button", { name: "Activity 160" })).toBeVisible();
    await page.getByRole("button", { name: "Load newer items" }).click();
    await expect(page.getByRole("button", { name: "Activity 240" })).toBeVisible();
    expect(await page.locator(".conversation-stream .conversation-message, .conversation-stream .tool-activity-group, .conversation-stream .approval-card").count()).toBeLessThan(100);

    // The first of the five frozen reloads warms the renderer. Both sides of the
    // retention comparison then use the same fixed 30-second sampling window.
    // Working-set retention compares the warm window peak with the settled
    // post-workload median. Windows may trim an otherwise unchanged idle
    // renderer's resident pages, while the beginning of the post-workload
    // window can still contain transiently resident pages.
    await page.reload();
    await page.locator(".thread-select").click({ force: true });
    await expect(page.getByRole("button", { name: "Activity 80" })).toBeVisible();
    await waitForStableRenderer(application);
    reloadSamples.push(await processSnapshot(application));
    warmBaseline = await captureSamplingWindow(application, idleRecoverySeconds, sampleIntervalSeconds);

    for (let index = 1; index < 5; index++) {
      await page.reload();
      await page.locator(".thread-select").click({ force: true });
      await expect(page.getByRole("button", { name: "Activity 80" })).toBeVisible();
      await waitForStableRenderer(application);
      reloadSamples.push(await processSnapshot(application));
    }

    const toolSidebar = page.getByRole("button", { name: "Toggle workspace tool sidebar" });
    if ((await toolSidebar.getAttribute("aria-pressed")) === "false") await toolSidebar.click();
    await page.getByRole("tab", { name: "审阅", exact: true }).click();
    await expect(page.getByText("Showing a bounded result set.")).toBeVisible();
    await expect(page.getByText("Diff projection was truncated at the existing bound.")).toBeVisible();
    const changedFileCount = await page.evaluate(async () => (await window.caicli.getChanges({})).data?.changedFiles.length ?? 0);
    expect(changedFileCount).toBe(50);

    const bottomPanel = page.getByRole("button", { name: "Toggle workspace bottom panel" });
    if ((await bottomPanel.getAttribute("aria-pressed")) === "false") await bottomPanel.click();
    await expect(bottomPanel).toHaveAttribute("aria-pressed", "true");
    await page.getByRole("button", { name: "新建终端", exact: true }).click();
    const terminalInput = page.getByLabel("Terminal input");
    await terminalInput.pressSequentially("long-output");
    await terminalInput.press("Enter");
    await expect(page.locator(".terminal-session-status")).toContainText("较早输出已截断");
    await expect(page.locator(".xterm-rows")).toContainText("long-output-tail-sentinel");
    const projection = await page.evaluate(async () => window.caicli.getTerminal({ sessionId: "terminal_fixture", afterCursor: 0 }));
    expect(projection.data?.cursor).toBe(70 * 1024);
    expect(projection.data?.output.length).toBeLessThanOrEqual(64 * 1024);
    await page.getByRole("button", { name: /Ctrl\+C/ }).click();
    await expect(page.locator(".terminal-session-status")).toContainText("exited");
    await page.getByRole("button", { name: "关闭", exact: true }).click();

    postWorkloadIdle = await captureSamplingWindow(application, idleRecoverySeconds, sampleIntervalSeconds);
    retention = calculateRetention(warmBaseline, postWorkloadIdle, reloadSamples);
    expect(retention.idleWorkingSetPercent, "Renderer retained working set after symmetric fixed idle windows").toBeLessThanOrEqual(15);
    expect(retention.idlePrivateBytesPercent, "Renderer retained private bytes after symmetric fixed idle windows").toBeLessThanOrEqual(15);
  } catch (error) {
    primaryError = error;
  }

  const ownedPids = uniquePids([...(warmBaseline?.samples ?? []), ...reloadSamples, ...(postWorkloadIdle?.samples ?? [])]);
  try {
    if (application) await application.close();
    await waitForProcessesToExit(ownedPids);
    processDelta = countLiveProcesses(ownedPids);
    fs.rmSync(root, { recursive: true, force: true });
    tempDelta = fs.existsSync(root) ? 1 : 0;
    expect(processDelta, "Long-session owned processes must exit").toBe(0);
    expect(tempDelta, "Long-session temp root must be released").toBe(0);
  } catch (error) {
    cleanupError = error;
  }

  const passed = primaryError === null && cleanupError === null && retention !== null &&
    retention.idleWorkingSetPercent <= 15 && retention.idlePrivateBytesPercent <= 15 &&
    processDelta === 0 && tempDelta === 0;
  const evidence = {
    schemaVersion: 2,
    status: passed ? "Passed" : "Failed",
    workload: "week77-long-session-v2",
    profile: testInfo.repeatEachIndex + 1,
    timelineItems: 240,
    pageSize: 80,
    reloads: 5,
    diffFiles: 50,
    terminalProducedBytes: 70 * 1024,
    terminalRetainedBytes: 64 * 1024,
    sampling: { warmBaselineSeconds: idleRecoverySeconds, postWorkloadIdleSeconds: idleRecoverySeconds, sampleIntervalSeconds, settleSeconds: idleRecoverySeconds - ((settledSampleCount - 1) * sampleIntervalSeconds), settledSampleCount, workingSetStatistic: "warm-window-peak-to-post-idle-settled-median", privateBytesStatistic: "median-of-settled-suffix" },
    retention,
    warmBaseline,
    reloadSamples,
    postWorkloadIdle,
    cleanup: { processDelta, tempDelta },
    failure: primaryError === null && cleanupError === null ? null : safeError(primaryError ?? cleanupError),
  };
  const evidenceBody = JSON.stringify(evidence, null, 2);
  const evidenceDirectory = process.env.CAICLI_WEEK77_EVIDENCE_DIR ?? path.resolve(desktopRoot, "..", "..", "artifacts", "desktop-performance", "week77-single-profiles");
  if (evidenceDirectory) {
    fs.mkdirSync(evidenceDirectory, { recursive: true });
    const evidenceName = process.env.CAICLI_WEEK77_EVIDENCE_DIR
      ? `long-session-${testInfo.repeatEachIndex + 1}.json`
      : `long-session-${new Date().toISOString().replaceAll(":", "-")}-${process.pid}.json`;
    fs.writeFileSync(path.join(evidenceDirectory, evidenceName), evidenceBody, "utf8");
  }
  await testInfo.attach("week77-long-session-metrics.json", { body: Buffer.from(evidenceBody), contentType: "application/json" });
  if (primaryError && cleanupError) throw new AggregateError([primaryError, cleanupError], "Long-session scenario and cleanup both failed.", { cause: cleanupError });
  if (primaryError) throw primaryError;
  if (cleanupError) throw cleanupError;
});

interface ProcessMetric { readonly pid: number; readonly role: string; readonly workingSetBytes: number; readonly privateBytes: number }
interface RoleSnapshot { readonly role: string; readonly processCount: number; readonly workingSetBytes: number; readonly privateBytes: number }
interface ProcessSnapshot { readonly capturedAtUtc: string; readonly processes: readonly ProcessMetric[]; readonly roles: readonly RoleSnapshot[] }
interface SamplingWindow { readonly durationSeconds: number; readonly sampleIntervalSeconds: number; readonly settleSeconds: number; readonly settledSampleCount: number; readonly samples: readonly ProcessSnapshot[]; readonly rendererWorkingSetPeakBytes: number; readonly rendererSettledMedian: { readonly workingSetBytes: number; readonly privateBytes: number } }
interface RetentionResult { readonly reloadTransientPeakWorkingSetPercent: number; readonly idleWorkingSetPercent: number; readonly idlePrivateBytesPercent: number }

async function processSnapshot(application: ElectronApplication): Promise<ProcessSnapshot> {
  return application.evaluate(({ app }) => {
    const processes = app.getAppMetrics().map((metric) => ({
      pid: metric.pid,
      role: metric.type,
      workingSetBytes: metric.memory.workingSetSize * 1024,
      privateBytes: metric.memory.privateBytes * 1024,
    })).sort((left, right) => left.pid - right.pid);
    const roles = new Map<string, { processCount: number; workingSetBytes: number; privateBytes: number }>();
    for (const metric of processes) {
      const value = roles.get(metric.role) ?? { processCount: 0, workingSetBytes: 0, privateBytes: 0 };
      value.processCount++;
      value.workingSetBytes += metric.workingSetBytes;
      value.privateBytes += metric.privateBytes;
      roles.set(metric.role, value);
    }
    for (const role of ["Browser", "Tab", "GPU", "Utility", "AppHost"]) {
      if (!roles.has(role)) roles.set(role, { processCount: 0, workingSetBytes: 0, privateBytes: 0 });
    }
    return {
      capturedAtUtc: new Date().toISOString(),
      processes,
      roles: [...roles].map(([role, value]) => ({ role, ...value })).sort((left, right) => left.role.localeCompare(right.role)),
    };
  });
}

async function captureSamplingWindow(application: ElectronApplication, durationSeconds: number, intervalSeconds: number): Promise<SamplingWindow> {
  const samples = [await processSnapshot(application)];
  const sampleCount = Math.floor(durationSeconds / intervalSeconds);
  for (let index = 0; index < sampleCount; index++) {
    await new Promise((resolve) => setTimeout(resolve, intervalSeconds * 1000));
    samples.push(await processSnapshot(application));
  }
  const settledSamples = samples.slice(-settledSampleCount);
  return {
    durationSeconds,
    sampleIntervalSeconds: intervalSeconds,
    settleSeconds: durationSeconds - ((settledSampleCount - 1) * intervalSeconds),
    settledSampleCount,
    samples,
    rendererWorkingSetPeakBytes: Math.max(...samples.map((sample) => roleBytes(sample, "Tab", "workingSetBytes"))),
    rendererSettledMedian: {
      workingSetBytes: median(settledSamples.map((sample) => roleBytes(sample, "Tab", "workingSetBytes"))),
      privateBytes: median(settledSamples.map((sample) => roleBytes(sample, "Tab", "privateBytes"))),
    },
  };
}

async function waitForStableRenderer(application: ElectronApplication): Promise<void> {
  await expect.poll(async () => (await processSnapshot(application)).roles.find((role) => role.role === "Tab")?.processCount ?? 0).toBe(1);
}

function calculateRetention(baseline: SamplingWindow, idle: SamplingWindow, reloads: readonly ProcessSnapshot[]): RetentionResult {
  const reloadPeak = Math.max(...reloads.map((sample) => roleBytes(sample, "Tab", "workingSetBytes")));
  return {
    reloadTransientPeakWorkingSetPercent: percentChange(baseline.rendererWorkingSetPeakBytes, reloadPeak),
    idleWorkingSetPercent: percentChange(baseline.rendererWorkingSetPeakBytes, idle.rendererSettledMedian.workingSetBytes),
    idlePrivateBytesPercent: percentChange(baseline.rendererSettledMedian.privateBytes, idle.rendererSettledMedian.privateBytes),
  };
}

function roleBytes(snapshot: ProcessSnapshot, role: string, field: "workingSetBytes" | "privateBytes"): number {
  return snapshot.roles.find((candidate) => candidate.role === role)?.[field] ?? 0;
}

function percentChange(baseline: number, value: number): number {
  if (baseline <= 0) throw new Error("Renderer process baseline was not observed.");
  return ((value - baseline) / baseline) * 100;
}

function median(values: readonly number[]): number {
  if (values.length === 0) throw new Error("A sampling window must contain at least one sample.");
  const ordered = [...values].sort((left, right) => left - right);
  const middle = Math.floor(ordered.length / 2);
  return ordered.length % 2 === 0 ? (ordered[middle - 1]! + ordered[middle]!) / 2 : ordered[middle]!;
}

function uniquePids(snapshots: readonly ProcessSnapshot[]): number[] {
  return [...new Set(snapshots.flatMap((sample) => sample.processes.map((process) => process.pid)))];
}

async function waitForProcessesToExit(pids: readonly number[]): Promise<void> {
  const deadline = Date.now() + 5_000;
  while (countLiveProcesses(pids) > 0 && Date.now() < deadline) await new Promise((resolve) => setTimeout(resolve, 100));
}

function countLiveProcesses(pids: readonly number[]): number {
  return pids.filter((pid) => {
    try { process.kill(pid, 0); return true; } catch { return false; }
  }).length;
}

function safeError(error: unknown): { readonly name: string; readonly message: string } {
  return error instanceof Error ? { name: error.name, message: error.message } : { name: "Error", message: String(error) };
}
