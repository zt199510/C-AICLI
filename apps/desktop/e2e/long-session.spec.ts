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
  const metrics: number[] = [];
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
    metrics.push(await workingSet(application));

    for (let index = 0; index < 5; index++) {
      await page.reload();
      await page.locator(".thread-select").click({ force: true });
      await expect(page.getByText("80 loaded items")).toBeVisible();
      metrics.push(await workingSet(application));
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
    metrics.push(await workingSet(application));
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
  await testInfo.attach("week75-long-session-metrics.json", {
    body: Buffer.from(JSON.stringify({ timelineItems: 240, pageSize: 80, reloads: 5, diffFiles: 50, terminalProducedBytes: 70 * 1024, terminalRetainedBytes: 64 * 1024, workingSetKiB: metrics }, null, 2)),
    contentType: "application/json",
  });
  if (primaryError && cleanupError) throw new AggregateError([primaryError, cleanupError], "Long-session scenario and cleanup both failed.", { cause: cleanupError });
  if (primaryError) throw primaryError;
  if (cleanupError) throw cleanupError;
});

async function workingSet(application: Awaited<ReturnType<typeof electron.launch>>): Promise<number> {
  return application.evaluate(({ app }) => app.getAppMetrics().reduce((sum, metric) => sum + metric.memory.workingSetSize, 0));
}
