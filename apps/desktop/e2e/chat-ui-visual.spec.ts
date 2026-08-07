import { _electron as electron, expect, test, type ElectronApplication, type Locator, type Page } from "@playwright/test";
import crypto from "node:crypto";
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const desktopRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const repositoryRoot = path.resolve(desktopRoot, "..", "..");
const gateRoot = path.join(repositoryRoot, "artifacts", "week86-renderer-chat-first-shell");
const supplementalRoot = path.join(repositoryRoot, "artifacts", "renderer-chat-ui-visual");
const viewports = [
  { width: 1440, height: 900, name: "desktop" },
  { width: 1280, height: 810, name: "codex-reference" },
  { width: 1024, height: 768, name: "compact" },
  { width: 800, height: 900, name: "narrow" },
] as const;
const gateFixtures = [
  { fixture: "open-workspace", name: "open-workspace" },
  { fixture: "selected-thread", name: "selected-thread" },
  { fixture: "waiting-approval", name: "waiting-approval" },
  { fixture: "runtime-unavailable", name: "failed-runtime" },
  { fixture: "review-terminal", name: "review-terminal-open" },
] as const;
const supplementalFixtures = [
  "new",
  "completed",
  "thinking",
  "streaming",
  "retry-wait",
  "retry-exhausted",
  "approval",
  "canceled",
  "activity-expanded",
  "runtime-unavailable",
] as const;

interface VisualCase {
  caseId: string;
  fixture: string;
  viewport: { width: number; height: number };
  screenshot: string;
  screenshotSha256: string;
  accessibilitySnapshot: string;
  accessibilitySnapshotSha256: string;
  bodyHorizontalOverflow: number;
  criticalOverlapCount: number;
  unreachableVisibleActionCount: number;
  unnamedVisibleActionCount: number;
  duplicateAssistantIdentityCount: number;
  duplicateUserSequenceCount: number;
  assistantBlockCount: number;
  primaryMutationActionCount: number;
  largeRunningTurnCardCount: number;
  composerPosition: string;
  openDrawerCount: number;
  drawerMutualExclusionPassed: boolean;
  timelineStageWidthBefore: number;
  timelineStageWidthAfter: number;
  timelineStageWidthDelta: number;
  timelineStageHeightBefore: number;
  timelineStageHeightAfter: number;
  timelineStageHeightDelta: number;
  floatingSurfaceOverlapCount: number;
  composerFloatingSurfaceOverlapCount: number;
  summaryFloatingPositioningPassed: boolean;
  bottomDockingPassed: boolean;
  nonModalFloatingSurfacesPassed: boolean;
  panelToggleOrder: string;
  toolSidebarPosition: string;
  toolSidebarWidth: number;
  taskSurfaceWidth: number;
  toolSidebarHeaderAlignmentPassed: boolean;
  composerMenuOpenCount: number;
  composerMenuObscuredActionCount: number;
}

interface PanelLayoutBefore {
  stageWidth: number;
  stageHeight: number;
  toolSidebarPosition: string;
  toolSidebarWidth: number;
  taskSurfaceWidth: number;
  toolSidebarHeaderAlignmentPassed: boolean;
}

