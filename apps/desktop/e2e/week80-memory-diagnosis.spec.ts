import {
  _electron as electron,
  expect,
  test,
  type CDPSession,
  type ElectronApplication,
  type Page,
} from "@playwright/test";
import { createHash } from "node:crypto";
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";

const desktopRoot = path.resolve(import.meta.dirname, "..");
const repositoryRoot = path.resolve(desktopRoot, "..", "..");
const packagedRoot = path.join(desktopRoot, "out", "C-AICLI Desktop-win32-x64");
const packagedExecutable = path.join(packagedRoot, "caicli-desktop.exe");
const packagedAppHost = path.join(packagedRoot, "resources", "apphost", "CSharpAiCli.AppHost.exe");
const evidenceRoot = process.env.CAICLI_WEEK80_EVIDENCE_DIR
  ? path.resolve(process.env.CAICLI_WEEK80_EVIDENCE_DIR)
  : path.join(repositoryRoot, "artifacts", "week80-renderer-private-bytes");
const windowSeconds = 30;
const sampleIntervalSeconds = 5;
const settledSampleCount = 3;
const profileNames = ["C0", "C1", "C2", "C3", "C4", "C5", "C6", "C7"] as const;
type ProfileName = typeof profileNames[number];

interface ObserverCounts {
  pageEvaluateCalls: number;
  electronAppEvaluateCalls: number;
  cdpPerformanceCalls: number;
  cdpDomCounterCalls: number;
  resourceSamples: number;
  externalProcessQueries: number;
  terminalPollCalls: number;
}

interface RendererDiagnostics {
  readonly detailRequestsStarted: number;
  readonly detailRequestsCompleted: number;
  readonly resyncRequested: number;
  readonly resyncCoalesced: number;
  readonly resyncCompleted: number;
  readonly projectionAppended: number;
  readonly projectionReplaced: number;
  readonly ignoredStaleResponses: number;
  readonly pendingDetailRequests: number;
  readonly maximumPendingDetailRequests: number;
  readonly listThreadsCalls: number;
  readonly getThreadCalls: number;
  readonly authoritativeTimelineItems: number;
  readonly turns: number;
  readonly authoritativeProjectionJsonUtf8Bytes: number;
  readonly timelineSummaryUtf8Bytes: number;
  readonly timelinePayloadJsonUtf8Bytes: number;
  readonly distinctTimelineTimestamps: number;
}

interface RoleMetric {
  readonly role: string;
  readonly processCount: number;
  readonly workingSetBytes: number;
  readonly privateBytes: number;
}

interface DiagnosticSample {
  readonly elapsedMilliseconds: number;
  readonly ownedPids: readonly number[];
  readonly roles: readonly RoleMetric[];
  readonly jsHeapUsedBytes: number;
  readonly jsHeapTotalBytes: number;
  readonly nodes: number;
  readonly documents: number;
  readonly jsEventListeners: number;
  readonly visibleTimelineCards: number;
  readonly renderer: RendererDiagnostics | null;
}

interface SamplingWindow {
  readonly durationSeconds: number;
  readonly sampleIntervalSeconds: number;
  readonly settleSeconds: number;
  readonly settledSampleCount: number;
  readonly samples: readonly DiagnosticSample[];
  readonly rendererWorkingSetPeakBytes: number;
  readonly rendererSettledMedian: {
    readonly workingSetBytes: number;
    readonly privateBytes: number;
    readonly jsHeapUsedBytes: number;
    readonly jsHeapTotalBytes: number;
    readonly nodes: number;
    readonly documents: number;
    readonly jsEventListeners: number;
  };
}

interface DiagnosticControl {
  snapshot(): Promise<RendererDiagnostics | null>;
  reset(): Promise<void>;
  setProjection(turns: number, timelineItems: number): Promise<void>;
  setResponseDelay(milliseconds: number): Promise<void>;
  emitThreadChanges(count: number): Promise<void>;
  queryThreadSummary(): Promise<{ timelineItems: number; turns: number } | null>;
}

