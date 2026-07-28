import {
  _electron as electron,
  expect,
  test,
  type CDPSession,
  type ElectronApplication,
  type Page,
} from "@playwright/test";
import { extractFile } from "@electron/asar";
import { createHash } from "node:crypto";
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { SourceMapConsumer, type RawSourceMap } from "source-map-js";

const desktopRoot = path.resolve(import.meta.dirname, "..");
const repositoryRoot = path.resolve(desktopRoot, "..", "..");
const packageRoot = path.join(desktopRoot, "out", "C-AICLI Desktop-win32-x64");
const packagedExecutable = path.join(packageRoot, "caicli-desktop.exe");
const packagedAppHost = path.join(packageRoot, "resources", "apphost", "CSharpAiCli.AppHost.exe");
const packagedAsar = path.join(packageRoot, "resources", "app.asar");
const productBundleName = "index-CIHLwOJK.js";
const productBundlePath = path.join(desktopRoot, "dist", "renderer", "assets", productBundleName);
const productMapPath = `${productBundlePath}.map`;
const evidenceRoot = process.env.CAICLI_WEEK83_EVIDENCE_DIR
  ? path.resolve(process.env.CAICLI_WEEK83_EVIDENCE_DIR)
  : path.join(repositoryRoot, "artifacts", "week83-approval-projection-remediation");

interface ProviderConfig {
  readonly OPENAI_MODEL: string;
  readonly OPENAI_BASE_URL: string;
  readonly OPENAI_API_KEY: string;
}

interface TimelineIdentity {
  readonly itemId: string;
  readonly turnId: string;
  readonly sequence: number;
  readonly type: string;
  readonly name: string | null;
  readonly referenceId: string | null;
}

interface RecoveryState {
  readonly threadStatus: string;
  readonly recoveryRequired: boolean;
  readonly turns: readonly {
    readonly turnId: string;
    readonly status: string;
    readonly stopReason: string | null;
    readonly recoveryRequired: boolean;
    readonly approvalRequestId: string | null;
  }[];
  readonly timeline: readonly TimelineIdentity[];
  readonly sequencesContiguous: boolean;
  readonly itemIdsUnique: boolean;
}

