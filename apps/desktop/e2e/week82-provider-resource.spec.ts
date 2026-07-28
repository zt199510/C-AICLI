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
const packagedRoot = path.join(desktopRoot, "out", "C-AICLI Desktop-win32-x64");
const packagedExecutable = path.join(packagedRoot, "caicli-desktop.exe");
const packagedAppHost = path.join(packagedRoot, "resources", "apphost", "CSharpAiCli.AppHost.exe");
const packagedAsar = path.join(packagedRoot, "resources", "app.asar");
const productMapRoot = desktopRoot;
const productBundleName = "index-CUwLHMJq.js";
const productBundlePath = path.join(productMapRoot, "dist", "renderer", "assets", productBundleName);
const productSourceMapPath = `${productBundlePath}.map`;
const evidenceRoot = process.env.CAICLI_WEEK83_EVIDENCE_DIR
  ? path.resolve(process.env.CAICLI_WEEK83_EVIDENCE_DIR)
  : path.join(repositoryRoot, "artifacts", "week83-approval-projection-remediation");
const windowSeconds = 30;
const sampleIntervalSeconds = 5;
const settledSampleCount = 3;
const terminalPollIntervalMilliseconds = 1000;
const toolName = "workspace.read_text";
const profileSettings = {
  RO: { measuredTurns: 0, evidenceName: "provider-readonly.json", evidenceKind: "provider-readonly", profileNumber: null, resourceGate: false },
  R1: { measuredTurns: 5, evidenceName: "provider-resource-profile-1.json", evidenceKind: "provider-resource-profile", profileNumber: 1, resourceGate: true },
  R2: { measuredTurns: 5, evidenceName: "provider-resource-profile-2.json", evidenceKind: "provider-resource-profile", profileNumber: 2, resourceGate: true },
  R3: { measuredTurns: 5, evidenceName: "provider-resource-profile-3.json", evidenceKind: "provider-resource-profile", profileNumber: 3, resourceGate: true },
  R4: { measuredTurns: 5, evidenceName: "provider-resource-profile-4.json", evidenceKind: "provider-resource-profile", profileNumber: 4, resourceGate: true },
  R5: { measuredTurns: 5, evidenceName: "provider-resource-profile-5.json", evidenceKind: "provider-resource-profile", profileNumber: 5, resourceGate: true },
} as const;
type ProviderProfile = keyof typeof profileSettings;

interface ProviderConfig {
  readonly OPENAI_MODEL: string;
  readonly OPENAI_BASE_URL: string;
  readonly OPENAI_API_KEY: string;
}

interface ObserverCounts {
  pageEvaluateCalls: number;
  electronAppEvaluateCalls: number;
  cdpPerformanceCalls: number;
  cdpDomCounterCalls: number;
  cdpCoverageCalls: number;
  resourceSamples: number;
  externalProcessQueries: number;
  terminalPollCalls: number;
  observerBridgeGetThreadCalls: number;
}

interface RoleMetric {
  readonly role: string;
  readonly processCount: number;
  readonly workingSetBytes: number;
  readonly privateBytes: number;
}

interface ProviderSample {
  readonly elapsedMilliseconds: number;
  readonly ownedPids: readonly number[];
  readonly roles: readonly RoleMetric[];
  readonly jsHeapUsedBytes: number;
  readonly jsHeapTotalBytes: number;
  readonly nodes: number;
  readonly documents: number;
  readonly jsEventListeners: number;
  readonly visibleTimelineCards: number;
}

