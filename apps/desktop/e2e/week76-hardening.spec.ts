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

    const create = page.getByRole("button", { name: "新建对话" }).first();
    await create.focus();
    await page.keyboard.press("Enter");
    const createdThreadId = await page.evaluate(async (title) => {
      const result = await window.caicli.createThread({ title });
      if (!result.succeeded || !result.data) throw new Error("Keyboard review thread could not be created.");
      return result.data.threadId;
    }, "Keyboard review");
    await expect.poll(async () => page.evaluate(async ({ title, threadId }) => {
      const result = await window.caicli.listThreads();
      return result.data?.threads.some((thread) => thread.threadId === threadId && thread.title === title) ?? false;
    }, { title: "Keyboard review", threadId: createdThreadId })).toBe(true);
    await expect(page.getByText("Keyboard review", { exact: true }).first()).toBeVisible();

    const more = page.getByRole("button", { name: "更多操作：Keyboard review" });
    await more.focus();
    await page.keyboard.press("Enter");
    const rename = page.getByRole("menuitem", { name: "重命名" });
    await expect(rename).toBeFocused();
    await page.keyboard.press("Enter");
    const renameInput = page.getByLabel("重命名对话");
    await expect(renameInput).toBeFocused();
    await page.keyboard.press("Escape");
    await expect(more).toBeFocused();

    await page.keyboard.press("Enter");
    const archive = page.getByRole("menuitem", { name: "归档" });
    await archive.focus();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("alertdialog", { name: "归档“Keyboard review”？" })).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(more).toBeFocused();

    const prompt = page.getByRole("textbox", { name: "Composer prompt" });
    await prompt.fill("Review @sent");
    const mentions = page.getByRole("listbox", { name: "Composer mentions" });
    await expect(mentions).toBeVisible();
    await expect(mentions.getByRole("option").first()).toBeVisible();
    await page.keyboard.press("End");
    expect(await prompt.getAttribute("aria-activedescendant")).toMatch(/^mention-/);
    await page.keyboard.press("Enter");
    await expect(prompt).toBeFocused();

    const toolSidebar = page.getByRole("button", { name: "Toggle workspace tool sidebar" });
    if ((await toolSidebar.getAttribute("aria-pressed")) === "false") await toolSidebar.click();
    const reviewTab = page.getByRole("tab", { name: "审阅", exact: true });
    await reviewTab.focus();
    await expect(reviewTab).toBeFocused();
    await reviewTab.click();
    await expect(page.locator('#workspace-panel-host[data-panel="changes"]')).toBeVisible();

    const bottomPanel = page.getByRole("button", { name: "Toggle workspace bottom panel" });
    if ((await bottomPanel.getAttribute("aria-pressed")) === "false") await bottomPanel.click();
    await expect(bottomPanel).toHaveAttribute("aria-pressed", "true");
    await page.getByRole("button", { name: "新建终端", exact: true }).click();
    await expect(page.getByRole("tab", { name: /Terminal 1/ })).toBeVisible();

    await page.setViewportSize({ width: 760, height: 560 });
    const showThreads = page.getByRole("button", { name: "显示会话侧栏" });
    await expect(showThreads).toBeVisible();
    await showThreads.focus();
    await page.keyboard.press("Enter");
    await page.getByRole("button", { name: "收起会话侧栏" }).focus();
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