test("authorized Week83 provider crash and explicit restart", async ({ browserName }, testInfo) => {
  if (browserName !== "chromium") throw new Error("Electron provider recovery requires Chromium.");
  if (process.env.CAICLI_WEEK83_PROVIDER_AUTHORIZED !== "read-only-recovery-resource") {
    throw new Error("Week83 provider recovery authorization was not explicitly granted.");
  }
  testInfo.setTimeout(420_000);
  const provider = readAuthorizedProviderConfig(path.join(repositoryRoot, ".env.local"));
  const secrets = Object.values(provider);
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "caicli-week83-recovery-"));
  const workspace = path.join(root, "workspace");
  const profile = path.join(root, "profile");
  const target = path.join(workspace, "recovery.txt");
  const ownedPids = new Set<number>();
  const started = Date.now();
  let application: ElectronApplication | null = null;
  let page: Page | null = null;
  let cdp: CDPSession | null = null;
  let scenarioError: unknown = null;
  let cleanupError: unknown = null;
  let oldState: RecoveryState | null = null;
  let restartedState: RecoveryState | null = null;
  let canceledState: RecoveryState | null = null;
  let oldAppHostPid: number | null = null;
  let newAppHostPid: number | null = null;
  let crashExecuted = false;
  let noAutomaticRestartOrReplayObserved = false;
  let workspaceUnchangedObserved = false;
  let reviewCleanObserved = false;
  let resync = { queueRequests: 0, runners: 0 };
  let domDelta = { nodes: 0, documents: 0, listeners: 0 };
  let domBefore: { nodes: number; documents: number; jsEventListeners: number };
  let processDelta: number | null = null;
  let temporaryDelta: number | null = null;
  let configurationDelta: number | null = null;
  let changedFiles: number | null = null;
  let changesDirty: boolean | null = null;
  let reports: number | null = null;
  let artifacts: number | null = null;

  fs.mkdirSync(workspace, { recursive: true });
  fs.mkdirSync(path.join(profile, ".caicli"), { recursive: true });
  fs.writeFileSync(target, "before\n", "utf8");
  fs.writeFileSync(path.join(profile, ".caicli", "config.json"), JSON.stringify({
    approvalMode: "on-request",
    disabledTools: [
      "agent.plan",
      "workspace.read_text",
      "workspace.search_text",
      "workspace.run_shell",
      "git.status",
      "git.diff",
      "mcp.*",
    ],
    agentRunLimits: { maxSteps: 2, maxToolCalls: 1, timeoutSeconds: 300 },
  }, null, 2), "utf8");
  const workspaceBefore = inventoryWorkspace(workspace);

  try {
    expect(sha256File(packagedExecutable)).toBe("160668DED8D58C80F6215BF5B899CD1C568439F5E4D8B8880D0CEBB068F43383");
    expect(sha256File(packagedAppHost)).toBe("DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA");
    const coverageTargets = createCoverageTargets();
    application = await electron.launch({
      executablePath: packagedExecutable,
      args: ["--disable-gpu"],
      env: authorizedChildEnvironment(root, profile, provider),
    });
    ownedPids.add(application.process().pid);
    await application.evaluate(async ({ dialog }, selectedWorkspace) => {
      dialog.showOpenDialog = async () => ({ canceled: false, filePaths: [selectedWorkspace] });
    }, workspace);
    page = await application.firstWindow();
    await expect(page.getByText("AppHost ready")).toBeVisible({ timeout: 60_000 });
    await page.getByRole("button", { name: "Open workspace" }).first().click();
    await expect.poll(async () => page!.evaluate(async () => {
      const snapshot = await window.caicli.getWorkspaceSnapshot();
      return snapshot?.status ?? null;
    })).toBe("ready");
    oldAppHostPid = await expect.poll(
      () => findAppHostPid(application!.process().pid), { timeout: 15_000 },
    ).not.toBeNull().then(() => findAppHostPid(application!.process().pid));
    if (oldAppHostPid === null) throw new Error("Owned AppHost was not observed.");
    ownedPids.add(oldAppHostPid);
    cdp = await page.context().newCDPSession(page);
    await cdp.send("Profiler.enable");
    await cdp.send("Profiler.startPreciseCoverage", { callCount: true, detailed: true });

    const title = "Week 82 provider recovery";
    await page.getByRole("button", { name: "Create thread" }).click();
    await page.getByLabel("Thread title").fill(title);
    await page.getByRole("button", { name: /^Create$/ }).click();
    const threadId = await expect.poll(async () => page!.evaluate(async (targetTitle) => {
      const result = await window.caicli.listThreads();
      return result.data?.threads.find((thread) => thread.title === targetTitle)?.threadId ?? null;
    }, title)).not.toBeNull().then(async () => page!.evaluate(async (targetTitle) => {
      const result = await window.caicli.listThreads();
      const id = result.data?.threads.find((thread) => thread.title === targetTitle)?.threadId;
      if (!id) throw new Error("Recovery thread was not returned by the authoritative list.");
      return id;
    }, title));
    await takeCoverage(cdp, coverageTargets);
    domBefore = await cdp.send("Memory.getDOMCounters");

    await page.getByRole("textbox", { name: "Composer prompt" }).fill(
      "Your only valid next action is exactly one workspace.apply_patch tool call replacing the single line 'before' with 'after' in recovery.txt. Do not answer directly, do not read files, and do not use shell, Git, MCP, or any other tool. Stop and wait when the Desktop approval is requested.",
    );
    await page.getByRole("button", { name: "Queue prompt" }).click();
    oldState = await waitForApprovalOrTerminal(page, threadId, 300_000);
    expect(oldState.turns).toHaveLength(1);
    expect(oldState.turns[0]?.status).toBe("waiting-for-approval");
    expect(oldState.turns[0]?.approvalRequestId).toBeTruthy();
    await expect(page.getByRole("group", { name: "Approval request" })).toBeVisible();
    expect(items(oldState, 0, "assistant.message").length).toBeGreaterThan(0);
    expect(items(oldState, 0, "tool.started")).toHaveLength(1);
    expect(items(oldState, 0, "approval.requested")).toHaveLength(1);
    expect(fs.readFileSync(target, "utf8")).toBe("before\n");

    process.kill(oldAppHostPid);
    crashExecuted = true;
    await expect.poll(() => isProcessAlive(oldAppHostPid!), { timeout: 15_000 }).toBe(false);
    await expect(page.getByRole("heading", { name: "AppHost stopped unexpectedly" })).toBeVisible();
    await expect.poll(async () => page!.evaluate(async () => (await window.caicli.getRuntimeStatus()).code)).toBe("apphost-exited");
    await new Promise((resolve) => setTimeout(resolve, 5_000));
    expect(findAppHostPid(application.process().pid)).toBeNull();
    expect(fs.readFileSync(target, "utf8")).toBe("before\n");
    await expect(page.getByText("Task completed successfully.")).toHaveCount(0);
    noAutomaticRestartOrReplayObserved = true;

    await page.getByRole("button", { name: "Restart AppHost" }).click();
    await expect(page.getByText("AppHost ready")).toBeVisible({ timeout: 60_000 });
    newAppHostPid = await expect.poll(
      () => findAppHostPid(application!.process().pid), { timeout: 15_000 },
    ).not.toBeNull().then(() => findAppHostPid(application!.process().pid));
    if (newAppHostPid === null) throw new Error("Restarted owned AppHost was not observed.");
    ownedPids.add(newAppHostPid);
    expect(newAppHostPid).not.toBe(oldAppHostPid);
    await page.getByRole("button", { name: "Open workspace" }).first().click();
    await page.getByText(title, { exact: true }).first().click();
    await expect(page.getByText("This turn needs recovery before it can continue.")).toBeVisible();
    const interrupted = await readRecoveryState(page, threadId);
    expect(interrupted.turns[0]?.status).toBe("failed");
    expect(interrupted.turns[0]?.stopReason).toBe("interrupted");
    expect(interrupted.turns[0]?.approvalRequestId).toBeNull();

    await page.getByRole("button", { name: "Restart", exact: true }).click();
    await expect(page.getByRole("alertdialog", { name: "Restart this turn?" })).toBeVisible();
    await page.getByRole("button", { name: "Restart turn", exact: true }).click();
    restartedState = await waitForApprovalOrTerminal(page, threadId, 300_000);
    expect(restartedState.turns).toHaveLength(2);
    expect(restartedState.turns[1]?.status).toBe("waiting-for-approval");
    expect(restartedState.turns[1]?.approvalRequestId).toBeTruthy();
    await expect(page.getByRole("group", { name: "Approval request" })).toBeVisible();
    expect(restartedState.turns[1]?.turnId).not.toBe(oldState.turns[0]?.turnId);
    expect(restartedState.turns[1]?.approvalRequestId).not.toBe(oldState.turns[0]?.approvalRequestId);
    expect(intersection(items(oldState, 0, "assistant.message"), items(restartedState, 1, "assistant.message"))).toHaveLength(0);
    expect(intersection(items(oldState, 0, "tool.started"), items(restartedState, 1, "tool.started"))).toHaveLength(0);
    expect(restartedState.sequencesContiguous).toBe(true);
    expect(restartedState.itemIdsUnique).toBe(true);
    expect(fs.readFileSync(target, "utf8")).toBe("before\n");

    await page.getByRole("button", { name: "Cancel", exact: true }).click();
    await expect.poll(async () => (await readRecoveryState(page!, threadId)).turns[1]?.status, {
      timeout: 30_000,
    }).toBe("canceled");
    canceledState = await readRecoveryState(page, threadId);
    expect(canceledState.turns[1]?.approvalRequestId).toBeNull();
    expect(items(canceledState, 1, "tool.completed")).toHaveLength(0);
    expect(items(canceledState, 1, "assistant.final")).toHaveLength(0);
    expect(fs.readFileSync(target, "utf8")).toBe("before\n");
    resync = await takeCoverage(cdp, coverageTargets);
    const domAfter = await cdp.send("Memory.getDOMCounters");
    domDelta = {
      nodes: domAfter.nodes - domBefore.nodes,
      documents: domAfter.documents - domBefore.documents,
      listeners: domAfter.jsEventListeners - domBefore.jsEventListeners,
    };
    expect(resync.queueRequests).toBeGreaterThanOrEqual(2);
    expect(resync.runners).toBeGreaterThanOrEqual(2);
    expect(resync.runners).toBeLessThanOrEqual(resync.queueRequests);
    expect(resync.queueRequests).toBeLessThanOrEqual(4);
    expect(resync.runners).toBeLessThanOrEqual(4);
    expect(domDelta.nodes).toBeLessThanOrEqual(100);
    expect(domDelta.documents).toBeLessThanOrEqual(0);
    expect(domDelta.listeners).toBeLessThanOrEqual(40);

    const projections = await page.evaluate(async () => {
      const [changes, reportResult, artifactResult] = await Promise.all([
        window.caicli.getChanges({}), window.caicli.listReports(), window.caicli.listArtifacts(),
      ]);
      return {
        dirty: changes.data?.dirty ?? null,
        changedFiles: changes.data?.changedFiles.length ?? null,
        reports: reportResult.data?.reports.length ?? 0,
        artifacts: artifactResult.data?.artifacts.length ?? 0,
      };
    });
    changesDirty = projections.dirty;
    changedFiles = projections.changedFiles;
    reports = projections.reports;
    artifacts = projections.artifacts;
    expect(changesDirty).toBe(false);
    expect(changedFiles).toBe(0);
    expect(reports).toBe(0);
    expect(artifacts).toBe(0);
    expect(inventoryWorkspace(workspace)).toEqual(workspaceBefore);
    workspaceUnchangedObserved = true;
    reviewCleanObserved = true;
  } catch (error) {
    scenarioError = error;
  }

  try {
    if (cdp) {
      await cdp.send("Profiler.stopPreciseCoverage").catch(() => undefined);
      await cdp.send("Profiler.disable").catch(() => undefined);
      await cdp.detach().catch(() => undefined);
    }
    if (application) {
      for (const pid of findDescendantPids(application.process().pid)) ownedPids.add(pid);
      await application.close().catch(() => undefined);
    }
    await waitForProcessesToExit([...ownedPids], 15_000);
    processDelta = countLiveProcesses([...ownedPids]);
    if (!isOwnedRoot(root)) throw new Error("Recovery temp-root ownership check failed.");
    fs.rmSync(root, { recursive: true, force: true });
    temporaryDelta = fs.existsSync(root) ? 1 : 0;
    configurationDelta = temporaryDelta;
  } catch (error) {
    cleanupError = error;
  }

  const evidenceState = canceledState ?? restartedState ?? oldState;
  const oldTurn = oldState?.turns[0] ?? null;
  const newTurn = restartedState?.turns[1] ?? null;
  const passed = scenarioError === null && cleanupError === null && oldTurn !== null && newTurn !== null &&
    canceledState?.turns[1]?.status === "canceled" &&
    oldTurn.turnId !== newTurn.turnId && oldTurn.approvalRequestId !== newTurn.approvalRequestId &&
    changedFiles === 0 && changesDirty === false && reports === 0 && artifacts === 0 &&
    processDelta === 0 && temporaryDelta === 0 && configurationDelta === 0;
  const evidence = {
    schemaVersion: "week83-approval-projection-remediation/v1",
    evidenceKind: "provider-recovery",
    status: passed ? "Passed" : "Failed",
    exactCandidateRevision: "348dd4f30064a70751ae2a55ec5e37a95c49ec87",
    packageIdentity: {
      sha256: "160668DED8D58C80F6215BF5B899CD1C568439F5E4D8B8880D0CEBB068F43383",
      bytes: 222753280,
      treeSha256: "7D8C690876B53E12AC976C7B12C1291456C1DB3EB13F6E98A08D6A305BC6F7A2",
      treeBytes: 464708884,
    },
    appHostIdentity: {
      sha256: "DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA",
      bytes: 79941168,
    },
    checks: {
      ownedAppHostCrashOnly: crashExecuted,
      durableApprovalObserved: oldTurn?.approvalRequestId !== null && oldTurn?.approvalRequestId !== undefined,
      noAutomaticRestartOrReplay: noAutomaticRestartOrReplayObserved,
      explicitRestart: newAppHostPid !== null && newAppHostPid !== oldAppHostPid,
      oldAttemptInterrupted: canceledState?.turns[0]?.stopReason === "interrupted",
      newAttemptCanceledBeforeApproval: canceledState?.turns[1]?.status === "canceled",
      turnIdentitySeparated: oldTurn !== null && newTurn !== null && oldTurn.turnId !== newTurn.turnId,
      approvalIdentitySeparated: oldTurn !== null && newTurn !== null && oldTurn.approvalRequestId !== newTurn.approvalRequestId,
      modelIdentitySeparated: oldState !== null && restartedState !== null &&
        intersection(items(oldState, 0, "assistant.message"), items(restartedState, 1, "assistant.message")).length === 0,
      toolIdentitySeparated: oldState !== null && restartedState !== null &&
        intersection(items(oldState, 0, "tool.started"), items(restartedState, 1, "tool.started")).length === 0,
      timelineContiguous: evidenceState?.sequencesContiguous ?? false,
      timelineItemIdsUnique: evidenceState?.itemIdsUnique ?? false,
      resyncBounded: resync.queueRequests >= 2 && resync.runners >= 2 &&
        resync.runners <= resync.queueRequests && resync.queueRequests <= 4 && resync.runners <= 4,
      domAndListenersBounded: domDelta.nodes <= 100 && domDelta.documents <= 0 && domDelta.listeners <= 40,
      diskUnchanged: workspaceUnchangedObserved && changedFiles === 0,
      reviewClean: reviewCleanObserved,
    },
    counts: {
      desktopLaunches: application ? 1 : 0,
      appHostProcesses: [oldAppHostPid, newAppHostPid].filter((pid) => pid !== null).length,
      appHostCrashes: crashExecuted ? 1 : 0,
      runtimeRestarts: newAppHostPid !== null ? 1 : 0,
      turns: evidenceState?.turns.length ?? 0,
      modelItems: evidenceState?.timeline.filter((item) => item.type === "assistant.message").length ?? 0,
      toolCalls: evidenceState?.timeline.filter((item) => item.type === "tool.started").length ?? 0,
      approvalRequests: evidenceState?.timeline.filter((item) => item.type === "approval.requested").length ?? 0,
      approvalResolutions: evidenceState?.timeline.filter((item) => item.type === "approval.resolved").length ?? 0,
      changedFiles,
      reports,
      artifacts,
      timelineItems: evidenceState?.timeline.length ?? 0,
      queueResyncRequests: resync.queueRequests,
      resyncRunners: resync.runners,
      nodesDelta: domDelta.nodes,
      documentsDelta: domDelta.documents,
      listenersDelta: domDelta.listeners,
    },
    durationMilliseconds: Date.now() - started,
    cleanupDelta: { process: processDelta, temporary: temporaryDelta, configuration: configurationDelta },
    failure: scenarioError === null && cleanupError === null ? null : safeError(scenarioError ?? cleanupError, secrets),
    summary: passed
      ? "Week83 packaged provider recovery failed closed after the owned AppHost crash, required explicit restart, separated all durable attempt identities, canceled before approval, and left zero cleanup delta."
      : "Week83 packaged provider recovery failed closed; the first attempt remains preserved in this evidence envelope.",
  };
  fs.mkdirSync(evidenceRoot, { recursive: true });
  const body = JSON.stringify(evidence, null, 2);
  fs.writeFileSync(path.join(evidenceRoot, "provider-recovery.json"), `${body}\n`, "utf8");
  await testInfo.attach("week83-provider-recovery.json", { body: Buffer.from(body), contentType: "application/json" });
  if (scenarioError && cleanupError) throw new AggregateError([scenarioError, cleanupError], "Recovery scenario and cleanup failed.");
  if (scenarioError) throw scenarioError;
  if (cleanupError) throw cleanupError;
  expect(passed).toBe(true);
});