test("Chat-first Gate and supplemental visual matrices remain structurally safe", async ({ browserName }) => {
  if (browserName !== "chromium") throw new Error("Electron tests require Chromium.");
  test.setTimeout(240_000);
  const runRoot = fs.mkdtempSync(path.join(os.tmpdir(), "caicli-chat-ui-visual-"));
  const environment = {
    ...process.env,
    APPDATA: path.join(runRoot, "appdata"),
    CAICLI_USER_PROFILE: path.join(runRoot, "profile"),
    CAICLI_E2E_ROOT: runRoot,
    CAICLI_CHAT_UI_FIXTURE: "new",
  };
  const application = await electron.launch({
    args: ["--disable-gpu", "--in-process-gpu", path.join(desktopRoot, "e2e", "fixture-main.cjs")],
    env: environment,
  });
  try {
    const page = await application.firstWindow();
    await expect(page.locator(".app-shell")).toHaveAttribute("data-runtime-state", "ready");
    prepareDirectory(gateRoot);
    prepareDirectory(supplementalRoot);

    const supplementalCases: VisualCase[] = [];
    for (const viewport of viewports) {
      supplementalCases.push(await captureCase(page, supplementalRoot, "new", "new", viewport));
      supplementalCases.push(await captureCase(page, supplementalRoot, "composer-add-menu", "new", viewport, "add"));
    }

    const gateCases: VisualCase[] = [];
    for (const entry of gateFixtures) {
      for (const viewport of viewports) {
        gateCases.push(await captureCase(page, gateRoot, entry.name, entry.fixture, viewport));
      }
    }
    expect(gateCases).toHaveLength(20);
    writeManifest(
      path.join(gateRoot, "screenshot-manifest.json"),
      "week86-gate-owned-chat-first-shell",
      gateCases,
    );
    fs.writeFileSync(
      path.join(gateRoot, "viewport-matrix.json"),
      `${JSON.stringify(summarizeViewportMatrix(gateCases), null, 2)}\n`,
    );

    for (const fixture of supplementalFixtures.slice(1)) {
      for (const viewport of viewports) {
        supplementalCases.push(await captureCase(page, supplementalRoot, fixture, fixture, viewport));
      }
    }
    expect(supplementalCases).toHaveLength(44);
    writeManifest(
      path.join(supplementalRoot, "supplemental-visual-manifest.json"),
      "chat-first-supplemental-visual-matrix",
      supplementalCases,
    );

    for (const entry of [...gateCases, ...supplementalCases]) {
      expect(entry.bodyHorizontalOverflow, `${entry.caseId}: horizontal body overflow`).toBe(0);
      expect(entry.criticalOverlapCount, `${entry.caseId}: composer/drawer overlap`).toBe(0);
      expect(entry.unreachableVisibleActionCount, `${entry.caseId}: unreachable action`).toBe(0);
      expect(entry.unnamedVisibleActionCount, `${entry.caseId}: unnamed action`).toBe(0);
      expect(entry.duplicateAssistantIdentityCount, `${entry.caseId}: duplicate assistant identity`).toBe(0);
      expect(entry.duplicateUserSequenceCount, `${entry.caseId}: duplicate user projection`).toBe(0);
      expect(entry.assistantBlockCount, `${entry.caseId}: assistant block identity`).toBeLessThanOrEqual(1);
      expect(entry.primaryMutationActionCount, `${entry.caseId}: competing primary mutations`).toBeLessThanOrEqual(1);
      expect(entry.largeRunningTurnCardCount, `${entry.caseId}: large running Turn card`).toBe(0);
      expect(entry.composerPosition, `${entry.caseId}: viewport-fixed composer`).not.toBe("fixed");
      expect(entry.drawerMutualExclusionPassed, `${entry.caseId}: narrow drawers must be mutually exclusive`).toBe(true);
      expect(entry.timelineStageWidthDelta, `${entry.caseId}: bottom dock changed the middle grid width`).toBe(0);
      expect(entry.timelineStageHeightDelta, `${entry.caseId}: bottom dock did not reserve its own row`).toBeGreaterThan(0);
      expect(entry.floatingSurfaceOverlapCount, `${entry.caseId}: summary and bottom panel overlap`).toBe(0);
      expect(entry.composerFloatingSurfaceOverlapCount, `${entry.caseId}: floating panel overlaps composer`).toBe(0);
      expect(entry.summaryFloatingPositioningPassed, `${entry.caseId}: summary must remain absolute`).toBe(true);
      expect(entry.bottomDockingPassed, `${entry.caseId}: bottom panel must dock below Composer`).toBe(true);
      expect(entry.nonModalFloatingSurfacesPassed, `${entry.caseId}: floating panels must remain non-modal`).toBe(true);
      expect(entry.panelToggleOrder, `${entry.caseId}: panel toggle order`).toBe("workspace-summary-overlay,workspace-bottom-panel,workspace-tool-sidebar");
      expect(entry.toolSidebarPosition, `${entry.caseId}: sidebar positioning mode`).toBe(entry.viewport.width >= 1200 ? "relative" : "absolute");
      if (entry.viewport.width >= 1200 && entry.fixture !== "review-terminal") {
        expect(entry.toolSidebarWidth, `${entry.caseId}: right workspace width`).toBeGreaterThanOrEqual(600);
        expect(entry.taskSurfaceWidth, `${entry.caseId}: conversation remains usable beside right workspace`).toBeGreaterThanOrEqual(320);
        expect(entry.toolSidebarHeaderAlignmentPassed, `${entry.caseId}: workbench tabbar must align with the task header`).toBe(true);
      }
      expect(entry.composerMenuOpenCount, `${entry.caseId}: Composer menu state`).toBe(entry.caseId.startsWith("composer-add-menu-") ? 1 : 0);
      expect(entry.composerMenuObscuredActionCount, `${entry.caseId}: obscured Composer menu action`).toBe(0);
    }
  } finally {
    await closeElectron(application);
    fs.rmSync(runRoot, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
    expect(fs.existsSync(runRoot), "Visual E2E temp root must be released.").toBe(false);
  }
});

async function captureCase(
  page: Page,
  root: string,
  name: string,
  fixture: string,
  viewport: (typeof viewports)[number],
  composerMenu: "add" | null = null,
): Promise<VisualCase> {
  await page.setViewportSize({ width: viewport.width, height: viewport.height });
  await page.evaluate((nextFixture) => {
    const fixtures = (window as unknown as {
      caicliVisualFixtures?: { setFixture: (value: string) => unknown };
    }).caicliVisualFixtures;
    if (!fixtures) throw new Error("Visual fixture bridge unavailable.");
    fixtures.setFixture(nextFixture);
  }, fixture);
  await waitForFixture(page, fixture);
  const existingComposerMenu = page.locator(".composer-menu");
  if (await existingComposerMenu.count()) {
    await page.keyboard.press("Escape");
    await expect(existingComposerMenu).toHaveCount(0);
  }
  const panelLayoutBefore = await configurePanels(page, fixture, viewport.width);
  if (composerMenu === "add") {
    await page.getByRole("button", { name: "Add context" }).click();
    await expect(page.getByRole("menu", { name: "添加上下文" })).toBeVisible();
  }

  const caseId = `${name}-${viewport.name}-${viewport.width}x${viewport.height}`;
  const screenshotPath = path.join(root, "screenshots", `${caseId}.png`);
  const accessibilityPath = path.join(root, "accessibility", `${caseId}.txt`);
  fs.mkdirSync(path.dirname(screenshotPath), { recursive: true });
  fs.mkdirSync(path.dirname(accessibilityPath), { recursive: true });
  await page.screenshot({ path: screenshotPath, fullPage: false });
  const accessibility = await page.locator("body").ariaSnapshot();
  fs.writeFileSync(accessibilityPath, accessibility);

  const metrics = await page.evaluate((layoutBefore) => {
    const visible = (element: Element) => {
      const html = element as HTMLElement;
      const style = getComputedStyle(html);
      const rect = html.getBoundingClientRect();
      return !html.hidden && html.getAttribute("aria-hidden") !== "true" &&
        style.display !== "none" && style.visibility !== "hidden" && rect.width > 0 && rect.height > 0;
    };
    const rectangle = (selector: string) => {
      const element = document.querySelector<HTMLElement>(selector);
      return element && visible(element) ? element.getBoundingClientRect() : null;
    };
    const intersects = (left: DOMRect | null, right: DOMRect | null) =>
      Boolean(left && right && left.left < right.right && left.right > right.left &&
        left.top < right.bottom && left.bottom > right.top);
    const actions = [...document.querySelectorAll<HTMLElement>("button, a[href], input, textarea, select, [tabindex]")]
      .filter((element) => visible(element) && !element.hasAttribute("disabled") && element.tabIndex >= 0);
    const unreachable = actions.filter((element) => {
      const rect = element.getBoundingClientRect();
      const outsideViewport = rect.right <= 0 || rect.bottom <= 0 || rect.left >= innerWidth || rect.top >= innerHeight;
      if (!outsideViewport) return false;
      let ancestor = element.parentElement;
      while (ancestor) {
        const style = getComputedStyle(ancestor);
        const scrollable = /(auto|scroll)/u.test(`${style.overflow} ${style.overflowX} ${style.overflowY}`) &&
          (ancestor.scrollHeight > ancestor.clientHeight || ancestor.scrollWidth > ancestor.clientWidth);
        const ancestorRect = ancestor.getBoundingClientRect();
        if (scrollable && ancestorRect.right > 0 && ancestorRect.bottom > 0 && ancestorRect.left < innerWidth && ancestorRect.top < innerHeight) return false;
        ancestor = ancestor.parentElement;
      }
      return true;
    });
    const unnamed = actions.filter((element) => {
      const label = element.getAttribute("aria-label") ?? element.getAttribute("title") ??
        element.getAttribute("aria-labelledby") ??
        ("labels" in element && (element as HTMLInputElement).labels?.length
          ? [...(element as HTMLInputElement).labels!].map((candidate) => candidate.textContent).join(" ")
          : null) ?? element.textContent;
      return !label?.trim();
    });
    const composerMenuObscuredActionCount = [...document.querySelectorAll<HTMLElement>(".composer-menu button")]
      .filter(visible)
      .filter((element) => {
        const rect = element.getBoundingClientRect();
        const topmost = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2);
        return Boolean(topmost && !element.contains(topmost));
      }).length;
    const assistantIds = [...document.querySelectorAll<HTMLElement>("[data-assistant-message-id]")]
      .filter(visible)
      .map((element) => element.dataset.assistantMessageId ?? "");
    const userSequences = [...document.querySelectorAll<HTMLElement>(".message-user[data-sequence]")]
      .filter(visible)
      .map((element) => element.dataset.sequence ?? "");
    const duplicateCount = (values: string[]) => values.length - new Set(values).size;
    const openDrawers = [...document.querySelectorAll<HTMLElement>(".drawer.drawer-open")].filter(visible);
    const composer = document.querySelector<HTMLElement>(".composer");
    const mutationGroups = [
      ".composer-send:not([disabled])",
      ".approval-actions",
      ".assistant-recovery-actions",
      ".recovery-actions",
    ].filter((selector) => {
      const element = document.querySelector<HTMLElement>(selector);
      return Boolean(element && visible(element));
    });
    const composerRect = rectangle(".composer");
    const summaryRect = rectangle(".workspace-summary-overlay");
    const bottomPanelRect = rectangle(".workspace-bottom-panel");
    const criticalOverlapCount = [
      rectangle(".thread-sidebar.drawer-open"),
      rectangle(".inspector.drawer-open"),
    ].filter((drawer) => intersects(composerRect, drawer)).length;
    const timelineStage = document.querySelector<HTMLElement>(".timeline-stage");
    const taskSurface = document.querySelector<HTMLElement>(".task-surface");
    const summary = document.querySelector<HTMLElement>(".workspace-summary-overlay");
    const bottomPanel = document.querySelector<HTMLElement>(".workspace-bottom-panel");
    const panelToggleOrder = [...document.querySelectorAll<HTMLElement>(".panel-toggle-group [aria-controls]")]
      .map((element) => element.getAttribute("aria-controls"))
      .join(",");
    const timelineStageWidthAfter = timelineStage?.getBoundingClientRect().width ?? 0;
    const timelineStageHeightAfter = timelineStage?.getBoundingClientRect().height ?? 0;
    const taskSurfaceRect = taskSurface?.getBoundingClientRect() ?? null;
    const bottomPosition = bottomPanel ? getComputedStyle(bottomPanel).position : "missing";
    const bottomDockGap = bottomPanelRect && composerRect ? bottomPanelRect.top - composerRect.bottom : Number.POSITIVE_INFINITY;
    const bottomDockingPassed = Boolean(bottomPanel && bottomPanelRect && taskSurfaceRect &&
      bottomPanel.parentElement === taskSurface && bottomPosition !== "absolute" && bottomPosition !== "fixed" &&
      bottomDockGap >= -1 && bottomDockGap <= 24 &&
      Math.abs(bottomPanelRect.left - taskSurfaceRect.left) <= 1 &&
      Math.abs(bottomPanelRect.right - taskSurfaceRect.right) <= 1 &&
      Math.abs(bottomPanelRect.bottom - taskSurfaceRect.bottom) <= 1);
    return {
      bodyHorizontalOverflow: Math.max(0, document.documentElement.scrollWidth - innerWidth),
      criticalOverlapCount,
      unreachableVisibleActionCount: unreachable.length,
      unnamedVisibleActionCount: unnamed.length,
      duplicateAssistantIdentityCount: duplicateCount(assistantIds),
      duplicateUserSequenceCount: duplicateCount(userSequences),
      assistantBlockCount: assistantIds.length,
      primaryMutationActionCount: mutationGroups.length,
      largeRunningTurnCardCount: [...document.querySelectorAll<HTMLElement>(".task-controls")].filter(visible).length,
      composerPosition: composer ? getComputedStyle(composer).position : "missing",
      openDrawerCount: openDrawers.length,
      drawerMutualExclusionPassed: innerWidth > 899 || openDrawers.length <= 1,
      timelineStageWidthBefore: layoutBefore.stageWidth,
      timelineStageWidthAfter,
      timelineStageWidthDelta: Math.abs(timelineStageWidthAfter - layoutBefore.stageWidth),
      timelineStageHeightBefore: layoutBefore.stageHeight,
      timelineStageHeightAfter,
      timelineStageHeightDelta: Math.max(0, layoutBefore.stageHeight - timelineStageHeightAfter),
      floatingSurfaceOverlapCount: intersects(summaryRect, bottomPanelRect) ? 1 : 0,
      composerFloatingSurfaceOverlapCount: [summaryRect, bottomPanelRect].filter((surface) => intersects(composerRect, surface)).length,
      summaryFloatingPositioningPassed: Boolean(summary && getComputedStyle(summary).position === "absolute"),
      bottomDockingPassed,
      nonModalFloatingSurfacesPassed: [summary, bottomPanel].every((surface) => surface && surface.getAttribute("role") !== "dialog" && !surface.hasAttribute("aria-modal")),
      panelToggleOrder,
      toolSidebarPosition: layoutBefore.toolSidebarPosition,
      toolSidebarWidth: layoutBefore.toolSidebarWidth,
      taskSurfaceWidth: layoutBefore.taskSurfaceWidth,
      toolSidebarHeaderAlignmentPassed: layoutBefore.toolSidebarHeaderAlignmentPassed,
      composerMenuOpenCount: [...document.querySelectorAll<HTMLElement>(".composer-menu")].filter(visible).length,
      composerMenuObscuredActionCount,
    };
  }, panelLayoutBefore);

  return {
    caseId,
    fixture,
    viewport: { width: viewport.width, height: viewport.height },
    screenshot: relative(screenshotPath),
    screenshotSha256: sha256(screenshotPath),
    accessibilitySnapshot: relative(accessibilityPath),
    accessibilitySnapshotSha256: sha256(accessibilityPath),
    ...metrics,
  };
}