for (const profile of profileNames) {
  test(`${profile} credential-free renderer memory profile`, async ({ browserName }, testInfo) => {
    if (browserName !== "chromium") throw new Error("Electron diagnostics require Chromium.");
    testInfo.setTimeout(150_000);
    fs.mkdirSync(evidenceRoot, { recursive: true });
    const root = fs.mkdtempSync(path.join(os.tmpdir(), `caicli-week80-${profile.toLowerCase()}-`));
    const observer: ObserverCounts = {
      pageEvaluateCalls: 0,
      electronAppEvaluateCalls: 0,
      cdpPerformanceCalls: 0,
      cdpDomCounterCalls: 0,
      resourceSamples: 0,
      externalProcessQueries: 0,
      terminalPollCalls: 0,
    };
    const started = Date.now();
    let application: ElectronApplication | null = null;
    let cdp: CDPSession | null = null;
    let page: Page | null = null;
    let appHostPid: number | null = null;
    let warm: SamplingWindow | null = null;
    let post: SamplingWindow | null = null;
    let heapSummary: ReturnType<typeof summarizeHeapSnapshot> | null = null;
    let workloadDiagnostics: RendererDiagnostics | null = null;
    let scenarioError: unknown = null;
    let cleanupError: unknown = null;
    let processDelta: number | null = null;
    let temporaryDelta: number | null = null;
    let configurationDelta: number | null = null;

    try {
      if (profile === "C0") {
        expect(sha256(packagedExecutable)).toBe("6BDB9203C0ACCB8D0E3B90EC1F4218A02BF9E05A21E068654F1C2B9B0E82DE29");
        expect(sha256(packagedAppHost)).toBe("DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA");
        const workspace = path.join(root, "workspace");
        fs.mkdirSync(workspace, { recursive: true });
        application = await electron.launch({
          executablePath: packagedExecutable,
          args: ["--disable-gpu"],
          env: credentialFreeEnvironment(root),
        });
        observer.electronAppEvaluateCalls++;
        await application.evaluate(async ({ dialog }, selectedWorkspace) => {
          dialog.showOpenDialog = async () => ({ canceled: false, filePaths: [selectedWorkspace] });
        }, workspace);
        page = await application.firstWindow();
        await expect(page.getByText("AppHost ready")).toBeVisible({ timeout: 60_000 });
        await page.getByRole("button", { name: "Open workspace" }).first().click();
        await expect.poll(async () => observedPageEvaluate(page!, observer, async () =>
          Boolean(await window.caicli.getWorkspaceSnapshot())), { timeout: 20_000 }).toBe(true);
        appHostPid = findAppHostPid(application.process().pid, observer);
        expect(appHostPid, "Packaged C0 must observe the owned AppHost PID.").not.toBeNull();
      } else {
        application = await electron.launch({
          args: ["--disable-gpu", path.join(desktopRoot, "e2e", "fixture-main.cjs")],
          env: {
            ...credentialFreeEnvironment(root),
            CAICLI_E2E_SCENARIO: `week80-${profile.toLowerCase()}`,
            CAICLI_E2E_ROOT: root,
          },
        });
        page = await application.firstWindow();
        await expect(page.getByText("Fixture review thread")).toBeVisible();
        await page.locator(".thread-select").click({ force: true });
        if (profile === "C2") {
          await expect(page.getByText("80 loaded items")).toBeVisible();
          await page.getByRole("button", { name: "Load newer items" }).click();
          await expect(page.getByText("160 loaded items")).toBeVisible();
          await page.getByRole("button", { name: "Load newer items" }).click();
          await expect(page.getByText("240 loaded items")).toBeVisible();
        }
      }

      cdp = await page.context().newCDPSession(page);
      await cdp.send("Performance.enable");
      const diagnostics = createDiagnosticControl(page, observer);
      if (profile === "C2") {
        await page.reload();
        await page.locator(".thread-select").click({ force: true });
        await expect(page.getByText("80 loaded items")).toBeVisible();
      }
      warm = await captureSamplingWindow(application, page, cdp, appHostPid, observer);
      if (profile !== "C0") await diagnostics.reset();
      const aggregatedWorkloadDiagnostics = await runWorkload(profile, page, diagnostics);
      workloadDiagnostics = profile === "C0"
        ? null
        : aggregatedWorkloadDiagnostics ?? await diagnostics.snapshot();
      post = await captureSamplingWindow(application, page, cdp, appHostPid, observer);
      if (profile === "C5") heapSummary = await captureFixtureHeapSummary(cdp, root);
    } catch (error) {
      scenarioError = error;
    }

    const ownedPids = uniquePids([...(warm?.samples ?? []), ...(post?.samples ?? [])]);
    if (application) ownedPids.add(application.process().pid);
    if (appHostPid !== null) ownedPids.add(appHostPid);
    try {
      if (cdp) await cdp.detach().catch(() => undefined);
      if (application) await application.close();
      await waitForProcessesToExit([...ownedPids], 10_000);
      processDelta = countLiveProcesses([...ownedPids]);
      if (!isOwnedRoot(root, profile)) throw new Error("Diagnostic temp-root ownership check failed.");
      fs.rmSync(root, { recursive: true, force: true });
      temporaryDelta = fs.existsSync(root) ? 1 : 0;
      configurationDelta = temporaryDelta;
      expect(processDelta, "Diagnostic owned processes must exit.").toBe(0);
      expect(temporaryDelta, "Diagnostic temporary root must be released.").toBe(0);
      expect(configurationDelta, "Diagnostic configuration must be released.").toBe(0);
    } catch (error) {
      cleanupError = error;
    }

    const retention = warm && post ? {
      workingSetPercent: percentChange(warm.rendererWorkingSetPeakBytes, post.rendererSettledMedian.workingSetBytes),
      privateBytesPercent: percentChange(warm.rendererSettledMedian.privateBytes, post.rendererSettledMedian.privateBytes),
      jsHeapUsedPercent: percentChange(warm.rendererSettledMedian.jsHeapUsedBytes, post.rendererSettledMedian.jsHeapUsedBytes),
      jsHeapTotalPercent: percentChange(warm.rendererSettledMedian.jsHeapTotalBytes, post.rendererSettledMedian.jsHeapTotalBytes),
      nodesDelta: post.rendererSettledMedian.nodes - warm.rendererSettledMedian.nodes,
      documentsDelta: post.rendererSettledMedian.documents - warm.rendererSettledMedian.documents,
      listenersDelta: post.rendererSettledMedian.jsEventListeners - warm.rendererSettledMedian.jsEventListeners,
    } : null;
    const passed = scenarioError === null && cleanupError === null && warm !== null && post !== null &&
      processDelta === 0 && temporaryDelta === 0 && configurationDelta === 0;
    const evidence = {
      schemaVersion: "week80-renderer-private-bytes/v1",
      evidenceKind: "credential-free-profile",
      profile,
      status: passed ? "Passed" : "Failed",
      productRevision: "8e227a4ca050e9bdff5d25d61bf89725fed26104",
      baselineHead: "962d5dda4ae875299a96ba2c825bd13ec683240a",
      packageIdentity: {
        sha256: "6BDB9203C0ACCB8D0E3B90EC1F4218A02BF9E05A21E068654F1C2B9B0E82DE29",
        bytes: 222753280,
      },
      appHostIdentity: {
        sha256: "DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA",
        bytes: 79941168,
      },
      workload: workloadDescription(profile),
      settings: {
        warmWindowSeconds: windowSeconds,
        postWindowSeconds: windowSeconds,
        sampleIntervalSeconds,
        settleSeconds: windowSeconds - ((settledSampleCount - 1) * sampleIntervalSeconds),
        settledSampleCount,
        workers: 1,
        retries: 0,
        forcedGc: false,
        rendererReloadUsedForGate: false,
        providerCalls: 0,
      },
      observer: {
        ...observer,
        terminalPollIntervalMilliseconds: null,
        createsDom: false,
        persistentReferences: "One bounded CDP session and bounded numeric/sample arrays, released before cleanup.",
      },
      workloadDiagnostics,
      heapSummary,
      warm,
      post,
      retention,
      gate15Percent: retention ? {
        workingSetWithinLimit: retention.workingSetPercent <= 15,
        privateBytesWithinLimit: retention.privateBytesPercent <= 15,
      } : null,
      durationMilliseconds: Date.now() - started,
      cleanupDelta: {
        process: processDelta,
        temporary: temporaryDelta,
        configuration: configurationDelta,
      },
      failure: scenarioError === null && cleanupError === null ? null : safeError(scenarioError ?? cleanupError),
      summary: passed
        ? `${profile} credential-free diagnostic profile completed with symmetric windows and zero cleanup delta.`
        : `${profile} credential-free diagnostic profile failed without changing the retention gate.`,
    };
    const body = JSON.stringify(evidence, null, 2);
    fs.writeFileSync(path.join(evidenceRoot, `profile-${profile.toLowerCase()}.json`), body, "utf8");
    await testInfo.attach(`week80-${profile.toLowerCase()}-memory.json`, {
      body: Buffer.from(body),
      contentType: "application/json",
    });
    if (scenarioError && cleanupError) throw new AggregateError([scenarioError, cleanupError], `${profile} scenario and cleanup failed.`);
    if (scenarioError) throw scenarioError;
    if (cleanupError) throw cleanupError;
  });
}