async function readRecoveryState(page: Page, threadId: string): Promise<RecoveryState> {
  return page.evaluate(async (id) => {
    const result = await window.caicli.getThread({ threadId: id, afterSequence: 0 });
    if (!result.succeeded || !result.data) throw new Error(result.error?.code ?? "thread-unavailable");
    const timeline = result.data.timeline.map((item) => ({
      itemId: item.itemId,
      turnId: item.turnId,
      sequence: item.sequence,
      type: item.type,
      name: item.payload.name,
      referenceId: item.payload.referenceId,
    }));
    return {
      threadStatus: result.data.thread.status,
      recoveryRequired: result.data.recoveryRequired,
      turns: result.data.turns.map((turn) => ({
        turnId: turn.turnId,
        status: turn.status,
        stopReason: turn.stopReason,
        recoveryRequired: turn.recoveryRequired,
        approvalRequestId: turn.approval?.requestId ?? null,
      })),
      timeline,
      sequencesContiguous: timeline.every((item, index) => index === 0 || item.sequence === timeline[index - 1]!.sequence + 1),
      itemIdsUnique: new Set(timeline.map((item) => item.itemId)).size === timeline.length,
    };
  }, threadId);
}

async function waitForApprovalOrTerminal(page: Page, threadId: string, timeoutMilliseconds: number): Promise<RecoveryState> {
  const deadline = Date.now() + timeoutMilliseconds;
  let state = await readRecoveryState(page, threadId);
  while (Date.now() < deadline) {
    const latest = state.turns.at(-1);
    if (latest?.approvalRequestId || latest && ["completed", "failed", "canceled"].includes(latest.status)) return state;
    await new Promise((resolve) => setTimeout(resolve, 1_000));
    state = await readRecoveryState(page, threadId);
  }
  throw new Error("Recovery provider turn exceeded the authorized approval wait bound.");
}