async function closeElectron(application: ElectronApplication) {
  let closed = false;
  const close = application.close().then(() => { closed = true; });
  await Promise.race([
    close,
    new Promise<void>((resolve) => setTimeout(resolve, 5_000)),
  ]);
  if (!closed) {
    application.process().kill();
    await Promise.race([
      close.catch(() => undefined),
      new Promise<void>((resolve) => setTimeout(resolve, 2_000)),
    ]);
  }
}

async function waitForFixture(page: Page, fixture: string) {
  if (fixture === "new" || fixture === "open-workspace") {
    await expect(page.getByText("This conversation has no messages yet. Use the composer below to begin.")).toBeVisible();
  } else if (fixture === "runtime-unavailable") {
    await expect(page.getByText("AppHost 不可用")).toBeVisible();
  } else {
    await expect(page.locator('[data-assistant-message-id="fixture-assistant-1"]')).toHaveCount(1);
  }
  await page.waitForTimeout(40);
}

async function configurePanels(page: Page, fixture: string, width: number): Promise<PanelLayoutBefore> {
  const summaryToggle = page.getByRole("button", { name: "Toggle workspace summary" });
  const bottomToggle = page.getByRole("button", { name: "Toggle workspace bottom panel" });
  const sidebarToggle = page.getByRole("button", { name: "Toggle workspace tool sidebar" });
  if (width < 1200 && (await sidebarToggle.getAttribute("aria-pressed")) === "true") {
    await closeSidebarDrawer(page, sidebarToggle);
  }
  await setPressed(summaryToggle, false);
  await setPressed(bottomToggle, false);
  if (width >= 1200) await setPressed(sidebarToggle, true);

  if (width <= 899) {
    const showConversations = page.getByRole("button", { name: "显示会话侧栏" });
    if (await showConversations.isVisible()) {
      await showConversations.click();
      await expect(page.locator("#threads-panel")).toHaveAttribute("aria-hidden", "false");
      await sidebarToggle.click();
      await expect(page.locator("#threads-panel")).toHaveAttribute("aria-hidden", "true");
      await page.getByRole("tab", { name: "审阅", exact: true }).focus();
      await page.keyboard.press("Escape");
      await expect(sidebarToggle).toBeFocused();
    }
  }

  if (width < 1200) await setPressed(sidebarToggle, true);
  const rightLayout = await page.evaluate(() => {
    const sidebar = document.querySelector<HTMLElement>("#workspace-tool-sidebar");
    const sidebarRect = sidebar?.getBoundingClientRect();
    const taskSurfaceRect = document.querySelector<HTMLElement>(".task-surface")?.getBoundingClientRect();
    const taskToolbarRect = document.querySelector<HTMLElement>(".task-toolbar")?.getBoundingClientRect();
    const workspaceContentRect = document.querySelector<HTMLElement>(".workspace-content")?.getBoundingClientRect();
    return {
      toolSidebarPosition: sidebar ? getComputedStyle(sidebar).position : "missing",
      toolSidebarWidth: sidebarRect?.width ?? 0,
      taskSurfaceWidth: taskSurfaceRect?.width ?? 0,
      toolSidebarHeaderAlignmentPassed: Boolean(sidebarRect && taskToolbarRect && workspaceContentRect &&
        Math.abs(sidebarRect.top - taskToolbarRect.top) <= 1 &&
      Math.abs(sidebarRect.right - workspaceContentRect.right) <= 1),
    };
  });
  await setPressed(sidebarToggle, false);
  const stageBeforeBottom = await page.locator(".timeline-stage").evaluate((element) => {
    const rect = element.getBoundingClientRect();
    return { stageWidth: rect.width, stageHeight: rect.height };
  });
  const panelLayoutBefore = { ...rightLayout, ...stageBeforeBottom };
  await setPressed(bottomToggle, true);
  await expect(sidebarToggle).toHaveAttribute("aria-pressed", "false");
  await setPressed(summaryToggle, true);
  return panelLayoutBefore;
}