async function runWorkload(
  profile: ProfileName,
  page: Page,
  diagnostics: DiagnosticControl,
): Promise<RendererDiagnostics | null> {
  switch (profile) {
    case "C0":
      return null;
    case "C1":
      for (let turn = 1; turn <= 6; turn++) {
        await diagnostics.setProjection(turn, turn * 6);
        const before = await diagnostics.snapshot();
        await diagnostics.emitThreadChanges(1);
        await expect.poll(async () => (await diagnostics.snapshot())?.resyncCompleted ?? 0)
          .toBe((before?.resyncCompleted ?? 0) + 1);
      }
      await expect(page.getByText("36 loaded items")).toBeVisible();
      return null;
    case "C2":
      {
        const segments: RendererDiagnostics[] = [];
      for (let index = 0; index < 4; index++) {
        const beforeReload = await diagnostics.snapshot();
        if (beforeReload) segments.push(beforeReload);
        await page.reload();
        await page.locator(".thread-select").click({ force: true });
        await expect(page.getByText("80 loaded items")).toBeVisible();
      }
        const finalSegment = await diagnostics.snapshot();
        if (finalSegment) segments.push(finalSegment);
        return aggregateRendererDiagnostics(segments);
      }
    case "C3":
      await diagnostics.setResponseDelay(100);
      await diagnostics.emitThreadChanges(40);
      await expect.poll(async () => (await diagnostics.snapshot())?.pendingDetailRequests ?? -1).toBe(0);
      await expect.poll(async () => (await diagnostics.snapshot())?.resyncCompleted ?? 0).toBeGreaterThan(0);
      await diagnostics.setResponseDelay(0);
      await expect(page.getByText("36 loaded items")).toBeVisible();
      return null;
    case "C4":
      await diagnostics.setProjection(6, 36);
      await diagnostics.emitThreadChanges(1);
      await expect.poll(async () => (await diagnostics.snapshot())?.resyncCompleted ?? 0).toBe(1);
      await expect(page.getByText("36 loaded items")).toBeVisible();
      return null;
    case "C5":
    case "C7":
      for (let turn = 2; turn <= 11; turn++) {
        await diagnostics.setProjection(turn, turn * 6);
        for (let notification = 0; notification < 8; notification++) {
          const before = await diagnostics.snapshot();
          await diagnostics.emitThreadChanges(1);
          await expect.poll(async () => (await diagnostics.snapshot())?.resyncCompleted ?? 0)
            .toBe((before?.resyncCompleted ?? 0) + 1);
        }
      }
      await expect(page.getByText("66 loaded items")).toBeVisible();
      return null;
    case "C6":
      await diagnostics.setProjection(11, 66);
      for (let poll = 0; poll < 137; poll++) {
        expect(await diagnostics.queryThreadSummary()).toEqual({ timelineItems: 66, turns: 11 });
      }
      return null;
  }
}

