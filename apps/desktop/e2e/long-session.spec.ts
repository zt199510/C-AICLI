import { _electron as electron, expect, test } from "@playwright/test";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";

const desktopRoot = path.resolve(import.meta.dirname, "..");

test("long timeline, bounded diff, and terminal output survive repeated reloads", async ({ browserName }, testInfo) => {
  if (browserName !== "chromium") throw new Error("Electron tests require Chromium.");
  testInfo.setTimeout(90_000);
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "caicli-week75-long-session-"));
  const application = await electron.launch({
    args: ["--disable-gpu", path.join(desktopRoot, "e2e", "fixture-main.cjs")],
    env: { ...process.env, CAICLI_E2E_SCENARIO: "long-session", CAICLI_E2E_ROOT: root, APPDATA: path.join(root, "appdata") },
  });
  const metrics: ProcessSnapshot[] = [];
  let retention: { reloadPeakWorkingSetPercent: number; idleWorkingSetPercent: number; idlePrivateBytesPercent: number } | null = null;
  let primaryError: unknown = null;
  try {
    const page = await application.firstWindow();
    await expect(page.getByText("Fixture review thread")).toBeVisible();
    await page.locator(".thread-select").click({ force: true });
    await expect(page.getByText("80 loaded items")).toBeVisible();
    await page.getByRole("button", { name: "Load newer items" }).click();
    await expect(page.getByText("160 loaded items")).toBeVisible();
    await page.getByRole("button", { name: "Load newer items" }).click();
    await expect(page.getByText("240 loaded items")).toBeVisible();
    expect(await page.locator(".timeline-card").count()).toBeLessThan(100);
    await waitForStableRenderer(application);
    metrics.push(await processSnapshot(application));

    for (let index = 0; index < 5; index++) {
      await page.reload();
      await page.locator(".thread-select").click({ force: true });
      await expect(page.getByText("80 loaded items")).toBeVisible();
      await waitForStableRenderer(application);
      metrics.push(await processSnapshot(application));
    }

    await page.getByRole("tab", { name: "Changes" }).click();
    await expect(page.getByText("Showing a bounded result set.")).toBeVisible();
    await expect(page.getByText("Diff projection was truncated at the existing bound.")).toBeVisible();
    await expect(page.locator(".review-section li")).toHaveCount(50);

    await page.getByRole("button", { name: "Open terminal" }).click();
    await page.getByRole("textbox", { name: "Terminal input" }).fill("long-output");
    await page.getByRole("button", { name: "Send" }).click();
    await expect(page.getByRole("status").filter({ hasText: "output truncated" })).toBeVisible();
    const terminal = page.locator(".terminal-output");
    await expect(terminal).toContainText("[earlier output truncated]");
    await expect(terminal).toContainText("long-output-tail-sentinel");
    const projection = await page.evaluate(async () => window.caicli.getTerminal({ sessionId: "terminal_fixture", afterCursor: 0 }));
    expect(projection.data?.cursor).toBe(70 * 1024);
    expect(projection.data?.output.length).toBeLessThanOrEqual(64 * 1024);
    await page.getByRole("button", { name: "Cancel process" }).click();
    await expect(page.getByText(/exited/)).toBeVisible();
    await page.getByRole("button", { name: "Close terminal" }).click();
    const peak = await processSnapshot(application);
    await page.waitForTimeout(30_000); // fixed idle recovery measurement window, not a readiness wait
    const idle = await processSnapshot(application);
    metrics.push(peak, idle);
    const reloadBaseline = roleBytes(metrics[1]!, "Tab", "workingSetBytes");
    const reloadFinal = roleBytes(metrics[5]!, "Tab", "workingSetBytes");
    retention = {
      reloadPeakWorkingSetPercent: percentChange(reloadBaseline, reloadFinal),
      idleWorkingSetPercent: percentChange(reloadBaseline, roleBytes(idle, "Tab", "workingSetBytes")),
      idlePrivateBytesPercent: percentChange(roleBytes(metrics[1]!, "Tab", "privateBytes"), roleBytes(idle, "Tab", "privateBytes")),
    };
    expect(retention.idleWorkingSetPercent, "Renderer retained working set after the fixed idle recovery window").toBeLessThanOrEqual(15);
    expect(retention.idlePrivateBytesPercent, "Renderer retained private bytes after the fixed idle recovery window").toBeLessThanOrEqual(15);
  } catch (error) {
    primaryError = error;
  }

  let cleanupError: unknown = null;
  try {
    await application.close();
    fs.rmSync(root, { recursive: true, force: true });
    expect(fs.existsSync(root), "Long-session temp root must be released.").toBe(false);
  } catch (error) {
    cleanupError = error;
  }
  const evidence = { schemaVersion: 1, workload: "week76-long-session-v1", timelineItems: 240, pageSize: 80, reloads: 5, diffFiles: 50, terminalProducedBytes: 70 * 1024, terminalRetainedBytes: 64 * 1024, idleRecoverySeconds: 30, retention, samples: metrics };
  const evidenceBody = JSON.stringify(evidence, null, 2);
  if (process.env.CAICLI_WEEK76_EVIDENCE_DIR) {
    fs.mkdirSync(process.env.CAICLI_WEEK76_EVIDENCE_DIR, { recursive: true });
    fs.writeFileSync(path.join(process.env.CAICLI_WEEK76_EVIDENCE_DIR, `long-session-${testInfo.repeatEachIndex + 1}.json`), evidenceBody, "utf8");
  }
  await testInfo.attach("week75-long-session-metrics.json", {
    body: Buffer.from(evidenceBody),
    contentType: "application/json",
  });
  if (primaryError && cleanupError) throw new AggregateError([primaryError, cleanupError], "Long-session scenario and cleanup both failed.", { cause: cleanupError });
  if (primaryError) throw primaryError;
  if (cleanupError) throw cleanupError;
});

interface RoleSnapshot { readonly role: string; readonly processCount: number; readonly workingSetBytes: number; readonly privateBytes: number }
interface ProcessSnapshot { readonly capturedAtUtc: string; readonly roles: readonly RoleSnapshot[] }

async function processSnapshot(application: Awaited<ReturnType<typeof electron.launch>>): Promise<ProcessSnapshot> {
  return application.evaluate(({ app }) => {
    const roles = new Map<string, { processCount: number; workingSetBytes: number; privateBytes: number }>();
    for (const metric of app.getAppMetrics()) {
      const value = roles.get(metric.type) ?? { processCount: 0, workingSetBytes: 0, privateBytes: 0 };
      value.processCount++;
      value.workingSetBytes += metric.memory.workingSetSize * 1024;
      value.privateBytes += metric.memory.privateBytes * 1024;
      roles.set(metric.type, value);
    }
    return { capturedAtUtc: new Date().toISOString(), roles: [...roles].map(([role, value]) => ({ role, ...value })).sort((left, right) => left.role.localeCompare(right.role)) };
  });
}

async function waitForStableRenderer(application: Awaited<ReturnType<typeof electron.launch>>): Promise<void> {
  await expect.poll(async () => (await processSnapshot(application)).roles.find((role) => role.role === "Tab")?.processCount ?? 0).toBeLessThanOrEqual(1);
}

function roleBytes(snapshot: ProcessSnapshot, role: string, field: "workingSetBytes" | "privateBytes"): number {
  return snapshot.roles.find((candidate) => candidate.role === role)?.[field] ?? 0;
}

function percentChange(baseline: number, value: number): number {
  if (baseline <= 0) throw new Error("Renderer process baseline was not observed.");
  return ((value - baseline) / baseline) * 100;
}