async function setPressed(locator: Locator, pressed: boolean) {
  if ((await locator.getAttribute("aria-pressed")) !== String(pressed)) await locator.click();
}

async function closeSidebarDrawer(page: Page, sidebarToggle: Locator) {
  await page.locator("#workspace-tool-sidebar button:not([disabled])").first().focus();
  await page.keyboard.press("Escape");
  await expect(sidebarToggle).toHaveAttribute("aria-pressed", "false");
  await expect(sidebarToggle).toBeFocused();
}

function prepareDirectory(directory: string) {
  fs.mkdirSync(path.join(directory, "screenshots"), { recursive: true });
  fs.mkdirSync(path.join(directory, "accessibility"), { recursive: true });
}

function writeManifest(outputPath: string, matrix: string, cases: VisualCase[]) {
  const result = {
    schemaVersion: 1,
    matrix,
    candidateRevision: gitRevision(),
    generatedAtUtc: new Date().toISOString(),
    caseCount: cases.length,
    missingCount: 0,
    horizontalOverflowCount: cases.filter((entry) => entry.bodyHorizontalOverflow > 0).length,
    criticalOverlapCount: cases.reduce((sum, entry) => sum + entry.criticalOverlapCount, 0),
    unreachableControlCount: cases.reduce((sum, entry) => sum + entry.unreachableVisibleActionCount, 0),
    accessibilityViolationCount: cases.reduce((sum, entry) => sum + entry.unnamedVisibleActionCount, 0),
    duplicateMessageCount: cases.reduce((sum, entry) =>
      sum + entry.duplicateAssistantIdentityCount + entry.duplicateUserSequenceCount, 0),
    cases,
  };
  fs.writeFileSync(outputPath, `${JSON.stringify(result, null, 2)}\n`);
}

