import { _electron as electron, expect, test, type CDPSession, type ElectronApplication, type Page } from "@playwright/test";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";

const desktopRoot = path.resolve(import.meta.dirname, "..");
const repositoryRoot = path.resolve(desktopRoot, "..", "..");
const evidencePath = path.join(repositoryRoot, "artifacts", "week81-renderer-memory-remediation", "reload-diagnosis.json");

test("records staged workload allocation without changing the Gate", async ({ browserName }, testInfo) => {
  if (browserName !== "chromium") throw new Error("Electron diagnostics require Chromium.");
  testInfo.setTimeout(210_000);
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "caicli-week81-reload-diagnosis-"));
  let application: ElectronApplication | null = null;
  let cdp: CDPSession | null = null;
  const samples: DiagnosticSample[] = [];
  try {
    application = await electron.launch({
      args: ["--disable-gpu", path.join(desktopRoot, "e2e", "fixture-main.cjs")],
      env: { ...process.env, CAICLI_E2E_SCENARIO: "long-session", CAICLI_E2E_ROOT: root, APPDATA: path.join(root, "appdata") },
    });
    const page = await application.firstWindow();
    cdp = await page.context().newCDPSession(page);
    await cdp.send("Performance.enable");
    await expect(page.getByText("Fixture review thread")).toBeVisible();
    await page.locator(".thread-select").click({ force: true });
    await expect(page.getByText("80 loaded items")).toBeVisible();
    await page.getByRole("button", { name: "Load newer items" }).click();
    await expect(page.getByText("160 loaded items")).toBeVisible();
    await page.getByRole("button", { name: "Load newer items" }).click();
    await expect(page.getByText("240 loaded items")).toBeVisible();
    await installLifecycleCounters(page);
    samples.push(await sample("initial-240", application, page, cdp));

    for (let index = 1; index <= 4; index++) {
      await page.reload();
      await page.locator(".thread-select").click({ force: true });
      await expect(page.getByText("80 loaded items")).toBeVisible();
      await page.waitForTimeout(1_000);
      samples.push(await sample(`reload-${index}`, application, page, cdp));
      await installLifecycleCounters(page);
    }

    await page.waitForTimeout(30_000);
    samples.push(await sample("post-reloads-30s", application, page, cdp));
    await page.getByRole("tab", { name: "Changes" }).click();
    await expect(page.locator(".review-section li")).toHaveCount(50);
    samples.push(await sample("post-changes-0s", application, page, cdp));
    await page.waitForTimeout(30_000);
    samples.push(await sample("post-changes-30s", application, page, cdp));
    await page.getByRole("button", { name: "Open terminal" }).click();
    samples.push(await sample("post-terminal-open-0s", application, page, cdp));
    await page.waitForTimeout(30_000);
    samples.push(await sample("post-terminal-open-30s", application, page, cdp));
    await page.getByRole("textbox", { name: "Terminal input" }).fill("long-output");
    await page.getByRole("button", { name: "Send" }).click();
    await expect(page.getByRole("status").filter({ hasText: "output truncated" })).toBeVisible();
    samples.push(await sample("post-terminal-input-0s", application, page, cdp));
    await page.waitForTimeout(30_000);
    samples.push(await sample("post-terminal-input-30s", application, page, cdp));
    await page.getByRole("button", { name: "Cancel process" }).click();
    await expect(page.getByText(/exited/)).toBeVisible();
    samples.push(await sample("post-terminal-cancel-0s", application, page, cdp));
    await page.waitForTimeout(30_000);
    samples.push(await sample("post-terminal-cancel-30s", application, page, cdp));
    await page.getByRole("button", { name: "Close terminal" }).click();
    samples.push(await sample("post-terminal-close-0s", application, page, cdp));
    await page.waitForTimeout(30_000);
    samples.push(await sample("post-terminal-close-30s", application, page, cdp));

    fs.mkdirSync(path.dirname(evidencePath), { recursive: true });
    fs.writeFileSync(evidencePath, `${JSON.stringify({
      schemaVersion: "week81-reload-diagnosis/v1",
      status: "Measured",
      settings: { reloads: 4, stageIdleSeconds: 30, forcedGc: false, heapSnapshot: false, gateEligible: false },
      samples,
    }, null, 2)}\n`, "utf8");
  } finally {
    if (cdp) await cdp.detach().catch(() => undefined);
    if (application) await application.close();
    fs.rmSync(root, { recursive: true, force: true });
    expect(fs.existsSync(root), "Reload diagnosis temp root must be released.").toBe(false);
  }
});