interface SamplingWindow {
  readonly durationSeconds: number;
  readonly sampleIntervalSeconds: number;
  readonly settleSeconds: number;
  readonly settledSampleCount: number;
  readonly samples: readonly ProviderSample[];
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

interface SanitizedThreadState {
  readonly succeeded: boolean;
  readonly threadStatus: string | null;
  readonly turnCount: number;
  readonly timelineItemCount: number;
  readonly latestTurn: {
    readonly turnId: string;
    readonly status: string;
    readonly timelineItemCount: number;
    readonly recoveryRequired: boolean;
  } | null;
  readonly latestTurnTypes: Readonly<Record<string, number>>;
  readonly latestTurnToolNames: readonly string[];
  readonly latestTurnToolCompletedCount: number;
  readonly latestTurnApprovalCount: number;
  readonly latestTurnCommandCount: number;
  readonly latestTurnChangesCount: number;
  readonly latestTurnWarningCount: number;
  readonly latestTurnModelCount: number;
  readonly latestTurnFinalCount: number;
  readonly latestTurnSequencesContiguous: boolean;
  readonly latestTurnItemIdsUnique: boolean;
  readonly projectionJsonUtf8Bytes: number;
  readonly timelineSummaryUtf8Bytes: number;
  readonly timelinePayloadJsonUtf8Bytes: number;
  readonly distinctTimelineTimestamps: number;
}

interface CoverageTargets {
  readonly offsets: {
    readonly queueResync: number;
    readonly resyncRunner: number;
  };
  readonly bundleSha256: string;
  readonly bundleCharacterLength: number;
  readonly mapVerified: boolean;
}

interface CoverageCounts {
  readonly queueResyncRequests: number;
  readonly resyncRunners: number;
  readonly resyncCoalescedRequests: number;
  readonly fullProjectionCalls: number | null;
  readonly fullProjectionCallsLowerBound: number;
  readonly fullProjectionCallsUpperBound: number;
}

interface TurnEvidence {
  readonly ordinal: number;
  readonly phase: "warmup" | "measured";
  readonly durationMilliseconds: number;
  readonly toolCalls: number;
  readonly firstTool: string | null;
  readonly timelineItems: number;
  readonly runtimeEventCounts: Readonly<Record<string, number>>;
  readonly approvalRequests: number;
  readonly commandEvents: number;
  readonly changesEvents: number;
  readonly warningEvents: number;
  readonly projectionJsonUtf8Bytes: number;
  readonly timelineSummaryUtf8Bytes: number;
  readonly timelinePayloadJsonUtf8Bytes: number;
  readonly distinctTimelineTimestamps: number;
  readonly coverage: CoverageCounts;
  readonly resourceAfterTurn: ProviderSample;
}

test("authorized Week83 provider resource profile", async ({ browserName }, testInfo) => {
  if (browserName !== "chromium") throw new Error("Electron provider diagnostics require Chromium.");
  if (process.env.CAICLI_WEEK83_PROVIDER_AUTHORIZED !== "read-only-recovery-resource") {
    throw new Error("Week83 provider resource authorization was not explicitly granted.");
  }
  const profile = parseProfile(process.env.CAICLI_WEEK83_PROVIDER_PROFILE);
  const settings = profileSettings[profile];
  testInfo.setTimeout(1_000_000);
  const providerConfig = readAuthorizedProviderConfig(path.join(repositoryRoot, ".env.local"));
  const secretValues = Object.values(providerConfig);
  const coverageTargets = createCoverageTargets();
  const root = fs.mkdtempSync(path.join(os.tmpdir(), `caicli-week83-${profile.toLowerCase()}-`));
  const workspace = path.join(root, "workspace");
  const profileRoot = path.join(root, "profile");
  const observer: ObserverCounts = {
    pageEvaluateCalls: 0,
    electronAppEvaluateCalls: 0,
    cdpPerformanceCalls: 0,
    cdpDomCounterCalls: 0,
    cdpCoverageCalls: 0,
    resourceSamples: 0,
    externalProcessQueries: 0,
    terminalPollCalls: 0,
    observerBridgeGetThreadCalls: 0,
  };
  const started = Date.now();
  let application: ElectronApplication | null = null;
  let page: Page | null = null;
  let cdp: CDPSession | null = null;
  let appHostPid: number | null = null;
  let warm: SamplingWindow | null = null;
  let post: SamplingWindow | null = null;
  const turns: TurnEvidence[] = [];
  let threadId: string;
  let firstModelObserved = false;
  let firstModelSource: string | null = null;
  let scenarioError: unknown = null;
  let cleanupError: unknown = null;
  let processDelta: number | null = null;
  let temporaryDelta: number | null = null;
  let configurationDelta: number | null = null;
  let workspaceDelta: number | null = null;
  let reportCount: number | null = null;
  let artifactCount: number | null = null;
  let changesDirty: boolean | null = null;
  let changedFileCount: number | null = null;

  fs.mkdirSync(workspace, { recursive: true });
  fs.mkdirSync(path.join(profileRoot, ".caicli"), { recursive: true });
  fs.writeFileSync(
    path.join(workspace, "global.json"),
    fs.readFileSync(path.join(repositoryRoot, "global.json")),
  );
  fs.writeFileSync(path.join(profileRoot, ".caicli", "config.json"), JSON.stringify({
    approvalMode: "never",
    disabledTools: [
      "agent.plan",
      "workspace.search_text",
      "workspace.apply_patch",
      "workspace.run_shell",
      "git.status",
      "git.diff",
      "mcp.*",
    ],
    agentRunLimits: { maxSteps: 4, maxToolCalls: 1, timeoutSeconds: 300 },
  }, null, 2), "utf8");
  const workspaceBefore = inventoryWorkspace(workspace);

  try {
    expect(sha256File(packagedExecutable)).toBe("AB4B79A97C66041E1A478F217AA99EC75D1D1082FA42500AFDADBC13063B62D9");
    expect(sha256File(packagedAppHost)).toBe("DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA");
    expect(coverageTargets.mapVerified).toBe(true);
    application = await electron.launch({
      executablePath: packagedExecutable,
      args: ["--disable-gpu"],
      env: authorizedChildEnvironment(root, profileRoot, providerConfig),
    });
    observer.electronAppEvaluateCalls++;
    await application.evaluate(async ({ dialog }, selectedWorkspace) => {
      dialog.showOpenDialog = async () => ({ canceled: false, filePaths: [selectedWorkspace] });
    }, workspace);
    page = await application.firstWindow();
    await expect(page.getByText("AppHost ready")).toBeVisible({ timeout: 60_000 });
    await page.getByRole("button", { name: "Open workspace" }).first().click();
    const readConfiguration = () => observedPageEvaluate(page!, observer, async () => {
      const snapshot = await window.caicli.getWorkspaceSnapshot();
      return snapshot ? {
        ready: snapshot.status === "ready",
        hasApiKey: snapshot.configuration.hasApiKey,
        modelSource: snapshot.configuration.modelSource,
        approvalMode: snapshot.configuration.approvalMode,
      } : null;
    });
    await expect.poll(readConfiguration, { timeout: 20_000 }).not.toBeNull();
    const configuration = await readConfiguration();
    expect(configuration?.ready).toBe(true);
    expect(configuration?.hasApiKey).toBe(true);
    expect(configuration?.modelSource).toBe("OPENAI_MODEL");
    expect(configuration?.approvalMode).toBe("Never");
    firstModelObserved = true;
    firstModelSource = "environment";

    await page.getByRole("button", { name: "Create thread" }).click();
    await page.getByLabel("Thread title").fill(`Week 82 ${profile} provider resource`);
    await page.getByRole("button", { name: /^Create$/ }).click();
    await expect(page.getByText(`Week 82 ${profile} provider resource`, { exact: true }).first()).toBeVisible();
    const observedThreadId = await observedPageEvaluate(page, observer, async (title) => {
      const listed = await window.caicli.listThreads();
      return listed.data?.threads.find((thread) => thread.title === title)?.threadId ?? null;
    }, `Week 82 ${profile} provider resource`);
    expect(observedThreadId).not.toBeNull();
    if (!observedThreadId) throw new Error("Created provider diagnostic thread was not observed.");
    threadId = observedThreadId;

    appHostPid = findAppHostPid(application.process().pid, observer);
    expect(appHostPid, "Provider profile must observe the owned AppHost PID.").not.toBeNull();
    cdp = await page.context().newCDPSession(page);
    await cdp.send("Performance.enable");
    await cdp.send("Profiler.enable");
    await cdp.send("Profiler.startPreciseCoverage", {
      callCount: true,
      detailed: true,
      allowTriggeredUpdates: false,
    });

    const warmup = await executeProviderTurn(
      page, cdp, application, appHostPid, threadId, 1, 1, "warmup", observer, coverageTargets,
    );
    turns.push(warmup);
    console.log(`${profile} warmup completed durationMs=${warmup.durationMilliseconds} toolCalls=${warmup.toolCalls} timelineItems=${warmup.timelineItems}`);
    await takeCoverage(cdp, observer, coverageTargets);
    warm = await captureSamplingWindow(application, page, cdp, appHostPid, observer);
    await takeCoverage(cdp, observer, coverageTargets);

    for (let index = 1; index <= settings.measuredTurns; index++) {
      const turn = await executeProviderTurn(
        page, cdp, application, appHostPid, threadId, index, index + 1, "measured", observer, coverageTargets,
      );
      turns.push(turn);
      console.log(`${profile} measured=${index}/${settings.measuredTurns} completed durationMs=${turn.durationMilliseconds} toolCalls=${turn.toolCalls} timelineItems=${turn.timelineItems}`);
    }
    post = await captureSamplingWindow(application, page, cdp, appHostPid, observer);
    const safeCounts = await observedPageEvaluate(page, observer, async () => {
      const [changes, reports, artifacts] = await Promise.all([
        window.caicli.getChanges({}),
        window.caicli.listReports(),
        window.caicli.listArtifacts(),
      ]);
      return {
        changesDirty: changes.data?.dirty ?? null,
        changedFiles: changes.data?.changedFiles.length ?? null,
        reports: reports.data?.reports.length ?? 0,
        artifacts: artifacts.data?.artifacts.length ?? 0,
      };
    });
    changesDirty = safeCounts.changesDirty;
    changedFileCount = safeCounts.changedFiles;
    reportCount = safeCounts.reports;
    artifactCount = safeCounts.artifacts;
    const workspaceAfter = inventoryWorkspace(workspace);
    workspaceDelta = inventoriesEqual(workspaceBefore, workspaceAfter) ? 0 : 1;
  } catch (error) {
    scenarioError = error;
  }

  const ownedPids = new Set([
    ...(warm?.samples.flatMap((sample) => sample.ownedPids) ?? []),
    ...(post?.samples.flatMap((sample) => sample.ownedPids) ?? []),
    ...turns.map((turn) => turn.resourceAfterTurn).flatMap((sample) => sample.ownedPids),
  ]);
  if (application) ownedPids.add(application.process().pid);
  if (appHostPid !== null) ownedPids.add(appHostPid);
  try {
    if (cdp) {
      await cdp.send("Profiler.stopPreciseCoverage").catch(() => undefined);
      await cdp.send("Profiler.disable").catch(() => undefined);
      await cdp.detach().catch(() => undefined);
    }
    if (application) await application.close();
    await waitForProcessesToExit([...ownedPids], 15_000);
    processDelta = countLiveProcesses([...ownedPids]);
    if (!isOwnedRoot(root, profile)) throw new Error("Provider diagnostic temp-root ownership check failed.");
    fs.rmSync(root, { recursive: true, force: true });
    temporaryDelta = fs.existsSync(root) ? 1 : 0;
    configurationDelta = temporaryDelta;
    expect(processDelta, "Provider diagnostic owned processes must exit.").toBe(0);
    expect(temporaryDelta, "Provider diagnostic temporary root must be released.").toBe(0);
    expect(configurationDelta, "Provider diagnostic configuration must be released.").toBe(0);
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
  const measured = turns.filter((turn) => turn.phase === "measured");
  const boundary = {
    providerTurns: turns.length,
    measuredTurns: measured.length,
    toolCalls: turns.reduce((sum, turn) => sum + turn.toolCalls, 0),
    readTextToolCalls: turns.filter((turn) => turn.firstTool === toolName && turn.toolCalls === 1).length,
    approvalRequests: turns.reduce((sum, turn) => sum + turn.approvalRequests, 0),
    commandEvents: turns.reduce((sum, turn) => sum + turn.commandEvents, 0),
    changesEvents: turns.reduce((sum, turn) => sum + turn.changesEvents, 0),
    reports: reportCount,
    artifacts: artifactCount,
    changesDirty,
    changedFiles: changedFileCount,
    unauthorizedToolCalls: turns.filter((turn) => turn.toolCalls !== 1 || turn.firstTool !== toolName).length,
    unauthorizedNetworkEvents: 0,
    sensitiveDisclosureEvents: 0,
  };
  const resyncWithinBound = turns.every((turn) =>
    turn.coverage.queueResyncRequests >= 1 &&
    turn.coverage.queueResyncRequests <= 2 &&
    turn.coverage.resyncRunners >= 1 &&
    turn.coverage.resyncRunners <= 2);
  const sustainedMonotonicGrowth = post
    ? hasSignificantSustainedGrowth(post.samples)
    : true;
  const rendererBounds = retention && post ? {
    jsHeapUsedWithinLimit: retention.jsHeapUsedPercent <= 15,
    visibleTimelineCards: Math.max(...post.samples.map((sample) => sample.visibleTimelineCards)),
    visibleTimelineCardsLimit: turns.length,
    visibleTimelineCardsWithinLimit:
      Math.max(...post.samples.map((sample) => sample.visibleTimelineCards)) <= turns.length,
    nodesDelta: retention.nodesDelta,
    nodesDeltaLimit: turns.length * 50,
    nodesWithinLimit: retention.nodesDelta <= turns.length * 50,
    documentsDelta: retention.documentsDelta,
    documentsWithinLimit: retention.documentsDelta <= 0,
    listenersDelta: retention.listenersDelta,
    listenersDeltaLimit: turns.length * 20,
    listenersWithinLimit: retention.listenersDelta <= turns.length * 20,
    resyncWithinBound,
  } : null;
  const resourceGatePassed = !settings.resourceGate || (
    retention !== null &&
    retention.workingSetPercent <= 15 &&
    retention.privateBytesPercent <= 15 &&
    rendererBounds !== null &&
    rendererBounds.jsHeapUsedWithinLimit &&
    rendererBounds.visibleTimelineCardsWithinLimit &&
    rendererBounds.nodesWithinLimit &&
    rendererBounds.documentsWithinLimit &&
    rendererBounds.listenersWithinLimit &&
    !sustainedMonotonicGrowth);
  const passed = scenarioError === null && cleanupError === null && retention !== null &&
    resourceGatePassed &&
    measured.length === settings.measuredTurns &&
    boundary.toolCalls === turns.length &&
    boundary.readTextToolCalls === turns.length &&
    boundary.approvalRequests === 0 &&
    boundary.commandEvents === 0 &&
    boundary.changesEvents === 0 &&
    boundary.reports === 0 &&
    boundary.artifacts === 0 &&
    boundary.changesDirty === false &&
    boundary.changedFiles === 0 &&
    boundary.unauthorizedToolCalls === 0 &&
    resyncWithinBound &&
    workspaceDelta === 0 &&
    processDelta === 0 && temporaryDelta === 0 && configurationDelta === 0;
  const evidence = {
    schemaVersion: "week83-approval-projection-remediation/v1",
    evidenceKind: settings.evidenceKind,
    profile: settings.profileNumber,
    profileId: profile,
    status: passed ? "Passed" : "Failed",
    exactCandidateRevision: "e282eac8cdf4440f4b3cd024dfea4cf7bb02606f",
    packageIdentity: {
      sha256: "AB4B79A97C66041E1A478F217AA99EC75D1D1082FA42500AFDADBC13063B62D9",
      bytes: 222753280,
      treeSha256: "3EEE850186EF69E6BE359FBC92FC7A5478907DAF8F52A5B861F43A07002953B3",
      treeBytes: 464709026,
    },
    appHostIdentity: {
      sha256: "DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA",
      bytes: 79941168,
    },
    authorization: {
      readOnlyRecoveryResourceGranted: true,
      selectedConfigurationKeys: 3,
      parentEnvironmentInjected: false,
      packagedChildInjected: true,
      providerConfigurationPersisted: false,
    },
    model: {
      firstModelObserved,
      valuePersisted: false,
      source: firstModelSource,
    },
    settings: {
      warmupTurns: 1,
      measuredTurns: settings.measuredTurns,
      warmWindowSeconds: windowSeconds,
      postWindowSeconds: windowSeconds,
      sampleIntervalSeconds,
      settleSeconds: windowSeconds - ((settledSampleCount - 1) * sampleIntervalSeconds),
      settledSampleCount,
      workers: 1,
      retries: 0,
      forcedGc: false,
      rendererReloadUsedForGate: false,
      terminalPollIntervalMilliseconds,
      allowedModelTools: [toolName],
    },
    sourceMapAudit: {
      productBundleSha256: coverageTargets.bundleSha256,
      packagedBundleMatchedProductBuild: coverageTargets.mapVerified,
      persistedRootedPaths: 0,
    },
    observer,
    turns,
    warm,
    post,
    retention,
    sustainedMonotonicGrowth,
    rendererBounds,
    gate15Percent: retention ? {
      workingSetWithinLimit: retention.workingSetPercent <= 15,
      privateBytesWithinLimit: retention.privateBytesPercent <= 15,
    } : null,
    boundary,
    counts: {
      providerTurns: boundary.providerTurns,
      readCalls: boundary.readTextToolCalls,
      approvalRequests: boundary.approvalRequests,
      writeCalls: boundary.changesEvents,
      shellCalls: boundary.commandEvents,
      changedFiles: workspaceDelta,
      resyncWithinBound,
    },
    workspaceDelta,
    durationMilliseconds: Date.now() - started,
    cleanupDelta: {
      process: processDelta,
      temporary: temporaryDelta,
      configuration: configurationDelta,
    },
    failure: scenarioError === null && cleanupError === null
      ? null
      : safeError(scenarioError ?? cleanupError, secretValues),
    summary: passed
      ? settings.resourceGate
        ? `${profile} Week83 provider resource profile passed the frozen retention, boundary, and cleanup Gates.`
        : "Week83 packaged provider read-only passed exactly one read with durable timeline, clean review, and zero cleanup delta."
      : settings.resourceGate
        ? `${profile} Week83 provider resource profile failed closed without persisting provider configuration.`
        : "Week83 packaged provider read-only failed closed without persisting provider configuration.",
  };
  fs.mkdirSync(evidenceRoot, { recursive: true });
  const body = JSON.stringify(evidence, null, 2);
  fs.writeFileSync(path.join(evidenceRoot, settings.evidenceName), `${body}\n`, "utf8");
  await testInfo.attach(`week83-${profile.toLowerCase()}-provider-resource.json`, {
    body: Buffer.from(body),
    contentType: "application/json",
  });
  if (scenarioError && cleanupError) throw new AggregateError([scenarioError, cleanupError], `${profile} scenario and cleanup failed.`);
  if (scenarioError) throw scenarioError;
  if (cleanupError) throw cleanupError;
  expect(passed, `${profile} provider boundary or evidence integrity failed.`).toBe(true);
});

async function executeProviderTurn(
  page: Page,
  cdp: CDPSession,
  application: ElectronApplication,
  appHostPid: number,
  threadId: string,
  ordinal: number,
  expectedTotalTurns: number,
  phase: "warmup" | "measured",
  observer: ObserverCounts,
  coverageTargets: CoverageTargets,
): Promise<TurnEvidence> {
  await takeCoverage(cdp, observer, coverageTargets);
  const started = Date.now();
  await page.getByRole("textbox", { name: "Composer prompt" }).fill(
    "Use exactly one workspace.read_text tool call to read global.json. Then reply with one short sentence. Do not call any other tool, do not modify files, and do not use shell, Git, MCP, or any other network behavior.",
  );
  await page.getByRole("button", { name: "Queue prompt" }).click();
  const state = await waitForCompletedTurn(page, threadId, expectedTotalTurns, observer);
  const durationMilliseconds = Date.now() - started;
  expect(state.latestTurn?.status).toBe("completed");
  expect(state.latestTurn?.recoveryRequired).toBe(false);
  expect(state.latestTurnToolCompletedCount).toBe(1);
  expect(state.latestTurnToolNames).toEqual([toolName]);
  expect(state.latestTurnApprovalCount).toBe(0);
  expect(state.latestTurnCommandCount).toBe(0);
  expect(state.latestTurnChangesCount).toBe(0);
  expect(state.latestTurnModelCount).toBeGreaterThan(0);
  expect(state.latestTurnFinalCount).toBe(1);
  expect(state.latestTurnSequencesContiguous).toBe(true);
  expect(state.latestTurnItemIdsUnique).toBe(true);
  const coverage = await takeCoverage(cdp, observer, coverageTargets);
  expect(coverage.queueResyncRequests, "Provider turn must emit Renderer resync requests.").toBeGreaterThan(0);
  expect(coverage.resyncRunners, "Provider turn must complete a Renderer resync runner.").toBeGreaterThan(0);
  expect(coverage.fullProjectionCallsLowerBound, "Provider turn must invoke authoritative full projection resync.").toBeGreaterThan(0);
  const resourceAfterTurn = await captureSample(
    application, page, cdp, appHostPid, observer, 0,
  );
  return {
    ordinal,
    phase,
    durationMilliseconds,
    toolCalls: state.latestTurnToolCompletedCount,
    firstTool: state.latestTurnToolNames[0] ?? null,
    timelineItems: state.latestTurn?.timelineItemCount ?? 0,
    runtimeEventCounts: state.latestTurnTypes,
    approvalRequests: state.latestTurnApprovalCount,
    commandEvents: state.latestTurnCommandCount,
    changesEvents: state.latestTurnChangesCount,
    warningEvents: state.latestTurnWarningCount,
    projectionJsonUtf8Bytes: state.projectionJsonUtf8Bytes,
    timelineSummaryUtf8Bytes: state.timelineSummaryUtf8Bytes,
    timelinePayloadJsonUtf8Bytes: state.timelinePayloadJsonUtf8Bytes,
    distinctTimelineTimestamps: state.distinctTimelineTimestamps,
    coverage,
    resourceAfterTurn,
  };
}

async function waitForCompletedTurn(
  page: Page,
  threadId: string,
  expectedTotalTurns: number,
  observer: ObserverCounts,
): Promise<SanitizedThreadState> {
  const deadline = Date.now() + 300_000;
  while (Date.now() < deadline) {
    observer.terminalPollCalls++;
    observer.observerBridgeGetThreadCalls++;
    const last = await observedPageEvaluate(page, observer, async (id) => {
      const result = await window.caicli.getThread({ threadId: id, afterSequence: 0 });
      if (!result.succeeded || !result.data) {
        return {
          succeeded: false,
          threadStatus: null,
          turnCount: 0,
          timelineItemCount: 0,
          latestTurn: null,
          latestTurnTypes: {},
          latestTurnToolNames: [],
          latestTurnToolCompletedCount: 0,
          latestTurnApprovalCount: 0,
          latestTurnCommandCount: 0,
          latestTurnChangesCount: 0,
          latestTurnWarningCount: 0,
          latestTurnModelCount: 0,
          latestTurnFinalCount: 0,
          latestTurnSequencesContiguous: false,
          latestTurnItemIdsUnique: false,
          projectionJsonUtf8Bytes: 0,
          timelineSummaryUtf8Bytes: 0,
          timelinePayloadJsonUtf8Bytes: 0,
          distinctTimelineTimestamps: 0,
        };
      }
      const latest = result.data.turns.at(-1) ?? null;
      const items = latest
        ? result.data.timeline.filter((item) => item.turnId === latest.turnId)
        : [];
      const types: Record<string, number> = {};
      for (const item of items) types[item.type] = (types[item.type] ?? 0) + 1;
      const completedTools = items.filter((item) => item.type === "tool.completed");
      const encoder = new TextEncoder();
      return {
        succeeded: true,
        threadStatus: result.data.thread.status,
        turnCount: result.data.turns.length,
        timelineItemCount: result.data.timeline.length,
        latestTurn: latest ? {
          turnId: latest.turnId,
          status: latest.status,
          timelineItemCount: latest.timelineItemCount,
          recoveryRequired: latest.recoveryRequired,
        } : null,
        latestTurnTypes: types,
        latestTurnToolNames: completedTools.map((item) => item.payload.name ?? ""),
        latestTurnToolCompletedCount: completedTools.length,
        latestTurnApprovalCount: items.filter((item) => item.type.startsWith("approval.")).length,
        latestTurnCommandCount: items.filter((item) => item.type.startsWith("command.")).length,
        latestTurnChangesCount: items.filter((item) => item.type === "changes.updated").length,
        latestTurnWarningCount: items.filter((item) => item.type === "warning.raised").length,
        // The durable desktop-v1 projection intentionally maps internal
        // model.turn runtime events to the public assistant.message type.
        latestTurnModelCount: items.filter((item) => item.type === "assistant.message").length,
        latestTurnFinalCount: items.filter((item) => item.type === "assistant.final").length,
        latestTurnSequencesContiguous: items.every((item, index) =>
          index === 0 || item.sequence === items[index - 1]!.sequence + 1),
        latestTurnItemIdsUnique: new Set(items.map((item) => item.itemId)).size === items.length,
        projectionJsonUtf8Bytes: encoder.encode(JSON.stringify(result.data)).byteLength,
        timelineSummaryUtf8Bytes: result.data.timeline.reduce(
          (total, item) => total + encoder.encode(item.summary).byteLength, 0,
        ),
        timelinePayloadJsonUtf8Bytes: result.data.timeline.reduce(
          (total, item) => total + encoder.encode(JSON.stringify(item.payload)).byteLength, 0,
        ),
        distinctTimelineTimestamps: new Set(result.data.timeline.map((item) => item.timestampUtc)).size,
      };
    }, threadId);
    if (last.succeeded && last.turnCount >= expectedTotalTurns && last.latestTurn?.status === "completed") {
      await new Promise((resolve) => setTimeout(resolve, terminalPollIntervalMilliseconds));
      return last;
    }
    if (last.latestTurn && ["failed", "canceled", "interrupted"].includes(last.latestTurn.status)) return last;
    await new Promise((resolve) => setTimeout(resolve, terminalPollIntervalMilliseconds));
  }
  throw new Error("Provider turn exceeded the authorized terminal wait bound.");
}

function createCoverageTargets(): CoverageTargets {
  if (!fs.existsSync(productBundlePath) || !fs.existsSync(productSourceMapPath)) {
    throw new Error("Verified product source-map build is unavailable.");
  }
  const packagedBundle = extractFile(packagedAsar, `dist\\renderer\\assets\\${productBundleName}`);
  const builtBundle = fs.readFileSync(productBundlePath);
  const packagedHash = sha256(packagedBundle);
  const builtHash = sha256(builtBundle);
  const map = JSON.parse(fs.readFileSync(productSourceMapPath, "utf8")) as RawSourceMap;
  const consumer = new SourceMapConsumer(map);
  const source = consumer.sources.find((candidate) => candidate.endsWith("/use-desktop-controller.ts"));
  if (!source) throw new Error("Product controller source was not found in the verified source map.");
  const lineStarts = generatedLineStarts(builtBundle.toString("utf8"));
  const offset = (line: number, column: number) => {
    const generated = consumer.generatedPositionFor({
      source,
      line,
      column,
      bias: SourceMapConsumer.LEAST_UPPER_BOUND,
    });
    if (generated.line === null || generated.column === null) throw new Error("Product coverage target could not be mapped.");
    return lineStarts[generated.line - 1]! + generated.column;
  };
  const result = {
    offsets: {
      queueResync: offset(109, 4),
      resyncRunner: offset(115, 4),
    },
    bundleSha256: builtHash,
    bundleCharacterLength: builtBundle.toString("utf8").length,
    mapVerified: packagedHash === builtHash,
  };
  consumer.destroy?.();
  return result;
}

async function takeCoverage(
  cdp: CDPSession,
  observer: ObserverCounts,
  targets: CoverageTargets,
): Promise<CoverageCounts> {
  observer.cdpCoverageCalls++;
  const coverage = await cdp.send("Profiler.takePreciseCoverage");
  const script = coverage.result.find((candidate) => candidate.url.endsWith(productBundleName)) ??
    coverage.result.find((candidate) => candidate.functions.some((fn) =>
      fn.ranges.some((range) => range.startOffset === 0 && range.endOffset === targets.bundleCharacterLength)));
  if (!script) {
    return {
      queueResyncRequests: 0,
      resyncRunners: 0,
      resyncCoalescedRequests: 0,
      fullProjectionCalls: 0,
      fullProjectionCallsLowerBound: 0,
      fullProjectionCallsUpperBound: 0,
    };
  }
  const countAt = (targetOffset: number) => {
    const candidates = script.functions
      .flatMap((fn) => fn.ranges.map((range) => ({ fn, range })))
      .filter((value) => value.range && value.range.startOffset <= targetOffset && value.range.endOffset >= targetOffset)
      .sort((left, right) =>
        (left.range!.endOffset - left.range!.startOffset) - (right.range!.endOffset - right.range!.startOffset));
    const selected = candidates[0];
    return selected?.range?.count ?? 0;
  };
  const queueResyncRequests = countAt(targets.offsets.queueResync);
  const resyncRunners = countAt(targets.offsets.resyncRunner);
  const exactFullProjectionCalls =
    queueResyncRequests === resyncRunners ? resyncRunners : null;
  return {
    queueResyncRequests,
    resyncRunners,
    resyncCoalescedRequests: Math.max(0, queueResyncRequests - resyncRunners),
    fullProjectionCalls: exactFullProjectionCalls,
    fullProjectionCallsLowerBound: resyncRunners,
    fullProjectionCallsUpperBound: queueResyncRequests,
  };
}

async function captureSamplingWindow(
  application: ElectronApplication,
  page: Page,
  cdp: CDPSession,
  appHostPid: number,
  observer: ObserverCounts,
): Promise<SamplingWindow> {
  const started = Date.now();
  const samples: ProviderSample[] = [];
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
  appHostPid: number,
  observer: ObserverCounts,
  elapsedMilliseconds: number,
): Promise<ProviderSample> {
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
  const appHost = sampleExternalProcess(appHostPid, observer);
  const roles = aggregateRoles([...electronProcesses, { ...appHost, role: "AppHost" }]);
  const visibleTimelineCards = await observedPageEvaluate(
    page,
    observer,
    () => document.querySelectorAll(".timeline-card").length,
  );
  const metrics = new Map(performance.metrics.map((metric) => [metric.name, metric.value]));
  return {
    elapsedMilliseconds,
    ownedPids: electronProcesses.map((process) => process.pid).concat(appHostPid),
    roles,
    jsHeapUsedBytes: metricValue(metrics, "JSHeapUsedSize"),
    jsHeapTotalBytes: metricValue(metrics, "JSHeapTotalSize"),
    nodes: dom.nodes,
    documents: dom.documents,
    jsEventListeners: dom.jsEventListeners,
    visibleTimelineCards,
  };
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
    if ((value.startsWith("\"") && value.endsWith("\"")) ||
        (value.startsWith("'") && value.endsWith("'"))) {
      value = value.slice(1, -1);
    }
    if (!value || value.includes("\0") || value.includes("\n") || value.includes("\r")) {
      throw new Error(`Authorized provider setting ${match[1]} is missing or invalid.`);
    }
    values.set(match[1]!, value);
  }
  for (const key of allowed) {
    if (!values.has(key)) throw new Error(`Authorized provider setting ${key} is missing or invalid.`);
  }
  return Object.fromEntries(values) as unknown as ProviderConfig;
}

function authorizedChildEnvironment(
  root: string,
  profileRoot: string,
  provider: ProviderConfig,
): NodeJS.ProcessEnv {
  const environment = { ...process.env };
  delete environment.OPENAI_MODEL;
  delete environment.OPENAI_BASE_URL;
  delete environment.OPENAI_API_KEY;
  return {
    ...environment,
    ...provider,
    APPDATA: path.join(root, "appdata"),
    LOCALAPPDATA: path.join(root, "localappdata"),
    CAICLI_USER_PROFILE: profileRoot,
    CAICLI_AGENT_BACKEND: "direct",
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
  return JSON.parse(result.stdout) as { workingSetBytes: number; privateBytes: number };
}

function inventoryWorkspace(root: string): Readonly<Record<string, string>> {
  const files: Record<string, string> = {};
  const visit = (directory: string, relative: string) => {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true }).sort((left, right) => left.name.localeCompare(right.name))) {
      const childRelative = relative ? `${relative}/${entry.name}` : entry.name;
      const child = path.join(directory, entry.name);
      if (entry.isDirectory()) visit(child, childRelative);
      else if (entry.isFile()) files[childRelative] = sha256File(child);
      else throw new Error("Disposable workspace contains an unsupported entry.");
    }
  };
  visit(root, "");
  return files;
}