function aggregateRendererDiagnostics(values: readonly RendererDiagnostics[]): RendererDiagnostics {
  const sum = (field: keyof RendererDiagnostics) => values.reduce((total, value) => total + value[field], 0);
  const last = values.at(-1);
  if (!last) throw new Error("C2 requires at least one diagnostic segment.");
  return {
    detailRequestsStarted: sum("detailRequestsStarted"),
    detailRequestsCompleted: sum("detailRequestsCompleted"),
    resyncRequested: sum("resyncRequested"),
    resyncCoalesced: sum("resyncCoalesced"),
    resyncCompleted: sum("resyncCompleted"),
    projectionAppended: sum("projectionAppended"),
    projectionReplaced: sum("projectionReplaced"),
    ignoredStaleResponses: sum("ignoredStaleResponses"),
    pendingDetailRequests: last.pendingDetailRequests,
    maximumPendingDetailRequests: Math.max(...values.map((value) => value.maximumPendingDetailRequests)),
    listThreadsCalls: sum("listThreadsCalls"),
    getThreadCalls: sum("getThreadCalls"),
    authoritativeTimelineItems: last.authoritativeTimelineItems,
    turns: last.turns,
    authoritativeProjectionJsonUtf8Bytes: last.authoritativeProjectionJsonUtf8Bytes,
    timelineSummaryUtf8Bytes: last.timelineSummaryUtf8Bytes,
    timelinePayloadJsonUtf8Bytes: last.timelinePayloadJsonUtf8Bytes,
    distinctTimelineTimestamps: last.distinctTimelineTimestamps,
  };
}

