import { _electron as electron, expect, test, type CDPSession, type ElectronApplication, type Page } from "@playwright/test";
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
const productBundleName = "index-Ctw8Lkem.js";
const productBundlePath = path.join(desktopRoot, "dist", "renderer", "assets", productBundleName);
const productMapPath = `${productBundlePath}.map`;
const evidenceRoot = process.env.CAICLI_WEEK82_EVIDENCE_DIR
  ? path.resolve(process.env.CAICLI_WEEK82_EVIDENCE_DIR)
  : path.join(repositoryRoot, "artifacts", "week82-desktop-preview-requalification");
const exactTestCommand = "dotnet msbuild Week82Gate.proj -target:Test -nologo";

interface ProviderConfig {
  readonly OPENAI_MODEL: string;
  readonly OPENAI_BASE_URL: string;
  readonly OPENAI_API_KEY: string;
}

interface WriteState {
  readonly threadStatus: string;
  readonly turns: readonly {
    readonly turnId: string;
    readonly status: string;
    readonly approval: null | {
      readonly requestId: string;
      readonly risk: string;
      readonly operation: string;
      readonly targetClass: string;
      readonly policyRevision: string;
      readonly safeSummary: string;
    };
  }[];
  readonly timeline: readonly { itemId: string; turnId: string; sequence: number; type: string; name: string | null }[];
  readonly sequencesContiguous: boolean;
  readonly itemIdsUnique: boolean;
}