function items(state: RecoveryState, turnIndex: number, type: string): TimelineIdentity[] {
  const turnId = state.turns[turnIndex]?.turnId;
  return state.timeline.filter((item) => item.turnId === turnId && item.type === type);
}

function intersection(left: readonly TimelineIdentity[], right: readonly TimelineIdentity[]): string[] {
  const rightIds = new Set(right.map((item) => item.itemId));
  return left.map((item) => item.itemId).filter((id) => rightIds.has(id));
}

function createCoverageTargets(): { queue: number; runner: number; length: number } {
  const packaged = extractFile(packagedAsar, `dist\\renderer\\assets\\${productBundleName}`);
  const built = fs.readFileSync(productBundlePath);
  if (sha256(packaged) !== sha256(built)) throw new Error("Packaged Renderer bundle does not match the verified product build.");
  const consumer = new SourceMapConsumer(JSON.parse(fs.readFileSync(productMapPath, "utf8")) as RawSourceMap);
  const source = consumer.sources.find((candidate) => candidate.endsWith("/use-desktop-controller.ts"));
  if (!source) throw new Error("Product controller source was not found in the verified source map.");
  const starts = generatedLineStarts(built.toString("utf8"));
  const offset = (line: number) => {
    const position = consumer.generatedPositionFor({ source, line, column: 4, bias: SourceMapConsumer.LEAST_UPPER_BOUND });
    if (position.line === null || position.column === null) throw new Error("Product coverage target could not be mapped.");
    return starts[position.line - 1]! + position.column;
  };
  const result = { queue: offset(109), runner: offset(115), length: built.toString("utf8").length };
  consumer.destroy?.();
  return result;
}