function createDiagnosticControl(page: Page, observer: ObserverCounts): DiagnosticControl {
  return {
    snapshot: () => observedPageEvaluate(page, observer, () => {
      const bridge = window.caicliMemoryDiagnostics as typeof window.caicliMemoryDiagnostics & {
        snapshot?: () => RendererDiagnostics;
      };
      return bridge?.snapshot?.() ?? null;
    }),
    reset: () => observedPageEvaluate(page, observer, () => {
      const bridge = window.caicliMemoryDiagnostics as typeof window.caicliMemoryDiagnostics & { reset?: () => void };
      bridge?.reset?.();
    }),
    setProjection: (turns, timelineItems) => observedPageEvaluate(page, observer, ([turnCount, itemCount]) => {
      const bridge = window.caicliMemoryDiagnostics as typeof window.caicliMemoryDiagnostics & {
        setProjection?: (value: { turns: number; timelineItems: number }) => void;
      };
      bridge?.setProjection?.({ turns: turnCount, timelineItems: itemCount });
    }, [turns, timelineItems] as const),
    setResponseDelay: (milliseconds) => observedPageEvaluate(page, observer, (value) => {
      const bridge = window.caicliMemoryDiagnostics as typeof window.caicliMemoryDiagnostics & {
        setResponseDelay?: (delay: number) => void;
      };
      bridge?.setResponseDelay?.(value);
    }, milliseconds),
    emitThreadChanges: (count) => observedPageEvaluate(page, observer, (value) => {
      const bridge = window.caicliMemoryDiagnostics as typeof window.caicliMemoryDiagnostics & {
        emitThreadChanges?: (eventCount: number) => void;
      };
      bridge?.emitThreadChanges?.(value);
    }, count),
    queryThreadSummary: () => observedPageEvaluate(page, observer, async () => {
      const result = await window.caicli.getThread({ threadId: "fixture-thread", afterSequence: 0 });
      return result.data
        ? { timelineItems: result.data.timeline.length, turns: result.data.turns.length }
        : null;
    }),
  };
}

async function observedPageEvaluate<R, A = void>(
  page: Page,
  observer: ObserverCounts,
  callback: (argument: A) => R | Promise<R>,
  argument?: A,
): Promise<R> {
  observer.pageEvaluateCalls++;
  return page.evaluate(callback, argument as A);
}

async function captureSamplingWindow(
  application: ElectronApplication,
  page: Page,
  cdp: CDPSession,
  appHostPid: number | null,
  observer: ObserverCounts,
): Promise<SamplingWindow> {
  const started = Date.now();
  const samples: DiagnosticSample[] = [];
  for (let index = 0; index <= Math.floor(windowSeconds / sampleIntervalSeconds); index++) {
    if (index > 0) await new Promise((resolve) => setTimeout(resolve, sampleIntervalSeconds * 1000));
    samples.push(await captureSample(application, page, cdp, appHostPid, observer, Date.now() - started));
  }
  const settled = samples.slice(-settledSampleCount);
  return {
    durationSeconds: windowSeconds,
    sampleIntervalSeconds,
    settleSeconds: windowSeconds - ((settledSampleCount - 1) * sampleIntervalSeconds),
    settledSampleCount,
    samples,
    rendererWorkingSetPeakBytes: Math.max(...samples.map((sample) => roleBytes(sample.roles, "Tab", "workingSetBytes"))),
    rendererSettledMedian: {
      workingSetBytes: median(settled.map((sample) => roleBytes(sample.roles, "Tab", "workingSetBytes"))),
      privateBytes: median(settled.map((sample) => roleBytes(sample.roles, "Tab", "privateBytes"))),
      jsHeapUsedBytes: median(settled.map((sample) => sample.jsHeapUsedBytes)),
      jsHeapTotalBytes: median(settled.map((sample) => sample.jsHeapTotalBytes)),
      nodes: median(settled.map((sample) => sample.nodes)),
      documents: median(settled.map((sample) => sample.documents)),
      jsEventListeners: median(settled.map((sample) => sample.jsEventListeners)),
    },
  };
}

