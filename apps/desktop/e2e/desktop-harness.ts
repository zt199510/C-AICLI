import { _electron as electron, expect, type ElectronApplication, type Page, type TestInfo } from "@playwright/test";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";

const desktopRoot = path.resolve(import.meta.dirname, "..");
const packagedExecutable = path.join(desktopRoot, "out", "C-AICLI Desktop-win32-x64", "caicli-desktop.exe");

export interface DesktopCase {
  readonly id: string;
  readonly root: string;
  readonly workspace: string;
  readonly profile: string;
  readonly appData: string;
  readonly sentinel: string;
  readonly packaged: boolean;
  readonly applications: ElectronApplication[];
  readonly applicationPids: Map<ElectronApplication, number>;
  readonly ownedPids: Set<number>;
}

export function createDesktopCase(id: string, packaged: boolean): DesktopCase {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), `caicli-week75-${id}-`));
  const workspace = path.join(root, "workspace");
  const profile = path.join(root, "profile");
  const appData = path.join(root, "appdata");
  fs.mkdirSync(workspace, { recursive: true });
  fs.mkdirSync(profile, { recursive: true });
  fs.mkdirSync(appData, { recursive: true });
  const sentinel = `week75-${id}-secret-${process.pid}`;
  fs.writeFileSync(path.join(workspace, "sentinel.txt"), sentinel, "utf8");
  return { id, root, workspace, profile, appData, sentinel, packaged, applications: [], applicationPids: new Map(), ownedPids: new Set() };
}

export async function launchDesktop(value: DesktopCase): Promise<{ application: ElectronApplication; page: Page }> {
  if (value.packaged) expect(fs.existsSync(packagedExecutable), "Run npm run package:dir before packaged E2E.").toBe(true);
  const dotnetCandidate = path.join(os.homedir(), ".dotnet", "dotnet.exe");
  const environment = {
    ...process.env,
    APPDATA: value.appData,
    LOCALAPPDATA: path.join(value.root, "localappdata"),
    CAICLI_USER_PROFILE: value.profile,
    CAICLI_DOTNET_HOST: fs.existsSync(dotnetCandidate) ? dotnetCandidate : "dotnet",
    CAICLI_APPHOST_CONFIGURATION: "Debug",
    CAICLI_E2E_CASE: value.id,
  };
  const application = value.packaged
    ? await electron.launch({ executablePath: packagedExecutable, args: ["--disable-gpu"], env: environment })
    : await electron.launch({ args: ["--disable-gpu", desktopRoot], env: environment });
  value.applications.push(application);
  const mainPid = application.process().pid;
  value.applicationPids.set(application, mainPid);
  value.ownedPids.add(mainPid);
  await application.evaluate(async ({ dialog }, workspace) => {
    dialog.showOpenDialog = async () => ({ canceled: false, filePaths: [workspace] });
  }, value.workspace);
  const page = await application.firstWindow();
  await expect(page.getByText("AppHost ready")).toBeVisible({ timeout: value.packaged ? 60_000 : 20_000 });
  return { application, page };
}

export async function openWorkspace(page: Page): Promise<void> {
  await page.getByRole("button", { name: "Open workspace" }).first().click();
  await expect.poll(async () => page.evaluate(async () => {
    const snapshot = await window.caicli.getWorkspaceSnapshot();
    if (!snapshot) return null;
    const listed = await window.caicli.listThreads();
    return listed.succeeded ? snapshot.workspaceId : null;
  }), { timeout: 20_000 }).not.toBeNull();
}

export async function createThread(page: Page, title: string): Promise<string> {
  await page.getByRole("button", { name: "Create thread" }).click();
  await page.getByLabel("Thread title").fill(title);
  await page.getByRole("button", { name: /^Create$/ }).click();
  await expect(page.getByText(title, { exact: true }).first()).toBeVisible();
  return await expect.poll(async () => page.evaluate(async (targetTitle) => {
    const result = await window.caicli.listThreads();
    return result.data?.threads.find((thread) => thread.title === targetTitle)?.threadId ?? null;
  }, title)).not.toBeNull().then(async () => page.evaluate(async (targetTitle) => {
    const result = await window.caicli.listThreads();
    const threadId = result.data?.threads.find((thread) => thread.title === targetTitle)?.threadId;
    if (!threadId) throw new Error("Created thread was not returned by the authoritative list.");
    return threadId;
  }, title));
}

export async function queuePrompt(page: Page, prompt: string): Promise<void> {
  await page.getByRole("textbox", { name: "Composer prompt" }).fill(prompt);
  await page.getByRole("button", { name: "Queue prompt" }).click();
}

export async function waitForThreadStatus(page: Page, threadId: string, status: string): Promise<void> {
  await expect.poll(async () => page.evaluate(async ({ id }) => {
    const result = await window.caicli.getThread({ threadId: id, afterSequence: 0 });
    return result.data?.thread.status ?? null;
  }, { id: threadId }), { timeout: 40_000 }).toBe(status);
}

export async function readTurns(page: Page, threadId: string) {
  return await page.evaluate(async ({ id }) => {
    const result = await window.caicli.getThread({ threadId: id, afterSequence: 0 });
    if (!result.succeeded || !result.data) throw new Error(`[${result.error?.code ?? "unknown"}] ${result.error?.safeMessage ?? "Thread unavailable."}`);
    return result.data.turns;
  }, { id: threadId });
}

