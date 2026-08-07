import { _electron as electron, expect, test, type ElectronApplication, type Locator } from "@playwright/test";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const desktopRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

test("the frozen Codex parity states match the conversation, right workbench, and bottom terminal baselines", async () => {
  const runRoot = fs.mkdtempSync(path.join(os.tmpdir(), "caicli-codex-golden-"));
  let application: ElectronApplication | null = null;
  try {
    application = await electron.launch({
      args: ["--disable-gpu", "--in-process-gpu", path.join(desktopRoot, "e2e", "fixture-main.cjs")],
      env: {
        ...process.env,
        APPDATA: path.join(runRoot, "appdata"),
        CAICLI_USER_PROFILE: path.join(runRoot, "profile"),
        CAICLI_E2E_ROOT: runRoot,
        CAICLI_CHAT_UI_FIXTURE: "selected-thread",
      },
    });
    const page = await application.firstWindow();
    await page.setViewportSize({ width: 1024, height: 768 });
    await expect(page.locator(".app-shell")).toHaveAttribute("data-runtime-state", "ready");
    await expect(page.locator('[data-assistant-message-id="fixture-assistant-1"]')).toHaveCount(1);
    await page.evaluate(() => {
      document.documentElement.dataset.theme = "light";
      document.documentElement.style.colorScheme = "light";
    });
    await page.evaluate(async () => { await document.fonts.ready; });
    await expect(page.locator(".app-shell")).toHaveScreenshot("codex-parity-1024x768.png", {
      animations: "disabled",
      caret: "hide",
      maxDiffPixels: 0,
      threshold: 0,
    });

    const summaryToggle = page.getByRole("button", { name: "Toggle workspace summary" });
    const bottomToggle = page.getByRole("button", { name: "Toggle workspace bottom panel" });
    const sidebarToggle = page.getByRole("button", { name: "Toggle workspace tool sidebar" });
    await page.setViewportSize({ width: 1228, height: 904 });
    await setPressed(summaryToggle, false);
    await setPressed(bottomToggle, false);
    await setPressed(sidebarToggle, true);
    await expect(page.getByRole("tab", { name: "审阅", exact: true })).toBeVisible();
    await expect(page.locator(".app-shell")).toHaveScreenshot("codex-right-workbench-1228x904.png", {
      animations: "disabled",
      caret: "hide",
      maxDiffPixels: 0,
      threshold: 0,
    });

    await page.getByRole("button", { name: "添加工作区工具" }).click();
    await expect(page.getByRole("menu", { name: "添加工作区工具" })).toBeVisible();
    await expect(page.locator(".app-shell")).toHaveScreenshot("codex-right-workbench-menu-1228x904.png", {
      animations: "disabled",
      caret: "hide",
      maxDiffPixels: 0,
      threshold: 0,
    });
    await page.keyboard.press("Escape");

    await page.setViewportSize({ width: 1280, height: 810 });
    await setPressed(summaryToggle, false);
    await setPressed(bottomToggle, true);
    await expect(sidebarToggle).toHaveAttribute("aria-pressed", "false");
    await page.getByRole("button", { name: "新建终端", exact: true }).click();
    await expect(page.getByRole("tab", { name: "Terminal 1" })).toBeVisible();
    await expect(page.locator(".app-shell")).toHaveScreenshot("codex-bottom-terminal-1280x810.png", {
      animations: "disabled",
      caret: "hide",
      maxDiffPixels: 0,
      threshold: 0,
    });
  } finally {
    if (application) await application.close().catch(() => application?.process().kill());
    fs.rmSync(runRoot, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
  }
});

async function setPressed(locator: Locator, pressed: boolean) {
  if ((await locator.getAttribute("aria-pressed")) !== String(pressed)) await locator.click();
}
