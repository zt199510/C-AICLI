import { expect, test } from "@playwright/test";
import fs from "node:fs";
import path from "node:path";
import {
  cleanupDesktopCase,
  closeDesktop,
  createDesktopCase,
  createThread,
  killAppHost,
  launchDesktop,
  openWorkspace,
  queuePrompt,
  readTurns,
  waitForThreadStatus,
} from "./desktop-harness";

const scenarios = ["success", "deny", "cancel", "crash", "restart", "corrupt-state"] as const;

for (const scenario of scenarios) {
  test(`${scenario} recovery loop uses authoritative packaged boundaries`, async ({ browserName }, testInfo) => {
    if (browserName !== "chromium") throw new Error("Electron tests require Chromium.");
    const packaged = testInfo.project.name === "packaged";
    testInfo.setTimeout(packaged ? 120_000 : scenario === "corrupt-state" || scenario === "restart" ? 90_000 : 60_000);
    const value = createDesktopCase(scenario, packaged);
    let primaryError: unknown = null;
    try {
      let { application, page } = await launchDesktop(value);
      await openWorkspace(page);
      const title = `Week75 ${scenario}`;
      const threadId = await createThread(page, title);

      if (scenario === "success") {
        await queuePrompt(page, "[approval] complete one controlled write");
        await expect(page.getByRole("group", { name: "Approval request" })).toBeVisible();
        await page.getByRole("button", { name: "Approve" }).click();
        await waitForThreadStatus(page, threadId, "completed");
        const turns = await readTurns(page, threadId);
        expect(turns).toHaveLength(1);
        expect(turns[0]?.status).toBe("completed");
      } else if (scenario === "deny") {
        await queuePrompt(page, "[approval] deny this controlled write");
        await expect(page.getByRole("group", { name: "Approval request" })).toBeVisible();
        await page.getByRole("button", { name: "Deny", exact: true }).click();
        await waitForThreadStatus(page, threadId, "failed");
        const turns = await readTurns(page, threadId);
        expect(turns[0]?.stopReason).toBe("approval-denied");
        expect(turns[0]?.approval).toBeNull();
      } else if (scenario === "cancel") {
        await queuePrompt(page, "[pause] wait for explicit cancellation");
        await waitForThreadStatus(page, threadId, "running");
        await page.getByRole("button", { name: "Cancel", exact: true }).click();
        await waitForThreadStatus(page, threadId, "canceled");
        expect((await readTurns(page, threadId))[0]?.stopReason).toBe("canceled");
      } else if (scenario === "crash") {
        await queuePrompt(page, "[pause] crash before a controlled write");
        await waitForThreadStatus(page, threadId, "running");
        await killAppHost(value, application);
        await expect(page.getByRole("heading", { name: "AppHost stopped unexpectedly" })).toBeVisible();
        await expect(page.getByRole("button", { name: "Restart AppHost" })).toBeEnabled();
        await expect(page.getByText("Task completed successfully.")).toHaveCount(0);
        await expect.poll(async () => page.evaluate(async () => (await window.caicli.getRuntimeStatus()).code)).toBe("apphost-exited");
      } else if (scenario === "restart") {
        await queuePrompt(page, "[approval] recover with a fresh approval");
        await expect(page.getByRole("group", { name: "Approval request" })).toBeVisible();
        const original = await readTurns(page, threadId);
        const originalApproval = original[0]?.approval?.requestId;
        expect(originalApproval).toBeTruthy();
        await killAppHost(value, application);
        await expect(page.getByRole("heading", { name: "AppHost stopped unexpectedly" })).toBeVisible();
        await page.getByRole("button", { name: "Restart AppHost" }).click();
        await expect(page.getByText("AppHost ready")).toBeVisible();
        await openWorkspace(page);
        await page.getByText(title, { exact: true }).first().click();
        await expect(page.getByText("This turn needs recovery before it can continue.")).toBeVisible();
        await page.getByRole("button", { name: "Restart", exact: true }).click();
        await expect(page.getByRole("alertdialog", { name: "Restart this turn?" })).toBeVisible();
        await page.getByRole("button", { name: "Restart turn", exact: true }).click();
        await expect.poll(async () => {
          const turns = await readTurns(page, threadId);
          const requestId = turns[1]?.approval?.requestId;
          return requestId && requestId !== originalApproval ? requestId : null;
        }, { timeout: 20_000 }).not.toBeNull();
        const recoveryTurns = await readTurns(page, threadId);
        expect(recoveryTurns).toHaveLength(2);
        expect(recoveryTurns[0]?.status).toBe("failed");
        expect(recoveryTurns[0]?.stopReason).toBe("interrupted");
        expect(recoveryTurns[0]?.approval).toBeNull();
        expect(recoveryTurns[1]?.turnId).not.toBe(recoveryTurns[0]?.turnId);
        expect(recoveryTurns[1]?.approval?.requestId).not.toBe(originalApproval);
        await expect(page.getByRole("group", { name: "Approval request" })).toBeVisible();
        await page.getByRole("button", { name: "Approve" }).click();
        await waitForThreadStatus(page, threadId, "completed");
        expect(await readTurns(page, threadId)).toHaveLength(2);
      } else {
        const corruptThreadId = await createThread(page, `${title} corrupt`);
        await closeDesktop(application);
        const manifest = path.join(value.profile, ".caicli", "threads", corruptThreadId, "thread.json");
        expect(fs.existsSync(manifest)).toBe(true);
        fs.writeFileSync(manifest, "{not-json}", "utf8");
        ({ application, page } = await launchDesktop(value));
        await openWorkspace(page);
        await expect(page.getByText(title, { exact: true }).first()).toBeVisible();
        await expect(page.getByRole("alert")).toContainText("Corrupt state was isolated");
        await expect(page.getByRole("alert")).not.toContainText(value.profile);
        await expect(page.getByRole("alert")).not.toContainText(value.sentinel);
        expect(fs.readFileSync(manifest, "utf8")).toBe("{not-json}");
      }

      const body = await page.locator("body").innerText().catch(() => "");
      expect(body).not.toContain(value.sentinel);
    } catch (error) {
      primaryError = error;
    }
    try {
      await cleanupDesktopCase(value, testInfo);
    } catch (cleanupError) {
      if (primaryError) throw new AggregateError([primaryError, cleanupError], "Scenario and cleanup both failed.", { cause: cleanupError });
      throw cleanupError;
    }
    if (primaryError) throw primaryError;
  });
}