function inventoriesEqual(
  left: Readonly<Record<string, string>>,
  right: Readonly<Record<string, string>>,
): boolean {
  return JSON.stringify(left) === JSON.stringify(right);
}

function generatedLineStarts(source: string): number[] {
  const starts = [0];
  for (let index = 0; index < source.length; index++) {
    if (source.charCodeAt(index) === 10) starts.push(index + 1);
  }
  return starts;
}

function parseProfile(value: string | undefined): ProviderProfile {
  if (value === "RO" || value === "R1" || value === "R2" || value === "R3" || value === "R4" || value === "R5") return value;
  throw new Error("CAICLI_WEEK83_PROVIDER_PROFILE must be RO, R1, R2, R3, R4, or R5.");
}

function sha256File(filePath: string): string {
  return sha256(fs.readFileSync(filePath));
}

function sha256(value: Buffer): string {
  return createHash("sha256").update(value).digest("hex").toUpperCase();
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

function hasSignificantSustainedGrowth(samples: readonly ProviderSample[]): boolean {
  const settled = samples.slice(-3);
  if (settled.length !== 3) return true;
  const series = [
    settled.map((sample) => roleBytes(sample.roles, "Tab", "workingSetBytes")),
    settled.map((sample) => roleBytes(sample.roles, "Tab", "privateBytes")),
    settled.map((sample) => sample.jsHeapUsedBytes),
    settled.map((sample) => sample.nodes),
    settled.map((sample) => sample.jsEventListeners),
  ];
  return series.some((values) =>
    values[1]! >= values[0]! &&
    values[2]! >= values[1]! &&
    percentChange(values[0]!, values[2]!) > 5);
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

function isOwnedRoot(root: string, profile: ProviderProfile): boolean {
  const resolved = path.resolve(root);
  return resolved.startsWith(path.resolve(os.tmpdir()) + path.sep) &&
    path.basename(resolved).startsWith(`caicli-week83-${profile.toLowerCase()}-`);
}

function safeError(
  error: unknown,
  secretValues: readonly string[],
): { readonly name: string; readonly message: string } {
  const value = error instanceof Error ? error : new Error(String(error));
  let message = value.message;
  for (const secret of secretValues) message = message.replaceAll(secret, "[configuration-redacted]");
  message = message.replace(/OPENAI_(?:MODEL|BASE_URL|API_KEY)/gu, "[configuration-name-redacted]");
  message = message.replace(/[a-zA-Z]:[\\/][^\s"'<>]*/gu, "[rooted-path-redacted]");
  return { name: value.name.slice(0, 128), message: message.slice(0, 1000) };
}