async function captureSample(
  application: ElectronApplication,
  page: Page,
  cdp: CDPSession,
  appHostPid: number | null,
  observer: ObserverCounts,
  elapsedMilliseconds: number,
): Promise<DiagnosticSample> {
  observer.resourceSamples++;
  observer.cdpPerformanceCalls++;
  const performance = await cdp.send("Performance.getMetrics");
  observer.cdpDomCounterCalls++;
  const dom = await cdp.send("Memory.getDOMCounters");
  observer.electronAppEvaluateCalls++;
  const electronProcesses = await application.evaluate(({ app }) => app.getAppMetrics().map((metric) => ({
    pid: metric.pid,
    role: metric.type,
    workingSetBytes: metric.memory.workingSetSize * 1024,
    privateBytes: metric.memory.privateBytes * 1024,
  })));
  const appHost = appHostPid === null ? null : sampleExternalProcess(appHostPid, observer);
  const roles = aggregateRoles(appHost ? [...electronProcesses, { ...appHost, role: "AppHost" }] : electronProcesses);
  const renderer = await observedPageEvaluate(page, observer, () => {
    const bridge = window.caicliMemoryDiagnostics as typeof window.caicliMemoryDiagnostics & {
      snapshot?: () => RendererDiagnostics;
    };
    return {
      visibleTimelineCards: document.querySelectorAll(".timeline-card").length,
      diagnostics: bridge?.snapshot?.() ?? null,
    };
  });
  const metrics = new Map(performance.metrics.map((metric) => [metric.name, metric.value]));
  return {
    elapsedMilliseconds,
    ownedPids: electronProcesses.map((process) => process.pid).concat(appHostPid === null ? [] : [appHostPid]),
    roles,
    jsHeapUsedBytes: metricValue(metrics, "JSHeapUsedSize"),
    jsHeapTotalBytes: metricValue(metrics, "JSHeapTotalSize"),
    nodes: dom.nodes,
    documents: dom.documents,
    jsEventListeners: dom.jsEventListeners,
    visibleTimelineCards: renderer.visibleTimelineCards,
    renderer: renderer.diagnostics,
  };
}

function aggregateRoles(processes: readonly { role: string; workingSetBytes: number; privateBytes: number }[]): RoleMetric[] {
  const roles = new Map<string, RoleMetric>();
  for (const process of processes) {
    const current = roles.get(process.role) ?? { role: process.role, processCount: 0, workingSetBytes: 0, privateBytes: 0 };
    roles.set(process.role, {
      role: process.role,
      processCount: current.processCount + 1,
      workingSetBytes: current.workingSetBytes + process.workingSetBytes,
      privateBytes: current.privateBytes + process.privateBytes,
    });
  }
  for (const role of ["Browser", "Tab", "GPU", "Utility", "AppHost"]) {
    if (!roles.has(role)) roles.set(role, { role, processCount: 0, workingSetBytes: 0, privateBytes: 0 });
  }
  return [...roles.values()].sort((left, right) => left.role.localeCompare(right.role));
}

function findAppHostPid(mainPid: number, observer: ObserverCounts): number | null {
  observer.externalProcessQueries++;
  const script = `$rootPid=${mainPid}; $all=@(Get-CimInstance Win32_Process); $byId=@{}; foreach($item in $all){$byId[[int]$item.ProcessId]=$item}; $result=$null; foreach($item in $all){if($item.Name -notlike 'CSharpAiCli.AppHost*'){continue}; $cursor=$item; for($depth=0;$depth -lt 12 -and $null -ne $cursor;$depth++){if([int]$cursor.ParentProcessId -eq $rootPid){$result=[int]$item.ProcessId; break}; $cursor=$byId[[int]$cursor.ParentProcessId]}; if($null -ne $result){break}}; $result | ConvertTo-Json -Compress`;
  const result = spawnSync("powershell.exe", ["-NoProfile", "-Command", script], { encoding: "utf8", windowsHide: true });
  if (result.status !== 0 || !result.stdout.trim()) return null;
  const parsed: unknown = JSON.parse(result.stdout);
  return typeof parsed === "number" && Number.isSafeInteger(parsed) ? parsed : null;
}