test("authorized Week82 controlled write uses two durable approvals", async ({ browserName }, testInfo) => {
  if (browserName !== "chromium") throw new Error("Electron provider write requires Chromium.");
  if (process.env.CAICLI_WEEK82_WRITE_AUTHORIZED !== "controlled-write-project-b") {
    throw new Error("Week82 controlled-write authorization was not explicitly granted.");
  }
  testInfo.setTimeout(420_000);
  const provider = readAuthorizedProviderConfig(path.join(repositoryRoot, ".env.local"));
  const secrets = Object.values(provider);
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "caicli-week82-write-"));
  const workspace = path.join(root, "project-b");
  const profile = path.join(root, "profile");
  const target = path.join(workspace, "result.txt");
  const project = path.join(workspace, "Week82Gate.proj");
  const ownedPids = new Set<number>();
  const started = Date.now();
  let application: ElectronApplication | null = null;
  let page: Page | null = null;
  let cdp: CDPSession | null = null;
  let scenarioError: unknown = null;
  let cleanupError: unknown = null;
  let firstApproval: WriteState["turns"][number]["approval"] = null;
  let secondApproval: WriteState["turns"][number]["approval"] = null;
  let finalState: WriteState | null = null;
  let independentExitCode: number | null = null;
  let changedFiles: number | null = null;
  let changedLines: number | null = null;
  let reports: number | null = null;
  let artifacts: number | null = null;
  let processDelta: number | null = null;
  let temporaryDelta: number | null = null;
  let configurationDelta: number | null = null;
  let resync = { queueRequests: 0, runners: 0 };
  let domDelta = { nodes: 0, documents: 0, listeners: 0 };

  fs.mkdirSync(workspace, { recursive: true });
  fs.mkdirSync(path.join(profile, ".caicli"), { recursive: true });
  fs.writeFileSync(target, "fail\n", "utf8");
  fs.writeFileSync(project, [
    "<Project>",
    "  <Target Name=\"Test\">",
    "    <ReadLinesFromFile File=\"result.txt\">",
    "      <Output TaskParameter=\"Lines\" ItemName=\"ResultLines\" />",
    "    </ReadLinesFromFile>",
    "    <Error Condition=\"'@(ResultLines)' != 'pass'\" Text=\"Expected the deterministic pass marker.\" />",
    "    <Message Text=\"Week82 target test passed.\" Importance=\"high\" />",
    "  </Target>",
    "</Project>",
    "",
  ].join("\n"), "utf8");
  fs.writeFileSync(path.join(profile, ".caicli", "config.json"), JSON.stringify({
    approvalMode: "on-request",
    disabledTools: ["agent.plan", "workspace.read_text", "workspace.search_text", "git.status", "git.diff", "mcp.*"],
    agentRunLimits: { maxSteps: 5, maxToolCalls: 2, timeoutSeconds: 300 },
  }, null, 2), "utf8");
  runChecked("git", ["init", "--quiet"], workspace);
  runChecked("git", ["config", "user.name", "Week82 Harness"], workspace);
  runChecked("git", ["config", "user.email", "week82@example.invalid"], workspace);
  runChecked("git", ["add", "--", "result.txt", "Week82Gate.proj"], workspace);
  runChecked("git", ["commit", "--quiet", "-m", "baseline"], workspace);
  const dotnet = dotnetCommand();
  const baseline = spawnSync(dotnet, ["msbuild", "Week82Gate.proj", "-target:Test", "-nologo"], {
    cwd: workspace, encoding: "utf8", windowsHide: true,
  });
  const baselineExitCode = baseline.status;
  expect(baselineExitCode, "Project B baseline must fail deterministically.").not.toBe(0);
  const projectHash = sha256File(project);

  try {
    expect(sha256File(packagedExecutable)).toBe("C736C48B23B8971ED5DAD7F53EBF7BE6CE5CDC2BA6B24CC2CCAFE3DD9064CBB0");
    expect(sha256File(packagedAppHost)).toBe("DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA");
    application = await electron.launch({
      executablePath: packagedExecutable,
      args: ["--disable-gpu"],
      env: authorizedChildEnvironment(root, profile, provider, dotnet),
    });
    ownedPids.add(application.process().pid);
    await application.evaluate(async ({ dialog }, selectedWorkspace) => {
      dialog.showOpenDialog = async () => ({ canceled: false, filePaths: [selectedWorkspace] });
    }, workspace);
    page = await application.firstWindow();
    await expect(page.getByText("AppHost ready")).toBeVisible({ timeout: 60_000 });
    await page.getByRole("button", { name: "Open workspace" }).first().click();
    const appHostPid = await expect.poll(() => findAppHostPid(application!.process().pid), { timeout: 15_000 }).not.toBeNull()
      .then(() => findAppHostPid(application!.process().pid));
    if (appHostPid === null) throw new Error("Owned AppHost was not observed.");
    ownedPids.add(appHostPid);
    cdp = await page.context().newCDPSession(page);
    await cdp.send("Profiler.enable");
    await cdp.send("Profiler.startPreciseCoverage", { callCount: true, detailed: true });
    const coverageTargets = createCoverageTargets();

    const title = "Week 82 controlled write";
    await page.getByRole("button", { name: "Create thread" }).click();
    await page.getByLabel("Thread title").fill(title);
    await page.getByRole("button", { name: /^Create$/ }).click();
    const threadId = await expect.poll(async () => page!.evaluate(async (targetTitle) => {
      const result = await window.caicli.listThreads();
      return result.data?.threads.find((thread) => thread.title === targetTitle)?.threadId ?? null;
    }, title)).not.toBeNull().then(async () => page!.evaluate(async (targetTitle) => {
      const result = await window.caicli.listThreads();
      const id = result.data?.threads.find((thread) => thread.title === targetTitle)?.threadId;
      if (!id) throw new Error("Controlled-write thread was not returned by the authoritative list.");
      return id;
    }, title));
    await takeCoverage(cdp, coverageTargets);
    const domBefore = await cdp.send("Memory.getDOMCounters");

    await page.getByRole("textbox", { name: "Composer prompt" }).fill(
      `Project B has a deterministic failing baseline. Use exactly one workspace.apply_patch call to replace the only line 'fail' with 'pass' in result.txt. Then use exactly one workspace.run_shell call with the exact command: ${exactTestCommand}. Do not modify Week82Gate.proj. Do not use any other file, tool, command, or network behavior.`,
    );
    await page.getByRole("button", { name: "Queue prompt" }).click();
    await expect(page.getByRole("group", { name: "Approval request" })).toBeVisible({ timeout: 300_000 });
    let state = await readWriteState(page, threadId);
    firstApproval = state.turns[0]?.approval ?? null;
    expect(firstApproval?.risk).toBe("write");
    expect(firstApproval?.targetClass).toBe("workspace-file");
    expect(fs.readFileSync(target, "utf8")).toBe("fail\n");
    await page.getByRole("button", { name: "Approve" }).click();

    await expect.poll(async () => {
      const value = await readWriteState(page!, threadId);
      return value.turns[0]?.approval?.risk === "shell" ? value.turns[0].approval.requestId : null;
    }, { timeout: 300_000 }).not.toBeNull();
    state = await readWriteState(page, threadId);
    secondApproval = state.turns[0]?.approval ?? null;
    expect(secondApproval?.risk).toBe("shell");
    expect(secondApproval?.targetClass).toBe("workspace-directory");
    expect(secondApproval?.requestId).not.toBe(firstApproval?.requestId);
    expect(secondApproval?.policyRevision).toBe(firstApproval?.policyRevision);
    expect(fs.readFileSync(target, "utf8")).toBe("pass\n");
    expect(sha256File(project)).toBe(projectHash);
    await page.getByRole("button", { name: "Approve" }).click();

    await expect.poll(async () => (await readWriteState(page!, threadId)).turns[0]?.status, { timeout: 300_000 }).toBe("completed");
    finalState = await readWriteState(page, threadId);
    expect(finalState.sequencesContiguous).toBe(true);
    expect(finalState.itemIdsUnique).toBe(true);
    expect(finalState.timeline.filter((item) => item.type === "tool.started" && item.name === "workspace.apply_patch")).toHaveLength(1);
    expect(finalState.timeline.filter((item) => item.type === "tool.completed" && item.name === "workspace.apply_patch")).toHaveLength(1);
    expect(finalState.timeline.filter((item) => item.type === "command.started" && item.name === "workspace.run_shell")).toHaveLength(1);
    expect(finalState.timeline.filter((item) => item.type === "command.completed" && item.name === "workspace.run_shell")).toHaveLength(1);
    expect(finalState.timeline.filter((item) => item.type === "approval.requested")).toHaveLength(2);
    expect(finalState.timeline.filter((item) => item.type === "approval.resolved")).toHaveLength(2);
    expect(finalState.timeline.filter((item) => item.type === "assistant.final")).toHaveLength(1);
    resync = await takeCoverage(cdp, coverageTargets);
    const domAfter = await cdp.send("Memory.getDOMCounters");
    domDelta = {
      nodes: domAfter.nodes - domBefore.nodes,
      documents: domAfter.documents - domBefore.documents,
      listeners: domAfter.jsEventListeners - domBefore.jsEventListeners,
    };
    expect(resync.queueRequests).toBeGreaterThanOrEqual(1);
    expect(resync.queueRequests).toBeLessThanOrEqual(2);
    expect(resync.runners).toBeGreaterThanOrEqual(1);
    expect(resync.runners).toBeLessThanOrEqual(2);
    expect(domDelta.nodes).toBeLessThanOrEqual(50);
    expect(domDelta.documents).toBeLessThanOrEqual(0);
    expect(domDelta.listeners).toBeLessThanOrEqual(20);

    const independent = spawnSync(dotnet, ["msbuild", "Week82Gate.proj", "-target:Test", "-nologo"], {
      cwd: workspace, encoding: "utf8", windowsHide: true,
    });
    independentExitCode = independent.status;
    expect(independentExitCode).toBe(0);
    const numstat = spawnSync("git", ["diff", "--numstat", "--", "result.txt", "Week82Gate.proj"], {
      cwd: workspace, encoding: "utf8", windowsHide: true,
    });
    expect(numstat.status).toBe(0);
    const rows = numstat.stdout.trim().split(/\r?\n/u).filter(Boolean);
    expect(rows).toHaveLength(1);
    const columns = rows[0]!.split("\t");
    changedLines = Number(columns[0]) + Number(columns[1]);
    expect(changedLines).toBeLessThanOrEqual(160);
    expect(columns[2]).toBe("result.txt");
    const projections = await page.evaluate(async () => {
      const [changes, reportResult, artifactResult] = await Promise.all([
        window.caicli.getChanges({}), window.caicli.listReports(), window.caicli.listArtifacts(),
      ]);
      return {
        changedFiles: changes.data?.changedFiles.length ?? null,
        reports: reportResult.data?.reports.length ?? 0,
        artifacts: artifactResult.data?.artifacts.length ?? 0,
      };
    });
    changedFiles = projections.changedFiles;
    reports = projections.reports;
    artifacts = projections.artifacts;
    expect(changedFiles).toBe(1);
    expect(reports).toBe(0);
    expect(artifacts).toBe(0);
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
    if (!isOwnedRoot(root)) throw new Error("Controlled-write temp-root ownership check failed.");
    fs.rmSync(root, { recursive: true, force: true });
    temporaryDelta = fs.existsSync(root) ? 1 : 0;
    configurationDelta = temporaryDelta;
  } catch (error) {
    cleanupError = error;
  }

  const timeline = finalState?.timeline ?? [];
  const passed = scenarioError === null && cleanupError === null && baselineExitCode !== 0 && independentExitCode === 0 &&
    firstApproval?.risk === "write" && secondApproval?.risk === "shell" && firstApproval.requestId !== secondApproval.requestId &&
    changedFiles === 1 && changedLines !== null && changedLines <= 160 && reports === 0 && artifacts === 0 &&
    processDelta === 0 && temporaryDelta === 0 && configurationDelta === 0;
  const evidence = {
    schemaVersion: "week82-desktop-preview-requalification/v1",
    evidenceKind: "provider-write",
    status: passed ? "Passed" : "Failed",
    exactCandidateRevision: "e9e062d985545377aa373767f563de6a2bb30a64",
    packageIdentity: {
      sha256: "C736C48B23B8971ED5DAD7F53EBF7BE6CE5CDC2BA6B24CC2CCAFE3DD9064CBB0",
      bytes: 222753280,
      treeSha256: "ADDFBC3114B10E31633F1E9A9500934B9B8F17BCD3222CD4D02217AA606C6E38",
      treeBytes: 464708708,
    },
    appHostIdentity: {
      sha256: "DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA",
      bytes: 79941168,
    },
    settings: {
      project: "fresh-disposable-project-b",
      allowlistedFiles: 1,
      maximumAllowlistedFiles: 2,
      maximumChangedLines: 160,
      exactTargetTestCommand: exactTestCommand,
      providerConfigurationPersisted: false,
    },
    checks: {
      deterministicFailingBaseline: baselineExitCode !== 0,
      exactlyOnePatch: timeline.filter((item) => item.type === "tool.completed" && item.name === "workspace.apply_patch").length === 1,
      exactlyOneTargetTestCommand: timeline.filter((item) => item.type === "command.completed" && item.name === "workspace.run_shell").length === 1,
      distinctDurableApprovals: firstApproval !== null && secondApproval !== null && firstApproval.requestId !== secondApproval.requestId,
      independentTargetTestPassed: independentExitCode === 0,
      timelineContiguous: finalState?.sequencesContiguous ?? false,
      timelineItemIdsUnique: finalState?.itemIdsUnique ?? false,
      resyncBounded: resync.queueRequests >= 1 && resync.queueRequests <= 2 &&
        resync.runners >= 1 && resync.runners <= 2,
      domAndListenersBounded: domDelta.nodes <= 50 && domDelta.documents <= 0 && domDelta.listeners <= 20,
      projectDiscarded: temporaryDelta === 0,
    },
    counts: {
      providerModelItems: timeline.filter((item) => item.type === "assistant.message").length,
      toolCalls: timeline.filter((item) => item.type === "tool.started" || item.type === "command.started").length,
      patchCalls: timeline.filter((item) => item.type === "tool.started" && item.name === "workspace.apply_patch").length,
      shellCalls: timeline.filter((item) => item.type === "command.started" && item.name === "workspace.run_shell").length,
      approvalRequests: timeline.filter((item) => item.type === "approval.requested").length,
      approvalResolutions: timeline.filter((item) => item.type === "approval.resolved").length,
      changedFiles,
      changedLines,
      reports,
      artifacts,
      timelineItems: timeline.length,
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
      ? "Week82 packaged controlled write passed deterministic red-green, one bounded patch, one exact target command, two distinct durable approvals, independent verification, and zero cleanup delta."
      : "Week82 packaged controlled write failed closed; this first attempt remains preserved in the evidence envelope.",
  };
  fs.mkdirSync(evidenceRoot, { recursive: true });
  const body = JSON.stringify(evidence, null, 2);
  fs.writeFileSync(path.join(evidenceRoot, "provider-write.json"), `${body}\n`, "utf8");
  await testInfo.attach("week82-provider-write.json", { body: Buffer.from(body), contentType: "application/json" });
  if (scenarioError && cleanupError) throw new AggregateError([scenarioError, cleanupError], "Controlled-write scenario and cleanup failed.");
  if (scenarioError) throw scenarioError;
  if (cleanupError) throw cleanupError;
  expect(passed).toBe(true);
});

