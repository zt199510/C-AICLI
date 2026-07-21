import { expect, test } from "@playwright/test";
import { cleanupDesktopCase, createDesktopCase, launchDesktop } from "./desktop-harness";

test("packaged trust boundary and keyboard accessibility stay fail-closed", async ({ browserName }, testInfo) => {
  if (browserName !== "chromium") throw new Error("Electron tests require Chromium.");
  testInfo.setTimeout(90_000);
  const value = createDesktopCase("week76-hardening", testInfo.project.name === "packaged");
  let primaryError: unknown = null;
  try {
    const { application, page } = await launchDesktop(value);
    const initialUrl = page.url();
    expect(await page.evaluate(() => window.open("https://example.invalid/", "_blank"))).toBeNull();
    const permission = await page.evaluate(async () => (await navigator.permissions.query({ name: "clipboard-read" as PermissionName })).state);
    expect(permission).toBe("denied");

    const openWorkspace = page.getByRole("button", { name: "Open workspace" }).first();
    await openWorkspace.focus();
    await page.keyboard.press("Enter");
    await expect.poll(async () => page.evaluate(async () => (await window.caicli.getWorkspaceSnapshot())?.workspaceId ?? null)).not.toBeNull();
    const workspaceId = await page.evaluate(async () => (await window.caicli.getWorkspaceSnapshot())?.workspaceId);
    await page.evaluate(() => {
      const transfer = new DataTransfer();
      transfer.setData("text/plain", "C:\\sentinel-secret\\outside-workspace.txt");
      document.body.dispatchEvent(new DragEvent("drop", { bubbles: true, cancelable: true, dataTransfer: transfer }));
    });
    expect(await page.evaluate(async () => (await window.caicli.getWorkspaceSnapshot())?.workspaceId)).toBe(workspaceId);

    const create = page.getByRole("button", { name: "Create thread" });
    await create.focus();
    await page.keyboard.press("Enter");
    await page.getByLabel("Thread title").fill("Keyboard review");
    await page.keyboard.press("Enter");
    await expect(page.getByText("Keyboard review", { exact: true }).first()).toBeVisible();

    const rename = page.getByRole("button", { name: "Rename Keyboard review" });
    await rename.focus();
    await page.keyboard.press("Enter");
    const renameInput = page.getByLabel("Rename thread");
    await expect(renameInput).toBeFocused();
    await page.keyboard.press("Escape");
    await expect(rename).toBeFocused();

    const archive = page.getByRole("button", { name: "Archive Keyboard review" });
    await archive.focus();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("alertdialog", { name: "Archive this thread?" })).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(archive).toBeFocused();

    const prompt = page.getByRole("textbox", { name: "Composer prompt" });
    await prompt.fill("Review @sent");
    const mentions = page.getByRole("listbox", { name: "Composer mentions" });
    await expect(mentions).toBeVisible();
    await expect(mentions.getByRole("option").first()).toBeVisible();
    await page.keyboard.press("End");
    expect(await prompt.getAttribute("aria-activedescendant")).toMatch(/^mention-/);
    await page.keyboard.press("Enter");
    await expect(prompt).toBeFocused();

    const changes = page.getByRole("tab", { name: "Changes" });
    await changes.focus();
    await page.keyboard.press("ArrowRight");
    await expect(page.getByRole("tab", { name: "Reports" })).toBeFocused();
    expect(await page.getByRole("tabpanel").getAttribute("aria-labelledby")).toBe("review-tab-reports");

    await page.setViewportSize({ width: 760, height: 560 });
    const showThreads = page.getByRole("button", { name: "Show threads" });
    await showThreads.focus();
    await page.keyboard.press("Enter");
    await page.getByRole("button", { name: "Collapse threads" }).focus();
    await page.keyboard.press("Escape");
    await expect(showThreads).toBeFocused();

    await page.emulateMedia({ reducedMotion: "reduce", forcedColors: "active" });
    const mediaEvidence = await page.evaluate(() => {
      const probe = document.createElement("span");
      probe.className = "spin";
      document.body.append(probe);
      const animationName = getComputedStyle(probe).animationName;
      probe.remove();
      return { animationName, reducedMotion: matchMedia("(prefers-reduced-motion: reduce)").matches, forcedColors: matchMedia("(forced-colors: active)").matches };
    });
    expect(mediaEvidence).toEqual({ animationName: "none", reducedMotion: true, forcedColors: true });

    for (const viewport of [{ width: 1024, height: 720 }, { width: 760, height: 560 }, { width: 520, height: 480 }, { width: 320, height: 480 }]) {
      await page.setViewportSize(viewport);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
    }
    await page.setViewportSize({ width: 1024, height: 720 });
    await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]?.webContents.setZoomFactor(2));
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
    await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]?.webContents.setZoomFactor(1));
    await testInfo.attach("week76-accessibility-tree.txt", { body: Buffer.from(await page.locator("body").ariaSnapshot()), contentType: "text/plain" });
    await page.evaluate(() => { window.location.href = "https://example.invalid/navigation-blocked"; });
    await expect.poll(() => page.url()).toBe(initialUrl);
  } catch (error) {
    primaryError = error;
  }

  let cleanupError: unknown = null;
  try { await cleanupDesktopCase(value, testInfo); } catch (error) { cleanupError = error; }
  if (primaryError && cleanupError) throw new AggregateError([primaryError, cleanupError], "Hardening scenario and cleanup both failed.");
  if (primaryError) throw primaryError;
  if (cleanupError) throw cleanupError;
});