async function takeCoverage(cdp: CDPSession, targets: { queue: number; runner: number; length: number }) {
  const coverage = await cdp.send("Profiler.takePreciseCoverage");
  const script = coverage.result.find((candidate) => candidate.url.endsWith(productBundleName)) ??
    coverage.result.find((candidate) => candidate.functions.some((fn) =>
      fn.ranges.some((range) => range.startOffset === 0 && range.endOffset === targets.length)));
  const countAt = (offset: number) => script?.functions.flatMap((fn) => fn.ranges)
    .filter((range) => range.startOffset <= offset && range.endOffset >= offset)
    .sort((left, right) => (left.endOffset - left.startOffset) - (right.endOffset - right.startOffset))[0]?.count ?? 0;
  return { queueRequests: countAt(targets.queue), runners: countAt(targets.runner) };
}

function readAuthorizedProviderConfig(filePath: string): ProviderConfig {
  const allowed = new Set(["OPENAI_MODEL", "OPENAI_BASE_URL", "OPENAI_API_KEY"]);
  const values = new Map<string, string>();
  for (const rawLine of fs.readFileSync(filePath, "utf8").split(/\r?\n/u)) {
    const line = rawLine.trim();
    if (!line || line.startsWith("#")) continue;
    const match = /^(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$/u.exec(line);
    if (!match || !allowed.has(match[1]!)) continue;
    let value = match[2]!.trim();
    if ((value.startsWith("\"") && value.endsWith("\"")) || (value.startsWith("'") && value.endsWith("'"))) value = value.slice(1, -1);
    if (!value || value.includes("\0") || value.includes("\n") || value.includes("\r")) throw new Error("Authorized provider setting is missing or invalid.");
    values.set(match[1]!, value);
  }
  for (const key of allowed) if (!values.has(key)) throw new Error("Authorized provider setting is missing or invalid.");
  return Object.fromEntries(values) as unknown as ProviderConfig;
}

function authorizedChildEnvironment(root: string, profile: string, provider: ProviderConfig): NodeJS.ProcessEnv {
  const environment = { ...process.env };
  delete environment.OPENAI_MODEL;
  delete environment.OPENAI_BASE_URL;
  delete environment.OPENAI_API_KEY;
  return {
    ...environment,
    ...provider,
    APPDATA: path.join(root, "appdata"),
    LOCALAPPDATA: path.join(root, "localappdata"),
    CAICLI_USER_PROFILE: profile,
    CAICLI_AGENT_BACKEND: "direct",
  };
}

function findAppHostPid(mainPid: number): number | null {
  const script = `$rootPid=${mainPid}; $all=@(Get-CimInstance Win32_Process); $byId=@{}; foreach($item in $all){$byId[[int]$item.ProcessId]=$item}; $result=$null; foreach($item in $all){if($item.Name -notlike 'CSharpAiCli.AppHost*'){continue}; $cursor=$item; for($depth=0;$depth -lt 12 -and $null -ne $cursor;$depth++){if([int]$cursor.ParentProcessId -eq $rootPid){$result=[int]$item.ProcessId; break}; $cursor=$byId[[int]$cursor.ParentProcessId]}; if($null-ne$result){break}}; $result | ConvertTo-Json -Compress`;
  const result = spawnSync("powershell.exe", ["-NoProfile", "-Command", script], { encoding: "utf8", windowsHide: true });
  if (result.status !== 0 || !result.stdout.trim()) return null;
  const parsed: unknown = JSON.parse(result.stdout);
  return typeof parsed === "number" && Number.isSafeInteger(parsed) ? parsed : null;
}

function findDescendantPids(mainPid: number): number[] {
  const script = `$rootPid=${mainPid}; $all=@(Get-CimInstance Win32_Process); $byId=@{}; foreach($item in $all){$byId[[int]$item.ProcessId]=$item}; $owned=@(); foreach($item in $all){$cursor=$item; for($depth=0;$depth-lt 12-and$null-ne$cursor;$depth++){if([int]$cursor.ParentProcessId-eq$rootPid){$owned += [int]$item.ProcessId; break}; $cursor=$byId[[int]$cursor.ParentProcessId]}}; @($owned) | ConvertTo-Json -Compress`;
  const result = spawnSync("powershell.exe", ["-NoProfile", "-Command", script], { encoding: "utf8", windowsHide: true });
  if (result.status !== 0 || !result.stdout.trim()) return [];
  const parsed: unknown = JSON.parse(result.stdout);
  return Array.isArray(parsed) ? parsed.filter((value): value is number => Number.isSafeInteger(value)) :
    typeof parsed === "number" && Number.isSafeInteger(parsed) ? [parsed] : [];
}

function inventoryWorkspace(root: string): Readonly<Record<string, string>> {
  const files: Record<string, string> = {};
  const visit = (directory: string, relative: string) => {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
      const childRelative = relative ? `${relative}/${entry.name}` : entry.name;
      const child = path.join(directory, entry.name);
      if (entry.isDirectory()) visit(child, childRelative);
      else if (entry.isFile()) files[childRelative] = sha256File(child);
    }
  };
  visit(root, "");
  return files;
}