async function readWriteState(page: Page, threadId: string): Promise<WriteState> {
  return page.evaluate(async (id) => {
    const result = await window.caicli.getThread({ threadId: id, afterSequence: 0 });
    if (!result.succeeded || !result.data) throw new Error(result.error?.code ?? "thread-unavailable");
    const timeline = result.data.timeline.map((item) => ({
      itemId: item.itemId, turnId: item.turnId, sequence: item.sequence, type: item.type, name: item.payload.name,
    }));
    return {
      threadStatus: result.data.thread.status,
      turns: result.data.turns.map((turn) => ({
        turnId: turn.turnId,
        status: turn.status,
        approval: turn.approval ? {
          requestId: turn.approval.requestId,
          risk: turn.approval.risk,
          operation: turn.approval.operation,
          targetClass: turn.approval.targetClass,
          policyRevision: turn.approval.policyRevision,
          safeSummary: turn.approval.safeSummary,
        } : null,
      })),
      timeline,
      sequencesContiguous: timeline.every((item, index) => index === 0 || item.sequence === timeline[index - 1]!.sequence + 1),
      itemIdsUnique: new Set(timeline.map((item) => item.itemId)).size === timeline.length,
    };
  }, threadId);
}

function dotnetCommand(): string {
  const candidate = path.join(os.homedir(), ".dotnet", "dotnet.exe");
  return fs.existsSync(candidate) ? candidate : "dotnet";
}