interface DiagnosticSample {
  readonly label: string;
  readonly rendererPrivateBytes: number;
  readonly rendererWorkingSetBytes: number;
  readonly jsHeapUsedBytes: number;
  readonly jsHeapTotalBytes: number;
  readonly nodes: number;
  readonly documents: number;
  readonly jsEventListeners: number;
  readonly visibleTimelineCards: number;
  readonly lifecycleEvents: { readonly beforeunload: number; readonly pagehide: number; readonly unload: number };
  readonly subtreeNodes: Readonly<Record<string, number>>;
}

async function sample(
  label: string,
  application: ElectronApplication,
  page: Page,
  cdp: CDPSession,
): Promise<DiagnosticSample> {
  const [processes, performance, dom, visibleTimelineCards, lifecycleEvents, subtreeNodes] = await Promise.all([
    application.evaluate(({ app }) => app.getAppMetrics().map((metric) => ({
      role: metric.type,
      privateBytes: metric.memory.privateBytes * 1024,
      workingSetBytes: metric.memory.workingSetSize * 1024,
    }))),
    cdp.send("Performance.getMetrics"),
    cdp.send("Memory.getDOMCounters"),
    page.locator(".timeline-card").count(),
    page.evaluate(() => ({
      beforeunload: Number(sessionStorage.getItem("caicli-week81-beforeunload") ?? 0),
      pagehide: Number(sessionStorage.getItem("caicli-week81-pagehide") ?? 0),
      unload: Number(sessionStorage.getItem("caicli-week81-unload") ?? 0),
    })),
    page.evaluate(() => {
      const count = (selector: string) => {
        const root = document.querySelector(selector);
        if (!root) return 0;
        const walker = document.createTreeWalker(root, NodeFilter.SHOW_ALL);
        let nodes = 1;
        while (walker.nextNode()) nodes++;
        return nodes;
      };
      return Object.fromEntries([
        ".titlebar", "#threads-panel", ".task-surface", ".timeline-view", ".terminal-panel",
        ".task-controls", ".composer", "#review-inspector-panel", ".review-content",
      ].map((selector) => [selector, count(selector)]));
    }),
  ]);
  const renderer = processes.find((process) => process.role === "Tab");
  if (!renderer) throw new Error("Renderer metrics are unavailable.");
  const metric = (name: string) => performance.metrics.find((candidate) => candidate.name === name)?.value ?? 0;
  return {
    label,
    rendererPrivateBytes: renderer.privateBytes,
    rendererWorkingSetBytes: renderer.workingSetBytes,
    jsHeapUsedBytes: metric("JSHeapUsedSize"),
    jsHeapTotalBytes: metric("JSHeapTotalSize"),
    nodes: dom.nodes,
    documents: dom.documents,
    jsEventListeners: dom.jsEventListeners,
    visibleTimelineCards,
    lifecycleEvents,
    subtreeNodes,
  };
}

async function installLifecycleCounters(page: Page): Promise<void> {
  await page.evaluate(() => {
    for (const event of ["beforeunload", "pagehide", "unload"] as const) {
      window.addEventListener(event, () => {
        const key = `caicli-week81-${event}`;
        sessionStorage.setItem(key, String(Number(sessionStorage.getItem(key) ?? 0) + 1));
      }, { once: true });
    }
  });
}