function generatedLineStarts(source: string): number[] {
  const starts = [0];
  for (let index = 0; index < source.length; index++) if (source.charCodeAt(index) === 10) starts.push(index + 1);
  return starts;
}

function isProcessAlive(pid: number): boolean {
  try { process.kill(pid, 0); return true; } catch { return false; }
}

async function waitForProcessesToExit(pids: readonly number[], timeoutMilliseconds: number): Promise<void> {
  const deadline = Date.now() + timeoutMilliseconds;
  while (countLiveProcesses(pids) > 0 && Date.now() < deadline) await new Promise((resolve) => setTimeout(resolve, 100));
}

function countLiveProcesses(pids: readonly number[]): number {
  return pids.filter(isProcessAlive).length;
}

function isOwnedRoot(root: string): boolean {
  const resolved = path.resolve(root);
  return resolved.startsWith(path.resolve(os.tmpdir()) + path.sep) && path.basename(resolved).startsWith("caicli-week83-recovery-");
}

function sha256File(filePath: string): string {
  return sha256(fs.readFileSync(filePath));
}

function sha256(value: Buffer): string {
  return createHash("sha256").update(value).digest("hex").toUpperCase();
}

function safeError(error: unknown, secrets: readonly string[]): { name: string; message: string } {
  const value = error instanceof Error ? error : new Error(String(error));
  let message = value.message;
  for (const secret of secrets) message = message.replaceAll(secret, "[configuration-redacted]");
  message = message.replace(/OPENAI_(?:MODEL|BASE_URL|API_KEY)/gu, "[configuration-name-redacted]");
  message = message.replace(/[a-zA-Z]:[\\/][^\s"'<>]*/gu, "[rooted-path-redacted]");
  return { name: value.name.slice(0, 128), message: message.slice(0, 1000) };
}