function summarizeViewportMatrix(cases: VisualCase[]) {
  return {
    schemaVersion: 1,
    caseCount: cases.length,
    missingCount: 0,
    overlapCount: cases.reduce((sum, entry) => sum + entry.criticalOverlapCount, 0),
    horizontalOverflowCount: cases.filter((entry) => entry.bodyHorizontalOverflow > 0).length,
    unreachableControlCount: cases.reduce((sum, entry) => sum + entry.unreachableVisibleActionCount, 0),
    drawerMutualExclusionFailureCount: cases.filter((entry) => !entry.drawerMutualExclusionPassed).length,
    cases: cases.map((entry) => ({
      caseId: entry.caseId,
      viewport: entry.viewport,
      bodyHorizontalOverflow: entry.bodyHorizontalOverflow,
      criticalOverlapCount: entry.criticalOverlapCount,
      unreachableVisibleActionCount: entry.unreachableVisibleActionCount,
      openDrawerCount: entry.openDrawerCount,
      drawerMutualExclusionPassed: entry.drawerMutualExclusionPassed,
    })),
  };
}

function sha256(filePath: string) {
  return crypto.createHash("sha256").update(fs.readFileSync(filePath)).digest("hex");
}

function gitRevision() {
  return execFileSync("git", ["rev-parse", "HEAD"], { cwd: repositoryRoot, encoding: "utf8" }).trim();
}

function relative(filePath: string) {
  return path.relative(repositoryRoot, filePath).replaceAll("\\", "/");
}