function runChecked(command: string, args: readonly string[], cwd: string): void {
  const result = spawnSync(command, args, { cwd, encoding: "utf8", windowsHide: true });
  if (result.status !== 0) throw new Error(`Project B setup command failed: ${command}.`);
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

function authorizedChildEnvironment(root: string, profile: string, provider: ProviderConfig, dotnet: string): NodeJS.ProcessEnv {
  const environment = { ...process.env };
  delete environment.OPENAI_MODEL;
  delete environment.OPENAI_BASE_URL;
  delete environment.OPENAI_API_KEY;
  const dotnetDirectory = path.dirname(dotnet);
  return {
    ...environment,
    ...provider,
    PATH: dotnet === "dotnet" ? environment.PATH : `${dotnetDirectory};${environment.PATH ?? ""}`,
    APPDATA: path.join(root, "appdata"),
    LOCALAPPDATA: path.join(root, "localappdata"),
    CAICLI_USER_PROFILE: profile,
    CAICLI_AGENT_BACKEND: "direct",
  };
}

function findAppHostPid(mainPid: number): number | null {
  const script = `$rootPid=${mainPid}; $all=@(Get-CimInstance Win32_Process); $byId=@{}; foreach($item in $all){$byId[[int]$item.ProcessId]=$item}; $result=$null; foreach($item in $all){if($item.Name -notlike 'CSharpAiCli.AppHost*'){continue}; $cursor=$item; for($depth=0;$depth -lt 12 -and $null-ne$cursor;$depth++){if([int]$cursor.ParentProcessId -eq $rootPid){$result=[int]$item.ProcessId; break}; $cursor=$byId[[int]$cursor.ParentProcessId]}; if($null-ne$result){break}}; $result | ConvertTo-Json -Compress`;
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

function createCoverageTargets(): { queue: number; runner: number; length: number } {
  const packaged = extractFile(packagedAsar, `dist\\renderer\\assets\\${productBundleName}`);
  const built = fs.readFileSync(productBundlePath);
  if (sha256Buffer(packaged) !== sha256Buffer(built)) throw new Error("Packaged Renderer bundle does not match the verified product build.");
  const consumer = new SourceMapConsumer(JSON.parse(fs.readFileSync(productMapPath, "utf8")) as RawSourceMap);
  const source = consumer.sources.find((candidate) => candidate.endsWith("/use-desktop-controller.ts"));
  if (!source) throw new Error("Product controller source was not found in the verified source map.");
  const starts = generatedLineStarts(built.toString("utf8"));
  const offset = (line: number) => {
    const position = consumer.generatedPositionFor({ source, line, column: 4, bias: SourceMapConsumer.LEAST_UPPER_BOUND });
    if (position.line === null || position.column === null) throw new Error("Product coverage target could not be mapped.");
    return starts[position.line - 1]! + position.column;
  };
  const result = { queue: offset(99), runner: offset(105), length: built.toString("utf8").length };
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
  return resolved.startsWith(path.resolve(os.tmpdir()) + path.sep) && path.basename(resolved).startsWith("caicli-week82-write-");
}

function sha256File(filePath: string): string {
  return sha256Buffer(fs.readFileSync(filePath));
}

function sha256Buffer(value: Buffer): string {
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