function sampleExternalProcess(pid: number, observer: ObserverCounts): { workingSetBytes: number; privateBytes: number } {
  observer.externalProcessQueries++;
  const script = `$item=Get-Process -Id ${pid} -ErrorAction Stop; [ordered]@{workingSetBytes=[int64]$item.WorkingSet64;privateBytes=[int64]$item.PrivateMemorySize64} | ConvertTo-Json -Compress`;
  const result = spawnSync("powershell.exe", ["-NoProfile", "-Command", script], { encoding: "utf8", windowsHide: true });
  if (result.status !== 0 || !result.stdout.trim()) throw new Error("Owned AppHost metric query failed.");
  const parsed = JSON.parse(result.stdout) as { workingSetBytes: number; privateBytes: number };
  return parsed;
}

function credentialFreeEnvironment(root: string): NodeJS.ProcessEnv {
  return {
    ...process.env,
    APPDATA: path.join(root, "appdata"),
    LOCALAPPDATA: path.join(root, "localappdata"),
    CAICLI_USER_PROFILE: path.join(root, "profile"),
    OPENAI_API_KEY: "",
    OPENAI_BASE_URL: "",
    OPENAI_MODEL: "",
  };
}

function workloadDescription(profile: ProfileName): string {
  switch (profile) {
    case "C0": return "Packaged Desktop ready with zero turns and symmetric idle windows.";
    case "C1": return "Deterministic fixture adds six turns and 36 safe timeline items in six batches.";
    case "C2": return "Week 77 frozen 240-item projection with five renderer loads; reload results do not determine the gate.";
    case "C3": return "Forty thread-change notifications request projection resync without turn or timeline growth.";
    case "C4": return "One authoritative projection adds six turns and 36 safe timeline items without provider access.";
    case "C5": return "Ten six-item fixture turns trigger eight non-overlapping full projection resyncs per turn, matching P10 notification volume without provider content.";
    case "C6": return "One hundred thirty-seven observer-style direct getThread polls read an 11-turn, 66-item fixture projection without updating renderer state.";
    case "C7": return "C5-equivalent 80 full projections and 66 items use a distinct ISO timestamp per item to exercise repeated locale formatting.";
  }
}

async function captureFixtureHeapSummary(
  cdp: CDPSession,
  root: string,
): Promise<ReturnType<typeof summarizeHeapSnapshot>> {
  const snapshotPath = path.join(root, "fixture-post-gate.heapsnapshot");
  const chunks: string[] = [];
  const listener = (event: { chunk: string }) => { chunks.push(event.chunk); };
  cdp.on("HeapProfiler.addHeapSnapshotChunk", listener);
  try {
    await cdp.send("HeapProfiler.enable");
    await cdp.send("HeapProfiler.takeHeapSnapshot", { reportProgress: false, captureNumericValue: false });
    fs.writeFileSync(snapshotPath, chunks.join(""), "utf8");
    return summarizeHeapSnapshot(JSON.parse(fs.readFileSync(snapshotPath, "utf8")) as HeapSnapshot);
  } finally {
    cdp.off("HeapProfiler.addHeapSnapshotChunk", listener);
    await cdp.send("HeapProfiler.disable").catch(() => undefined);
    fs.rmSync(snapshotPath, { force: true });
  }
}

interface HeapSnapshot {
  readonly snapshot: {
    readonly meta: {
      readonly node_fields: readonly string[];
      readonly node_types: readonly (readonly string[] | string)[];
    };
  };
  readonly nodes: readonly number[];
  readonly strings: readonly string[];
}