export async function killAppHost(value: DesktopCase, application: ElectronApplication): Promise<number> {
  const child = await expect.poll(() => findAppHostChild(application.process().pid), { timeout: 10_000 }).not.toBeNull()
    .then(() => findAppHostChild(application.process().pid));
  if (!child) throw new Error("Owned AppHost process was not found.");
  value.ownedPids.add(child.pid);
  process.kill(child.pid);
  await expect.poll(() => isProcessAlive(child.pid), { timeout: 10_000 }).toBe(false);
  return child.pid;
}

export async function closeDesktop(application: ElectronApplication): Promise<void> {
  const mainPid = application.process().pid;
  let quitError: unknown = null;
  try {
    // Playwright's Electron close follows the normal window/app lifecycle and
    // also closes its control transport, avoiding a browser-process evaluate
    // request remaining in flight while before-quit drains AppHost.
    await application.close();
  } catch (error) {
    quitError = error;
  }
  const exited = await waitForProcessExit(mainPid, 10_000);
  if (!exited) {
    try { process.kill(mainPid); } catch { /* already exited */ }
    await waitForProcessExit(mainPid, 2_000);
    throw new Error(`Desktop process ${mainPid} exceeded the graceful shutdown bound.`);
  }
  if (quitError) throw quitError;
}

export async function cleanupDesktopCase(value: DesktopCase, testInfo: TestInfo): Promise<void> {
  const failures: string[] = [];
  for (const application of [...value.applications].reverse()) {
    const mainPid = value.applicationPids.get(application);
    if (mainPid !== undefined && isProcessAlive(mainPid)) {
      for (const pid of findDescendantPids(mainPid)) value.ownedPids.add(pid);
      try { await closeDesktop(application); } catch (error) { failures.push(String(error)); }
    }
  }
  for (const pid of value.ownedPids) {
    if (isProcessAlive(pid) && !await waitForProcessExit(pid, 2_000)) failures.push(`owned process ${pid} remained alive`);
  }
  const inventory = {
    caseId: value.id,
    packaged: value.packaged,
    ownedPids: [...value.ownedPids],
    processDelta: failures.filter((failure) => failure.includes("process")),
    tempRootReleased: false,
  };
  if (!isOwnedTempRoot(value.root)) failures.push("test root ownership check failed");
  else {
    try { fs.rmSync(value.root, { recursive: true, force: true }); } catch (error) { failures.push(String(error)); }
    inventory.tempRootReleased = !fs.existsSync(value.root);
    if (!inventory.tempRootReleased) failures.push("test root remained after cleanup");
  }
  await testInfo.attach("week75-inventory.json", {
    body: Buffer.from(JSON.stringify(inventory, null, 2)),
    contentType: "application/json",
  });
  expect(failures, failures.join("; ")).toEqual([]);
}

interface ProcessRow { readonly pid: number; readonly name: string; readonly commandLine: string }

function findAppHostChild(parentPid: number): ProcessRow | null {
  const script = `$rootPid=${parentPid}; $all=@(Get-CimInstance Win32_Process); $byId=@{}; foreach($item in $all){$byId[[int]$item.ProcessId]=$item}; $match=$null; foreach($item in $all){if($item.Name -notlike 'CSharpAiCli.AppHost*' -and $item.CommandLine -notlike '*CSharpAiCli.AppHost*'){continue}; $cursor=$item; for($depth=0;$depth -lt 12 -and $null -ne $cursor;$depth++){if([int]$cursor.ParentProcessId -eq $rootPid){$match=$item; break}; $cursor=$byId[[int]$cursor.ParentProcessId]}; if($null -ne $match){break}}; $match | Select-Object @{n='pid';e={[int]$_.ProcessId}},@{n='name';e={$_.Name}},@{n='commandLine';e={$_.CommandLine}} | ConvertTo-Json -Compress`;
  const result = spawnSync("powershell.exe", ["-NoProfile", "-Command", script], { encoding: "utf8", windowsHide: true });
  if (result.status !== 0 || !result.stdout.trim()) return null;
  return JSON.parse(result.stdout) as ProcessRow;
}

function findDescendantPids(parentPid: number): number[] {
  const script = `$rootPid=${parentPid}; $all=@(Get-CimInstance Win32_Process); $byId=@{}; foreach($item in $all){$byId[[int]$item.ProcessId]=$item}; $owned=@(); foreach($item in $all){$cursor=$item; for($depth=0;$depth -lt 12 -and $null -ne $cursor;$depth++){if([int]$cursor.ParentProcessId -eq $rootPid){$owned += [int]$item.ProcessId; break}; $cursor=$byId[[int]$cursor.ParentProcessId]}}; @($owned) | ConvertTo-Json -Compress`;
  const result = spawnSync("powershell.exe", ["-NoProfile", "-Command", script], { encoding: "utf8", windowsHide: true });
  if (result.status !== 0 || !result.stdout.trim()) return [];
  const parsed: unknown = JSON.parse(result.stdout);
  return Array.isArray(parsed) ? parsed.filter((value): value is number => Number.isSafeInteger(value)) : typeof parsed === "number" ? [parsed] : [];
}

function isProcessAlive(pid: number): boolean {
  try { process.kill(pid, 0); return true; } catch { return false; }
}

async function waitForProcessExit(pid: number, timeoutMs: number): Promise<boolean> {
  const deadline = Date.now() + timeoutMs;
  while (isProcessAlive(pid) && Date.now() < deadline) {
    await new Promise((resolve) => setTimeout(resolve, 50));
  }
  return !isProcessAlive(pid);
}

function isOwnedTempRoot(root: string): boolean {
  const resolved = path.resolve(root);
  const temp = path.resolve(os.tmpdir()) + path.sep;
  return resolved.startsWith(temp) && path.basename(resolved).startsWith("caicli-week75-");
}
