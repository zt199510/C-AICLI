import { _electron as electron, expect, test } from "@playwright/test";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { assertFixtureProjection } from "./fixtures";

const desktopRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

test("read-only thread timeline review survives renderer reload", async ({ browserName }, testInfo) => {
  if (browserName !== "chromium") throw new Error("Electron tests require Chromium.");
  const packaged = testInfo.project.name === "packaged";
  if (packaged) testInfo.setTimeout(60_000);
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "caicli-week75-read-only-"));
  const packageRoot = process.env.CAICLI_DESKTOP_PACKAGE_ROOT
    ? path.resolve(process.env.CAICLI_DESKTOP_PACKAGE_ROOT)
    : path.join(desktopRoot, "out", "C-AICLI Desktop-win32-x64");
  const executablePath = path.join(packageRoot, "caicli-desktop.exe");
  const environment = { ...process.env, APPDATA: path.join(root, "appdata"), CAICLI_USER_PROFILE: path.join(root, "profile"), CAICLI_E2E_ROOT: root };
  const application = packaged
    ? await electron.launch({ executablePath, args: ["--disable-gpu"], env: environment })
    : await electron.launch({ args: ["--disable-gpu", path.join(desktopRoot, "e2e", "fixture-main.cjs")], env: environment });
  try {
    const page = await application.firstWindow();
    await expect(page.getByText("AppHost ready")).toBeVisible();
    if (packaged) {
      await expect(page.getByRole("heading", { name: "Open a workspace" })).toBeVisible();
      await page.reload();
      await expect(page.getByText("AppHost ready")).toBeVisible();
      return;
    }
    const projection = await page.evaluate(async () => ({
      list: await window.caicli.listThreads(),
      detail: await window.caicli.getThread({ threadId: "fixture-thread", afterSequence: 0 }),
    }));
    assertFixtureProjection(projection);
    const fixtureThread = page.locator("#threads-panel").getByText("Fixture review thread", { exact: true });
    await expect(fixtureThread).toBeVisible();
    await fixtureThread.click();
    await page.getByRole("button", { name: /Browse 1 turns/ }).click();
    await page.getByRole("button", { name: /Turn 1/ }).click();
    await expect(page.getByText("User message")).toBeVisible();
    await expect(page.locator(".timeline-card")).toHaveCount(14);
    await page.getByRole("button", { name: "Attach workspace file" }).click();
    await expect(page.getByLabel("Selected composer context").getByText("src/review.ts")).toBeVisible();
    await page.getByRole("textbox", { name: "Composer prompt" }).fill("Explain fixture @fixture");
    await expect(page.getByRole("listbox", { name: "Composer mentions" })).toBeVisible();
    await page.getByRole("option", { name: /Fixture skills/i }).click();
    await page.getByRole("button", { name: "Send prompt" }).click();
    await expect(page.getByText("Ready to run")).toBeVisible();
    await page.reload();
    await page.locator(".thread-select").click({ force: true });
    await expect(page.getByText("Ready to run")).toBeVisible();
    await page.getByRole("button", { name: "Clear pending input" }).click();
    await expect(page.getByText("Ready to run")).toHaveCount(0);
    await page.getByRole("button", { name: "Open terminal" }).click();
    await page.getByRole("textbox", { name: "Terminal input" }).fill("echo terminal-user-sentinel");
    await page.getByRole("button", { name: "Send" }).click();
    await expect(page.getByText("terminal-user-sentinel")).toBeVisible();
    await page.getByRole("button", { name: "Cancel process" }).click();
    await page.getByRole("button", { name: "Close terminal" }).click();
    await page.getByRole("tab", { name: "Artifacts" }).click();
    await page.getByRole("button", { name: /gerber-preview/i }).click();
    await expect(page.getByText("artifacts/preview.gbr")).toBeVisible();
    await expect(page.locator(".artifact-detail").getByText(/C:\\fixture\\workspace/i)).toHaveCount(0);
    await page.getByRole("tab", { name: "Gerber" }).click();
    await expect(page.getByText(/do not prove manufacturing or image correctness/i)).toBeVisible();
    await page.getByRole("button", { name: "Load verification" }).click();
    await expect(page.getByText("Not passed")).toBeVisible();
    await expect(page.getByRole("button", { name: "Accept verified run" })).toBeDisabled();
    await page.reload();
    await expect(page.getByText("Fixture review thread")).toBeVisible();
  } finally {
    await application.close();
    fs.rmSync(root, { recursive: true, force: true });
    expect(fs.existsSync(root), "Read-only E2E temp root must be released.").toBe(false);
  }
});