function summarizeHeapSnapshot(snapshot: HeapSnapshot) {
  const fields = snapshot.snapshot.meta.node_fields;
  const width = fields.length;
  const typeIndex = fields.indexOf("type");
  const nameIndex = fields.indexOf("name");
  const selfSizeIndex = fields.indexOf("self_size");
  const types = snapshot.snapshot.meta.node_types[typeIndex] as readonly string[];
  if (width < 1 || typeIndex < 0 || nameIndex < 0 || selfSizeIndex < 0 || !Array.isArray(types)) {
    throw new Error("Fixture heap snapshot schema is unsupported.");
  }
  const aggregates = new Map<string, { count: number; selfBytes: number }>();
  let totalSelfBytes = 0;
  for (let offset = 0; offset < snapshot.nodes.length; offset += width) {
    const type = types[snapshot.nodes[offset + typeIndex] ?? -1] ?? "unknown";
    const name = snapshot.strings[snapshot.nodes[offset + nameIndex] ?? -1] ?? "";
    const selfBytes = snapshot.nodes[offset + selfSizeIndex] ?? 0;
    const category = safeHeapCategory(type, name);
    const aggregate = aggregates.get(category) ?? { count: 0, selfBytes: 0 };
    aggregate.count++;
    aggregate.selfBytes += selfBytes;
    totalSelfBytes += selfBytes;
    aggregates.set(category, aggregate);
  }
  return {
    source: "credential-free C5 post-Gate disposable snapshot",
    gateEligible: false,
    rawSnapshotPersisted: false,
    rawStringsPersisted: false,
    nodeCount: snapshot.nodes.length / width,
    totalSelfBytes,
    topAggregates: [...aggregates.entries()]
      .map(([category, value]) => ({ category, ...value }))
      .sort((left, right) => right.selfBytes - left.selfBytes)
      .slice(0, 20),
  };
}

function safeHeapCategory(type: string, name: string): string {
  if (type === "string" || type === "concatenated string" || type === "sliced string") return "String";
  if (type === "closure") return "Closure";
  if (type === "code") return "Code";
  if (type !== "object" && type !== "native") return type;
  if (name.includes("Fiber")) return "ReactFiber";
  if (/^(?:HTML.*Element|Document|Text|Node|NodeList|DOMTokenList)$/.test(name)) return "DOM";
  if (["Array", "Object", "Promise", "Map", "Set", "WeakMap", "WeakSet", "Date", "RegExp"].includes(name)) {
    return name;
  }
  return type === "native" ? "NativeOther" : "ObjectOther";
}

function sha256(filePath: string): string {
  return createHash("sha256").update(fs.readFileSync(filePath)).digest("hex").toUpperCase();
}

function metricValue(metrics: ReadonlyMap<string, number>, name: string): number {
  const value = metrics.get(name);
  if (value === undefined || !Number.isFinite(value) || value < 0) throw new Error(`Missing non-negative ${name} metric.`);
  return value;
}

function roleBytes(roles: readonly RoleMetric[], role: string, field: "workingSetBytes" | "privateBytes"): number {
  return roles.find((candidate) => candidate.role === role)?.[field] ?? 0;
}

function median(values: readonly number[]): number {
  if (values.length === 0) throw new Error("A sampling window requires samples.");
  const sorted = [...values].sort((left, right) => left - right);
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 === 0 ? (sorted[middle - 1]! + sorted[middle]!) / 2 : sorted[middle]!;
}

function percentChange(baseline: number, value: number): number {
  if (baseline <= 0) throw new Error("A positive baseline is required.");
  return ((value - baseline) / baseline) * 100;
}

function uniquePids(samples: readonly DiagnosticSample[]): Set<number> {
  return new Set(samples.flatMap((sample) => sample.ownedPids));
}

async function waitForProcessesToExit(pids: readonly number[], timeoutMilliseconds: number): Promise<void> {
  const deadline = Date.now() + timeoutMilliseconds;
  while (countLiveProcesses(pids) > 0 && Date.now() < deadline) {
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
}

function countLiveProcesses(pids: readonly number[]): number {
  return pids.filter((pid) => {
    try { process.kill(pid, 0); return true; } catch { return false; }
  }).length;
}

function isOwnedRoot(root: string, profile: ProfileName): boolean {
  const resolved = path.resolve(root);
  return resolved.startsWith(path.resolve(os.tmpdir()) + path.sep) &&
    path.basename(resolved).startsWith(`caicli-week80-${profile.toLowerCase()}-`);
}

function safeError(error: unknown): { readonly name: string; readonly message: string } {
  const value = error instanceof Error ? error : new Error(String(error));
  return { name: value.name.slice(0, 128), message: value.message.replace(/[a-zA-Z]:[\\/][^\s"'<>]*/g, "[rooted-path-redacted]").slice(0, 1000) };
}
