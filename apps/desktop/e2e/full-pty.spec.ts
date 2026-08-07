import { expect, test, type Page } from "@playwright/test";
import {
  cleanupDesktopCase,
  createDesktopCase,
  createThread,
  launchDesktop,
  openWorkspace,
} from "./desktop-harness";

test("real AppHost provides independent interactive ConPTY sessions", async ({ browserName }, testInfo) => {
  if (browserName !== "chromium") throw new Error("Electron tests require Chromium.");
  testInfo.setTimeout(90_000);
  const desktop = createDesktopCase("full-pty", false);

  try {
    const { page } = await launchDesktop(desktop);
    await openWorkspace(page);
    await createThread(page, "Full PTY verification");

    const profiles = await page.evaluate(async () => {
      const result = await window.caicli.listTerminalProfiles();
      if (!result.succeeded || !result.data) throw new Error(result.error?.safeMessage ?? "Profiles unavailable.");
      return result.data.profiles;
    });
    expect(profiles.some((profile) => profile.profileId === "system-default" && profile.isDefault)).toBe(true);
    expect(profiles.some((profile) => profile.profileId === "cmd")).toBe(true);

    const bottomPanel = page.getByRole("button", { name: "Toggle workspace bottom panel" });
    if ((await bottomPanel.getAttribute("aria-pressed")) === "false") await bottomPanel.click();
    await expect(bottomPanel).toHaveAttribute("aria-pressed", "true");
    await page.getByRole("button", { name: "新建终端" }).click();
    const firstTab = page.getByRole("tab", { name: /Terminal 1/ });
    await expect(firstTab).toHaveAttribute("aria-selected", "true");
    const firstSessionId = await activeSessionId(page);

    const input = page.getByLabel("Terminal input");
    await input.click();
    await input.pressSequentially("echo pty-keyboard-sentinel");
    await input.press("Enter");
    await expect.poll(() => terminalOutput(page, firstSessionId), { timeout: 15_000 }).toContain("pty-keyboard-sentinel");

    await page.evaluate(async ({ sessionId }) => {
      const result = await window.caicli.resizeTerminal({ sessionId, cols: 100, rows: 35, clientMutationId: "e2e-resize-100x35" });
      if (!result.succeeded) throw new Error(result.error?.safeMessage ?? "Resize failed.");
    }, { sessionId: firstSessionId });

    await page.getByRole("button", { name: "新建终端" }).click();
    const secondTab = page.getByRole("tab", { name: /Terminal 2/ });
    await expect(secondTab).toHaveAttribute("aria-selected", "true");
    const secondSessionId = await activeSessionId(page);
    expect(secondSessionId).not.toBe(firstSessionId);
    await sendTerminal(page, secondSessionId, "echo pty-second-session\r", "e2e-second-session");
    await expect.poll(() => terminalOutput(page, secondSessionId), { timeout: 15_000 }).toContain("pty-second-session");

    await firstTab.click();
    await sendTerminal(page, firstSessionId, "ping -t 127.0.0.1\r", "e2e-long-running");
    await expect.poll(() => terminalOutput(page, firstSessionId), { timeout: 15_000 }).toContain("TTL=");
    await page.getByRole("button", { name: /Ctrl\+C/ }).click();
    await page.evaluate(async ({ sessionId }) => {
      const result = await window.caicli.cancelTerminal({ sessionId, clientMutationId: "e2e-interrupt-barrier" });
      if (!result.succeeded) throw new Error(result.error?.safeMessage ?? "Interrupt failed.");
    }, { sessionId: firstSessionId });
    await expect.poll(() => terminalOutput(page, firstSessionId), { timeout: 15_000 }).toMatch(/PS [^\r\n]*> $/);
    await sendTerminal(page, firstSessionId, "echo pty-after-interrupt\r", "e2e-after-interrupt");
    await expect.poll(() => terminalOutput(page, firstSessionId), { timeout: 15_000 }).toContain("pty-after-interrupt");

    await secondTab.click();
    await page.getByRole("button", { name: "关闭", exact: true }).click();
    await expect(secondTab).toHaveCount(0);
    await expect(firstTab).toBeVisible();
    await expect.poll(async () => (await terminalState(page, firstSessionId)).status).toBe("running");
  } finally {
    await cleanupDesktopCase(desktop, testInfo);
  }
});

async function activeSessionId(page: Page): Promise<string> {
  const id = await page.locator(".terminal-session-host").getAttribute("id");
  if (!id?.startsWith("terminal-session-")) throw new Error("Active terminal session host is missing.");
  return id.slice("terminal-session-".length);
}

async function terminalState(page: Page, sessionId: string) {
  return await page.evaluate(async ({ id }) => {
    const result = await window.caicli.getTerminal({ sessionId: id, afterCursor: 0 });
    if (!result.succeeded || !result.data) throw new Error(result.error?.safeMessage ?? "Terminal state unavailable.");
    return result.data;
  }, { id: sessionId });
}

async function terminalOutput(page: Page, sessionId: string): Promise<string> {
  return (await terminalState(page, sessionId)).output;
}

async function sendTerminal(page: Page, sessionId: string, text: string, mutationId: string): Promise<void> {
  await page.evaluate(async ({ id, value, mutation }) => {
    const result = await window.caicli.inputTerminal({ sessionId: id, text: value, clientMutationId: mutation });
    if (!result.succeeded) throw new Error(result.error?.safeMessage ?? "Terminal input failed.");
  }, { id: sessionId, value: text, mutation: mutationId });
}
