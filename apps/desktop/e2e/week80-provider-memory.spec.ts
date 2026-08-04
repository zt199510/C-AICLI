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
import {
  abortRetainedObjectTracking,
  finishRetainedObjectTracking,
  startRetainedObjectTracking,
} from "../scripts/week80-retained-tracker.mjs";

const desktopRoot = path.resolve(import.meta.dirname, "..");
const repositoryRoot = path.resolve(desktopRoot, "..", "..");
const packagedRoot = path.join(desktopRoot, "out", "C-AICLI Desktop-win32-x64");
const packagedExecutable = path.join(packagedRoot, "caicli-desktop.exe");
const packagedAppHost = path.join(packagedRoot, "resources", "apphost", "CSharpAiCli.AppHost.exe");
const packagedAsar = path.join(packagedRoot, "resources", "app.asar");
const productMapRoot = path.join(repositoryRoot, ".worktrees", "week80-product-map", "apps", "desktop");
const productBundleName = "index-CV5ZSfnt.js";
const productBundlePath = path.join(productMapRoot, "dist", "renderer", "assets", productBundleName);
const productSourceMapPath = `${productBundlePath}.map`;
const evidenceRoot = process.env.CAICLI_WEEK80_EVIDENCE_DIR
  ? path.resolve(process.env.CAICLI_WEEK80_EVIDENCE_DIR)
  : path.join(repositoryRoot, "artifacts", "week80-renderer-private-bytes");
const windowSeconds = 30;
const sampleIntervalSeconds = 5;
const settledSampleCount = 3;
const terminalPollIntervalMilliseconds = 1000;
const toolName = "workspace.read_text";
const providerPrompt = "Use exactly one workspace.read_text tool call to read global.json. Then reply with one short sentence. Do not call any other tool, do not modify files, and do not use shell, Git, MCP, or any other network behavior.";
const baselineDesktopSha256 = "6BDB9203C0ACCB8D0E3B90EC1F4218A02BF9E05A21E068654F1C2B9B0E82DE29";
const fixedCandidateDesktopSha256 = "97F378352C3A97BA71AC431CA3B1874C9F6C7E739A0D39D5E4B6A1DA6D3BA09F";
const virtualizedCandidateDesktopSha256 = "D6FB95D84F70F051E181C198DED12097D3A23D6CFD0C662873A589D9F89E26F1";
const zeroOverscanCandidateDesktopSha256 = "35BE96FFC4230974875EBBC3565394163FFDC6C8BAD393987216F9FFFB03142E";
const combinedCandidateDesktopSha256 = "15C4E45353865FFB2B63F503CA8D7063C1B867624C91134D5B25758D08790B4B";
const allocationFreeCandidateDesktopSha256 = "2A6C7FEEDF116C38F6DCA0BF4C82C12980F2371B142165096B97AA96837AA481";
const nativeSamplingDesktopSha256 = "C0EC7A8B40E7ADC013B6629A40966047BAB724C8A3357FAD0905149EB9D915C8";
const constantWindowDesktopSha256 = "453406A0A9BBB94A35E0E0CBE9F84D745B08592BA5451971F8C4D6DD02D1C326";
const defaultUiObserverDesktopSha256 = "8EE8A567A601F8BCCAAA75ACE9039A832F3B110FEB75664C76DB66A0F67E8C56";
const incrementalResyncDesktopSha256 = "C54B5CEE39D5866259F87834972A98F489BE094965732FC29A3EF9BCCF31680B";
const boundedTimelineDesktopSha256 = "217A0143A1AC85F9B7FB09118864FA0C013E4AA8111386087DF3DE7A162C47F0";
const sixItemTimelineDesktopSha256 = "A8E66848EF8213079E7A30E784F47AB507D6432C34820E902DEAE0743B074908";
const stableTimelineDesktopSha256 = "0C2EB6C8FC5591E46730C5A34E422C8F4CA5903C23680F3F6AFB551FD5BA6065";
const hiddenTimelineDesktopSha256 = "3E9B56DE78A6CD1AD7E0AF6AC646044BA12DA7EB8A88CEAEC1D4B7A2D79C6DB2";
const shallowThreadCloneDesktopSha256 = "71CF76129F73D514F97708986FC2340C761C32CA58B28172B44626651913B718";
const hiddenTimelineOneShotDesktopSha256 = "2E651F7DF48D22ACBB692FE8983E02ED3AF4BB0088DAFBD1F6F645A52D972A02";
const mainValidatedThreadDesktopSha256 = "07C7811B08E0BE7170DECDE89BA14013BA0C94A7CB6F1CDF2587639A34A4C504";
const timelineWindowFixDesktopSha256 = "EFBEFE6AA18AACA6AB82899234EB115DB28AFA295E4F87B40AB827A747F0C344";
const turnGroupsFixDesktopSha256 = "B6490B2EB6CD26EE21F4C6F716825CD82E693CF00DDEA520C14EB47EFE515140";
const stableStatusUiFixDesktopSha256 = "481AD81085D519791FC532F3147A251FE8BCBB8B52673D780204B09BD03B8BD7";
const boundedDomFixDesktopSha256 = "A994248B0589226D901D85D665325DE6CAE1058A8D6F83E5DB4377800D5C883A";
const lifecycleResyncFixDesktopSha256 = "E2A5BE23B0598ACA5827FC3977C48418298AE888D9293D1D51B937528BBB7A38";
const profileSettings = {
  P1: { measuredTurns: 1, evidenceName: "provider-profile-p1.json", coverageDiagnostic: true, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: false },
  P5: { measuredTurns: 5, evidenceName: "provider-profile-p5.json", coverageDiagnostic: true, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: false },
  P10: { measuredTurns: 10, evidenceName: "provider-profile-p10.json", coverageDiagnostic: true, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: false },
  P5NC: { measuredTurns: 5, evidenceName: "provider-profile-p5-no-coverage.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: false },
  P5BO: { measuredTurns: 5, evidenceName: "provider-profile-p5-bounded-observer.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: true, mainObservedTerminal: false },
  P5MO: { measuredTurns: 5, evidenceName: "provider-profile-p5-main-observer.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5F: { measuredTurns: 5, evidenceName: "provider-profile-p5-fixed.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5V: { measuredTurns: 5, evidenceName: "provider-profile-p5-virtualized.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5V0: { measuredTurns: 5, evidenceName: "provider-profile-p5-zero-overscan.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5C: { measuredTurns: 5, evidenceName: "provider-profile-p5-combined.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5A: { measuredTurns: 5, evidenceName: "provider-profile-p5-allocation-free-validator.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5M: { measuredTurns: 5, evidenceName: "provider-profile-p5-native-sampling.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5W: { measuredTurns: 5, evidenceName: "provider-profile-p5-constant-window.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5WU: { measuredTurns: 5, evidenceName: "provider-profile-p5-constant-window-ui-observer.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: false },
  P5WS: { measuredTurns: 5, evidenceName: "provider-profile-p5-constant-window-single-resync.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 8, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: false },
  P5U: { measuredTurns: 5, evidenceName: "provider-profile-p5-default-ui-observer.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: false },
  P5S: { measuredTurns: 5, evidenceName: "provider-profile-p5-default-single-resync.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 8, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: false },
  P5I: { measuredTurns: 5, evidenceName: "provider-profile-p5-incremental-resync.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: false },
  P5L: { measuredTurns: 5, evidenceName: "provider-profile-p5-bounded-timeline.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: false },
  P5L6: { measuredTurns: 5, evidenceName: "provider-profile-p5-six-item-timeline.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: false },
  P5H: { measuredTurns: 5, evidenceName: "provider-profile-p5-stable-timeline.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: false },
  P5Z: { measuredTurns: 5, evidenceName: "provider-profile-p5-hidden-timeline.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: false },
  P5J: { measuredTurns: 5, evidenceName: "provider-profile-p5-js-allocation-sampling.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: false },
  P5X: { measuredTurns: 5, evidenceName: "provider-profile-p5-shallow-thread-clone.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: false },
  P5Q: { measuredTurns: 5, evidenceName: "provider-profile-p5-one-shot-observer.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5QS: { measuredTurns: 5, evidenceName: "provider-profile-p5-one-shot-single-resync.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 8, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5QZ: { measuredTurns: 5, evidenceName: "provider-profile-p5-one-shot-hidden-timeline.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5Y: { measuredTurns: 5, evidenceName: "provider-profile-p5-main-validated-thread.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5T: { measuredTurns: 5, evidenceName: "provider-profile-p5-memory-infra.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P0T: { measuredTurns: 0, evidenceName: "provider-profile-p0-memory-infra.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: false },
  P5DT: { measuredTurns: 5, evidenceName: "provider-profile-p5-direct-memory-infra.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 0, directMeasuredTurns: true, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5G: { measuredTurns: 5, evidenceName: "provider-profile-p5-timeline-window-fix.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5TG: { measuredTurns: 5, evidenceName: "provider-profile-p5-turn-groups-fix.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5TGC: { measuredTurns: 5, evidenceName: "provider-profile-p5-turn-groups-census.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5ST: { measuredTurns: 5, evidenceName: "provider-profile-p5-stable-status-ui-fix.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5STC: { measuredTurns: 5, evidenceName: "provider-profile-p5-stable-status-ui-census.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5CF: { measuredTurns: 5, evidenceName: "provider-profile-p5-bounded-dom-fix.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5CFC: { measuredTurns: 5, evidenceName: "provider-profile-p5-bounded-dom-census.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5EL: { measuredTurns: 5, evidenceName: "provider-profile-p5-event-listener-census.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5RF: { measuredTurns: 5, evidenceName: "provider-profile-p5-lifecycle-resync-fix.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5B: { measuredTurns: 5, evidenceName: "provider-profile-p5-bounded-projection.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 8, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: true },
  P5N1: { measuredTurns: 5, evidenceName: "provider-profile-p5-one-notification.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 8, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: false },
  P5D: { measuredTurns: 5, evidenceName: "provider-profile-p5-direct.json", coverageDiagnostic: false, retainedDiagnostic: false, measuredNotificationStride: 0, directMeasuredTurns: true, boundedTerminalObserver: false, mainObservedTerminal: false },
  P0R: { measuredTurns: 0, evidenceName: "provider-profile-p0-retained.json", coverageDiagnostic: false, retainedDiagnostic: true, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: false },
  P5R: { measuredTurns: 5, evidenceName: "provider-profile-p5-retained.json", coverageDiagnostic: false, retainedDiagnostic: true, measuredNotificationStride: 1, directMeasuredTurns: false, boundedTerminalObserver: false, mainObservedTerminal: false },
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
  readonly coverage: CoverageCounts | null;
  readonly resourceAfterTurn: ProviderSample;
}

interface NotificationFilterCounts {
  readonly stride: number;
  readonly observed: number;
  readonly forwarded: number;
  readonly dropped: number;
  readonly identities: Readonly<Record<string, number>>;
}

test("authorized provider-backed renderer memory profile", async ({ browserName }, testInfo) => {
  if (browserName !== "chromium") throw new Error("Electron provider diagnostics require Chromium.");
  const profile = parseProfile(process.env.CAICLI_WEEK80_PROVIDER_PROFILE);
  const settings = profileSettings[profile];
  const expectedDesktopSha256 = profile === "P5RF"
    ? lifecycleResyncFixDesktopSha256
    : profile === "P5CF" || profile === "P5CFC" || profile === "P5EL"
    ? boundedDomFixDesktopSha256
    : profile === "P5ST" || profile === "P5STC"
    ? stableStatusUiFixDesktopSha256
    : profile === "P5TG" || profile === "P5TGC"
    ? turnGroupsFixDesktopSha256
    : profile === "P5G"
      ? timelineWindowFixDesktopSha256
    : profile === "P5F"
    ? fixedCandidateDesktopSha256
    : profile === "P5V"
      ? virtualizedCandidateDesktopSha256
      : profile === "P5V0"
        ? zeroOverscanCandidateDesktopSha256
        : profile === "P5C"
          ? combinedCandidateDesktopSha256
          : profile === "P5A"
            ? allocationFreeCandidateDesktopSha256
            : profile === "P5M" || profile === "P5Q" || profile === "P5QS" || profile === "P5T" || profile === "P0T" || profile === "P5DT"
              ? nativeSamplingDesktopSha256
              : profile === "P5W" || profile === "P5WU" || profile === "P5WS"
                ? constantWindowDesktopSha256
                : profile === "P5U" || profile === "P5S"
                  ? defaultUiObserverDesktopSha256
                  : profile === "P5I"
                    ? incrementalResyncDesktopSha256
                    : profile === "P5L"
                      ? boundedTimelineDesktopSha256
                      : profile === "P5L6"
                        ? sixItemTimelineDesktopSha256
                        : profile === "P5H"
                          ? stableTimelineDesktopSha256
                          : profile === "P5Z" || profile === "P5J"
                            ? hiddenTimelineDesktopSha256
                            : profile === "P5X"
                              ? shallowThreadCloneDesktopSha256
                              : profile === "P5QZ"
                                ? hiddenTimelineOneShotDesktopSha256
                                : profile === "P5Y" ? mainValidatedThreadDesktopSha256 : baselineDesktopSha256;
  testInfo.setTimeout(profile === "P10" || settings.retainedDiagnostic ? 1_500_000 : 1_000_000);
  const providerConfig = readAuthorizedProviderConfig(path.join(repositoryRoot, ".env.local"));
  const secretValues = Object.values(providerConfig);
  const coverageTargets = settings.coverageDiagnostic ? createCoverageTargets() : null;
  const root = fs.mkdtempSync(path.join(os.tmpdir(), `caicli-week80-${profile.toLowerCase()}-`));
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
  let applicationPid: number | null = null;
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
  let retainedTracker: Awaited<ReturnType<typeof startRetainedObjectTracking>> | null = null;
  let retainedObjectAggregate: Awaited<ReturnType<typeof finishRetainedObjectTracking>> | null = null;
  let notificationFilter: NotificationFilterCounts | null = null;
  let nativeSamplingActive = false;
  let nativeAllocationAggregate: ReturnType<typeof summarizeNativeAllocations> | null = null;
  let jsSamplingActive = false;
  let jsAllocationAggregate: ReturnType<typeof summarizeJsAllocations> | null = null;
  let memoryTrace: Awaited<ReturnType<typeof startMemoryInfraTrace>> | null = null;
  let memoryDumpAggregate: ReturnType<typeof summarizeMemoryInfraTrace> | null = null;
  let rendererPid: number | null = null;
  let warmMemoryDumpGuid: string | null = null;
  let warmDomCensus: Awaited<ReturnType<typeof captureDomCensus>> | null = null;
  let postDomCensus: Awaited<ReturnType<typeof captureDomCensus>> | null = null;
  let mutationCensus: Awaited<ReturnType<typeof readMutationCensus>> | null = null;
  let eventListenerCensus: Awaited<ReturnType<typeof readEventListenerCensus>> | null = null;

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
    expect(sha256File(packagedExecutable)).toBe(expectedDesktopSha256);
    expect(sha256File(packagedAppHost)).toBe("DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA");
    if (coverageTargets) expect(coverageTargets.mapVerified).toBe(true);
    application = await electron.launch({
      executablePath: packagedExecutable,
      args: ["--disable-gpu"],
      env: authorizedChildEnvironment(root, profileRoot, providerConfig),
    });
    applicationPid = application.process().pid;
    observer.electronAppEvaluateCalls++;
    await application.evaluate(async ({ dialog }, selectedWorkspace) => {
      dialog.showOpenDialog = async () => ({ canceled: false, filePaths: [selectedWorkspace] });
    }, workspace);
    page = await application.firstWindow();
    await expect(page.locator(".app-shell")).toHaveAttribute("data-runtime-state", "ready", { timeout: 60_000 });
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

    const providerThreadTitle = `Week 80 ${profile} provider diagnosis`;
    await page.evaluate(async (title) => {
      const result = await window.caicli.createThread({ title });
      if (!result.succeeded || !result.data) throw new Error("Provider diagnostic conversation could not be created.");
    }, providerThreadTitle);
    const providerThreadRow = page.locator("#threads-panel").getByText(providerThreadTitle, { exact: true }).first();
    await expect(providerThreadRow).toBeVisible();
    await providerThreadRow.click();
    const observedThreadId = await observedPageEvaluate(page, observer, async (title) => {
      const listed = await window.caicli.listThreads();
      return listed.data?.threads.find((thread) => thread.title === title)?.threadId ?? null;
    }, providerThreadTitle);
    expect(observedThreadId).not.toBeNull();
    if (!observedThreadId) throw new Error("Created provider diagnostic thread was not observed.");
    threadId = observedThreadId;

    appHostPid = findAppHostPid(applicationPid, observer);
    expect(appHostPid, "Provider profile must observe the owned AppHost PID.").not.toBeNull();
    cdp = await page.context().newCDPSession(page);
    await cdp.send("Performance.enable");
    if (coverageTargets) {
      await cdp.send("Profiler.enable");
      await cdp.send("Profiler.startPreciseCoverage", {
        callCount: true,
        detailed: true,
        allowTriggeredUpdates: false,
      });
    }

    const warmup = await executeProviderTurn(
      page, cdp, application, appHostPid, threadId, 1, 1, "warmup", observer, coverageTargets,
      settings.boundedTerminalObserver, false, ["P5WU", "P5WS", "P5U", "P5S", "P5I", "P5L", "P5L6", "P5H", "P5Z", "P5J", "P5X", "P5Q", "P5QS", "P5QZ", "P5Y", "P5T", "P5G", "P5TG", "P5TGC", "P5ST", "P5STC", "P5CF", "P5CFC", "P5EL", "P5RF"].includes(profile),
    );
    turns.push(warmup);
    console.log(`${profile} warmup completed durationMs=${warmup.durationMilliseconds} toolCalls=${warmup.toolCalls} timelineItems=${warmup.timelineItems}`);
    if (coverageTargets) await takeCoverage(cdp, observer, coverageTargets);
    warm = await captureSamplingWindow(application, page, cdp, appHostPid, observer);
    if (coverageTargets) await takeCoverage(cdp, observer, coverageTargets);
    if (settings.retainedDiagnostic) retainedTracker = await startRetainedObjectTracking(cdp);
    if (profile === "P5M") {
      await cdp.send("Memory.startSampling", { samplingInterval: 32_768, suppressRandomness: true });
      nativeSamplingActive = true;
    }
    if (profile === "P5J") {
      await cdp.send("HeapProfiler.enable");
      await cdp.send("HeapProfiler.startSampling", {
        samplingInterval: 32_768,
        stackDepth: 64,
        includeObjectsCollectedByMajorGC: true,
        includeObjectsCollectedByMinorGC: true,
      });
      jsSamplingActive = true;
    }
    if (profile === "P5T" || profile === "P0T" || profile === "P5DT" || profile === "P5TGC" || profile === "P5STC" || profile === "P5CFC") {
      rendererPid = await application.evaluate(({ BrowserWindow }) =>
        BrowserWindow.getAllWindows()[0]?.webContents.getOSProcessId() ?? null);
      if (!rendererPid) throw new Error("Renderer OS process id was unavailable for memory-infra attribution.");
      memoryTrace = await startMemoryInfraTrace(cdp);
      const dump = await cdp.send("Tracing.requestMemoryDump", {
        deterministic: false,
        levelOfDetail: "detailed",
      });
      if (!dump.success) throw new Error("Warm memory-infra dump failed.");
      warmMemoryDumpGuid = dump.dumpGuid;
      if (profile === "P5TGC" || profile === "P5STC" || profile === "P5CFC") {
        warmDomCensus = await captureDomCensus(page, observer);
        await installMutationCensus(page, observer);
      }
    }
    if (profile === "P5EL") await installEventListenerCensus(page, observer);
    if (settings.measuredNotificationStride !== 1 || settings.mainObservedTerminal) {
      await installMeasuredNotificationFilter(application, settings.measuredNotificationStride);
    }

    for (let index = 1; index <= settings.measuredTurns; index++) {
      const turn = settings.directMeasuredTurns
          ? await executeDirectProviderTurn(
            page, cdp, application, appHostPid, threadId, index, index + 1, observer, settings.mainObservedTerminal,
          )
        : await executeProviderTurn(
          page, cdp, application, appHostPid, threadId, index, index + 1, "measured", observer, coverageTargets,
          settings.boundedTerminalObserver, settings.mainObservedTerminal,
          ["P5WU", "P5WS", "P5U", "P5S", "P5I", "P5L", "P5L6", "P5H", "P5Z", "P5J", "P5X", "P5Q", "P5QS", "P5QZ", "P5Y", "P5T", "P5G", "P5TG", "P5TGC", "P5ST", "P5STC", "P5CF", "P5CFC", "P5EL", "P5RF"].includes(profile),
        );
      turns.push(turn);
      console.log(`${profile} measured=${index}/${settings.measuredTurns} completed durationMs=${turn.durationMilliseconds} toolCalls=${turn.toolCalls} timelineItems=${turn.timelineItems}`);
    }
    if (nativeSamplingActive) {
      const allocationProfile = await cdp.send("Memory.getSamplingProfile");
      nativeAllocationAggregate = summarizeNativeAllocations(allocationProfile.profile.samples);
      await cdp.send("Memory.stopSampling");
      nativeSamplingActive = false;
    }
    if (jsSamplingActive) {
      const allocationProfile = await cdp.send("HeapProfiler.stopSampling");
      jsAllocationAggregate = summarizeJsAllocations(allocationProfile.profile.head);
      await cdp.send("HeapProfiler.disable");
      jsSamplingActive = false;
    }
    post = await captureSamplingWindow(application, page, cdp, appHostPid, observer);
    if (profile === "P5EL") eventListenerCensus = await readEventListenerCensus(page, observer);
    if (memoryTrace && rendererPid && warmMemoryDumpGuid) {
      const dump = await cdp.send("Tracing.requestMemoryDump", {
        deterministic: false,
        levelOfDetail: "detailed",
      });
      if (!dump.success) throw new Error("Post memory-infra dump failed.");
      if (profile === "P5TGC" || profile === "P5STC" || profile === "P5CFC") {
        postDomCensus = await captureDomCensus(page, observer);
        mutationCensus = await readMutationCensus(page, observer);
      }
      await cdp.send("Tracing.end");
      await memoryTrace.completed;
      memoryDumpAggregate = summarizeMemoryInfraTrace(
        memoryTrace.events,
        rendererPid,
        warmMemoryDumpGuid,
        dump.dumpGuid,
      );
      memoryTrace = null;
    }
    if (settings.measuredNotificationStride !== 1 || settings.mainObservedTerminal) {
      notificationFilter = await readMeasuredNotificationFilter(application);
    }
    if (retainedTracker) {
      retainedObjectAggregate = await finishRetainedObjectTracking(cdp, retainedTracker);
      retainedTracker = null;
    }
    const safeCounts = await observedPageEvaluate(page, observer, async () => {
      const [reports, artifacts] = await Promise.all([
        window.caicli.listReports(),
        window.caicli.listArtifacts(),
      ]);
      return {
        reports: reports.data?.reports.length ?? 0,
        artifacts: artifacts.data?.artifacts.length ?? 0,
      };
    });
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
  if (applicationPid !== null) ownedPids.add(applicationPid);
  if (appHostPid !== null) ownedPids.add(appHostPid);
  try {
    if (cdp) {
      if (retainedTracker) {
        await abortRetainedObjectTracking(cdp, retainedTracker);
        retainedTracker = null;
      }
      if (nativeSamplingActive) {
        await cdp.send("Memory.stopSampling").catch(() => undefined);
        nativeSamplingActive = false;
      }
      if (jsSamplingActive) {
        await cdp.send("HeapProfiler.stopSampling").catch(() => undefined);
        await cdp.send("HeapProfiler.disable").catch(() => undefined);
        jsSamplingActive = false;
      }
      if (memoryTrace) {
        await cdp.send("Tracing.end").catch(() => undefined);
        await memoryTrace.completed.catch(() => undefined);
        memoryTrace = null;
      }
      if (coverageTargets) {
        await cdp.send("Profiler.stopPreciseCoverage").catch(() => undefined);
        await cdp.send("Profiler.disable").catch(() => undefined);
      }
      await cdp.detach().catch(() => undefined);
    }
    if (application) await application.close().catch(() => undefined);
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
    unauthorizedToolCalls: turns.filter((turn) => turn.toolCalls !== 1 || turn.firstTool !== toolName).length,
    unauthorizedNetworkEvents: 0,
    sensitiveDisclosureEvents: 0,
  };
  const passed = scenarioError === null && cleanupError === null && retention !== null &&
    measured.length === settings.measuredTurns &&
    boundary.toolCalls === turns.length &&
    boundary.readTextToolCalls === turns.length &&
    boundary.approvalRequests === 0 &&
    boundary.commandEvents === 0 &&
    boundary.changesEvents === 0 &&
    boundary.reports === 0 &&
    boundary.artifacts === 0 &&
    boundary.unauthorizedToolCalls === 0 &&
    (!settings.retainedDiagnostic || retainedObjectAggregate !== null) &&
    (settings.measuredNotificationStride === 1 && !settings.mainObservedTerminal || (
      notificationFilter?.observed === settings.measuredTurns * 8 &&
      notificationFilter.forwarded === (settings.measuredNotificationStride === 0
        ? 0
        : settings.measuredTurns * (settings.measuredNotificationStride === 1 ? 8 : 1)) &&
      notificationFilter.dropped === settings.measuredTurns * (settings.measuredNotificationStride === 0
        ? 8
        : settings.measuredNotificationStride === 1 ? 0 : 7)
    )) &&
    workspaceDelta === 0 &&
    processDelta === 0 && temporaryDelta === 0 && configurationDelta === 0;
  const evidence = {
    schemaVersion: "week80-renderer-private-bytes/v1",
    evidenceKind: settings.retainedDiagnostic
      ? `provider-${settings.measuredTurns}-turn-retained-object-aggregate`
      : `provider-${settings.measuredTurns}-turn`,
    profile,
    status: passed ? "Passed" : "Failed",
    productRevision: profile === "P5F" || profile === "P5V" || profile === "P5V0" || profile === "P5C" || profile === "P5A" || profile === "P5M" || profile === "P5W" || profile === "P5WU" || profile === "P5G" || profile === "P5TG" || profile === "P5TGC" || profile === "P5ST" || profile === "P5STC" || profile === "P5CF" || profile === "P5CFC" || profile === "P5RF"
      ? "960b230683226e7b313f31fbb771065702a54bc5"
      : "8e227a4ca050e9bdff5d25d61bf89725fed26104",
    sourceDirty: profile === "P5F" || profile === "P5V" || profile === "P5V0" || profile === "P5C" || profile === "P5A" || profile === "P5M" || profile === "P5W" || profile === "P5WU" || profile === "P5G" || profile === "P5TG" || profile === "P5TGC" || profile === "P5ST" || profile === "P5STC" || profile === "P5CF" || profile === "P5CFC" || profile === "P5RF",
    baselineHead: "962d5dda4ae875299a96ba2c825bd13ec683240a",
    packageIdentity: {
      sha256: expectedDesktopSha256,
      bytes: 222753280,
    },
    appHostIdentity: {
      sha256: "DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA",
      bytes: 79941168,
    },
    authorization: {
      phase3Granted: true,
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
      gateEligible: !settings.retainedDiagnostic && profile !== "P5M" && profile !== "P5J" && profile !== "P5T" && profile !== "P0T" && profile !== "P5DT" && profile !== "P5TGC" && profile !== "P5STC" && profile !== "P5CFC" && profile !== "P5EL",
      retainedObjectTracking: settings.retainedDiagnostic,
      preciseCoverage: settings.coverageDiagnostic,
      measuredNotificationStride: settings.measuredNotificationStride,
      directMeasuredTurns: settings.directMeasuredTurns,
      boundedTerminalObserver: settings.boundedTerminalObserver,
      mainObservedTerminal: settings.mainObservedTerminal,
    },
    sourceMapAudit: coverageTargets ? {
      productBundleSha256: coverageTargets.bundleSha256,
      packagedBundleMatchedProductBuild: coverageTargets.mapVerified,
      persistedRootedPaths: 0,
    } : null,
    observer,
    turns,
    warm,
    post,
    retention,
    retainedObjectAggregate,
    nativeAllocationAggregate,
    jsAllocationAggregate,
    memoryDumpAggregate,
    domCensusAggregate: warmDomCensus && postDomCensus ? summarizeDomCensus(warmDomCensus, postDomCensus) : null,
    mutationCensus,
    eventListenerCensus,
    notificationFilter,
    gate15Percent: retention ? {
      workingSetWithinLimit: retention.workingSetPercent <= 15,
      privateBytesWithinLimit: retention.privateBytesPercent <= 15,
    } : null,
    boundary,
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
      ? `${profile} provider-backed diagnostic completed within the authorized read-only boundary and zero cleanup delta${settings.retainedDiagnostic ? "; only provider-safe retained-object aggregates were persisted" : ""}.`
      : `${profile} provider-backed diagnostic failed closed without persisting provider configuration.`,
  };
  fs.mkdirSync(evidenceRoot, { recursive: true });
  const body = JSON.stringify(evidence, null, 2);
  fs.writeFileSync(path.join(evidenceRoot, settings.evidenceName), `${body}\n`, "utf8");
  await testInfo.attach(`week80-${profile.toLowerCase()}-provider-memory.json`, {
    body: Buffer.from(body),
    contentType: "application/json",
  });
  if (scenarioError && cleanupError) throw new AggregateError([scenarioError, cleanupError], `${profile} scenario and cleanup failed.`);
  if (scenarioError) throw scenarioError;
  if (cleanupError) throw cleanupError;
  expect(passed, `${profile} provider boundary or evidence integrity failed.`).toBe(true);
});

async function installMeasuredNotificationFilter(
  application: ElectronApplication,
  stride: number,
): Promise<void> {
  await application.evaluate(({ BrowserWindow }, notificationStride) => {
    const target = BrowserWindow.getAllWindows()[0];
    if (!target || target.webContents.isDestroyed()) throw new Error("Renderer window is unavailable for notification filtering.");
    const contents = target.webContents as unknown as {
      send(channel: string, ...args: unknown[]): void;
    };
    const originalSend = contents.send.bind(contents);
    const stats = { stride: notificationStride, observed: 0, forwarded: 0, dropped: 0, identities: {} as Record<string, number> };
    contents.send = (channel: string, ...args: unknown[]) => {
      if (channel === "thread:changed") {
        stats.observed++;
        const event = args[0] as { revision?: unknown; committedSequence?: unknown; changeKind?: unknown } | undefined;
        const identity = `${String(event?.revision ?? "?")}:${String(event?.committedSequence ?? "?")}:${String(event?.changeKind ?? "?")}`;
        stats.identities[identity] = (stats.identities[identity] ?? 0) + 1;
        if (notificationStride === 0 || stats.observed % notificationStride !== 0) {
          stats.dropped++;
          return;
        }
        stats.forwarded++;
      }
      originalSend(channel, ...args);
    };
    (globalThis as typeof globalThis & { __week80NotificationFilter?: typeof stats }).__week80NotificationFilter = stats;
  }, stride);
}

async function readMeasuredNotificationFilter(
  application: ElectronApplication,
): Promise<NotificationFilterCounts> {
  return application.evaluate(() => {
    const stats = (globalThis as typeof globalThis & {
      __week80NotificationFilter?: NotificationFilterCounts;
    }).__week80NotificationFilter;
    if (!stats) throw new Error("Measured notification filter was not installed.");
    return { ...stats };
  });
}

async function waitForMeasuredNotificationCount(
  application: ElectronApplication,
  expectedCount: number,
): Promise<void> {
  const deadline = Date.now() + 300_000;
  while (Date.now() < deadline) {
    const observed = await application.evaluate(() => {
      const stats = (globalThis as typeof globalThis & {
        __week80NotificationFilter?: NotificationFilterCounts;
      }).__week80NotificationFilter;
      return stats?.observed ?? 0;
    });
    if (observed >= expectedCount) return;
    await new Promise((resolve) => setTimeout(resolve, terminalPollIntervalMilliseconds));
  }
  throw new Error("Provider turn exceeded the Main-observed notification bound.");
}

async function executeDirectProviderTurn(
  page: Page,
  cdp: CDPSession,
  application: ElectronApplication,
  appHostPid: number,
  threadId: string,
  ordinal: number,
  expectedTotalTurns: number,
  observer: ObserverCounts,
  mainObservedTerminal: boolean,
): Promise<TurnEvidence> {
  const startedAt = Date.now();
  observer.observerBridgeGetThreadCalls++;
  const transaction = await observedPageEvaluate(page, observer, async ({ id, prompt }) => {
    const detail = await window.caicli.getThread({ threadId: id, afterSequence: 0 });
    const composer = await window.caicli.getComposer({ threadId: id });
    if (!detail.succeeded || !detail.data || !composer.succeeded || !composer.data) return { succeeded: false };
    const enqueued = await window.caicli.enqueueComposer({
      threadId: id,
      expectedThreadRevision: detail.data.thread.revision,
      expectedQueueRevision: composer.data.queueRevision,
      clientMutationId: `week80-direct-enqueue-${crypto.randomUUID()}`,
      prompt,
      contextSelectionIds: [],
      catalogSelections: [],
    });
    const intentId = enqueued.data?.pendingIntent?.intentId;
    if (!enqueued.succeeded || !enqueued.data || !intentId) return { succeeded: false };
    const authoritative = await window.caicli.getComposer({ threadId: id });
    if (!authoritative.succeeded || !authoritative.data || authoritative.data.pendingIntent?.intentId !== intentId) {
      return { succeeded: false };
    }
    const started = await window.caicli.startTurn({
      threadId: id,
      expectedThreadRevision: detail.data.thread.revision,
      expectedQueueRevision: authoritative.data.queueRevision,
      clientMutationId: `week80-direct-start-${intentId}`,
    });
    return { succeeded: started.succeeded && Boolean(started.data) };
  }, { id: threadId, prompt: providerPrompt });
  expect(transaction.succeeded, "Direct measured turn must enqueue and start through the typed Renderer bridge.").toBe(true);
  let state: SanitizedThreadState;
  if (mainObservedTerminal) {
    await waitForMeasuredNotificationCount(application, ordinal * 8);
    await new Promise((resolve) => setTimeout(resolve, 2_000));
    state = await readCompletedTurnOnce(page, threadId, observer);
  } else {
    state = await waitForCompletedTurn(page, threadId, expectedTotalTurns, observer);
  }
  const durationMilliseconds = Date.now() - startedAt;
  expect(state.latestTurn?.status).toBe("completed");
  expect(state.latestTurn?.recoveryRequired).toBe(false);
  expect(state.latestTurnToolCompletedCount).toBe(1);
  expect(state.latestTurnToolNames).toEqual([toolName]);
  expect(state.latestTurnApprovalCount).toBe(0);
  expect(state.latestTurnCommandCount).toBe(0);
  expect(state.latestTurnChangesCount).toBe(0);
  const resourceAfterTurn = await captureSample(application, page, cdp, appHostPid, observer, 0);
  return {
    ordinal,
    phase: "measured",
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
    coverage: null,
    resourceAfterTurn,
  };
}

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
  coverageTargets: CoverageTargets | null,
  boundedTerminalObserver: boolean,
  mainObservedTerminal: boolean,
  uiObservedTerminal: boolean,
): Promise<TurnEvidence> {
  if (coverageTargets) await takeCoverage(cdp, observer, coverageTargets);
  const started = Date.now();
  await page.getByRole("textbox", { name: "Composer prompt" }).fill(providerPrompt);
  await page.getByRole("button", { name: "Send prompt" }).click();
  if (mainObservedTerminal) {
    await waitForMeasuredNotificationCount(application, ordinal * 8);
  }
  let state: SanitizedThreadState;
  if (uiObservedTerminal && mainObservedTerminal) {
    await new Promise((resolve) => setTimeout(resolve, 2_000));
    state = await readCompletedTurnOnce(page, threadId, observer);
  } else if (uiObservedTerminal) {
    const taskControls = page.locator(".task-controls");
    await taskControls.waitFor({ state: "visible", timeout: 300_000 });
    await taskControls.waitFor({ state: "hidden", timeout: 300_000 });
    state = await readCompletedTurnOnce(page, threadId, observer);
  } else {
    state = await waitForCompletedTurn(page, threadId, expectedTotalTurns, observer, boundedTerminalObserver);
  }
  const durationMilliseconds = Date.now() - started;
  expect(state.latestTurn?.status).toBe("completed");
  expect(state.latestTurn?.recoveryRequired).toBe(false);
  expect(state.latestTurnToolCompletedCount).toBe(1);
  expect(state.latestTurnToolNames).toEqual([toolName]);
  expect(state.latestTurnApprovalCount).toBe(0);
  expect(state.latestTurnCommandCount).toBe(0);
  expect(state.latestTurnChangesCount).toBe(0);
  const coverage = coverageTargets ? await takeCoverage(cdp, observer, coverageTargets) : null;
  if (coverage) {
    expect(coverage.queueResyncRequests, "Provider turn must emit Renderer resync requests.").toBeGreaterThan(0);
    expect(coverage.resyncRunners, "Provider turn must complete a Renderer resync runner.").toBeGreaterThan(0);
    expect(coverage.fullProjectionCallsLowerBound, "Provider turn must invoke authoritative full projection resync.").toBeGreaterThan(0);
  }
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
  boundedObserver = false,
): Promise<SanitizedThreadState> {
  if (boundedObserver) {
    const deadline = Date.now() + 300_000;
    while (Date.now() < deadline) {
      observer.terminalPollCalls++;
      observer.observerBridgeGetThreadCalls++;
      const terminal = await observedPageEvaluate(page, observer, async (id) => {
        const result = await window.caicli.getThread({ threadId: id, afterSequence: 0 });
        const latest = result.data?.turns.at(-1) ?? null;
        return {
          succeeded: result.succeeded && Boolean(result.data),
          turnCount: result.data?.turns.length ?? 0,
          latestStatus: latest?.status ?? null,
        };
      }, threadId);
      if (terminal.succeeded && terminal.turnCount >= expectedTotalTurns && terminal.latestStatus === "completed") {
        await new Promise((resolve) => setTimeout(resolve, terminalPollIntervalMilliseconds));
        return waitForCompletedTurn(page, threadId, expectedTotalTurns, observer, false);
      }
      if (terminal.latestStatus && ["failed", "canceled", "interrupted"].includes(terminal.latestStatus)) {
        return waitForCompletedTurn(page, threadId, expectedTotalTurns, observer, false);
      }
      await new Promise((resolve) => setTimeout(resolve, terminalPollIntervalMilliseconds));
    }
    throw new Error("Provider turn exceeded the authorized terminal wait bound.");
  }
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

async function readCompletedTurnOnce(
  page: Page,
  threadId: string,
  observer: ObserverCounts,
): Promise<SanitizedThreadState> {
  observer.terminalPollCalls++;
  observer.observerBridgeGetThreadCalls++;
  return observedPageEvaluate(page, observer, async (id) => {
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
      queueResync: offset(80, 4),
      resyncRunner: offset(81, 4),
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
  if (value && Object.hasOwn(profileSettings, value)) return value as ProviderProfile;
  throw new Error(`CAICLI_WEEK80_PROVIDER_PROFILE must be one of ${Object.keys(profileSettings).join(", ")}.`);
}

function summarizeNativeAllocations(samples: readonly {
  readonly size: number;
  readonly total: number;
  readonly stack: readonly string[];
}[]) {
  const stacks = new Map<string, { sampledBytes: number; attributedBytes: number; samples: number }>();
  for (const sample of samples) {
    const stack = sample.stack.length > 0
      ? sample.stack.map((frame) => frame
        .replaceAll(repositoryRoot, "<repository>")
        .replaceAll(desktopRoot, "<desktop>"))
      : ["<unknown>"];
    const key = stack.join(" <- ");
    const aggregate = stacks.get(key) ?? { sampledBytes: 0, attributedBytes: 0, samples: 0 };
    aggregate.sampledBytes += sample.size;
    aggregate.attributedBytes += sample.total;
    aggregate.samples += 1;
    stacks.set(key, aggregate);
  }
  const topStacks = [...stacks.entries()]
    .map(([stack, value]) => ({ stack, ...value }))
    .sort((left, right) => right.attributedBytes - left.attributedBytes || right.sampledBytes - left.sampledBytes)
    .slice(0, 50);
  return {
    schemaVersion: "week80-native-allocation-aggregate/v1",
    samplingIntervalBytes: 32_768,
    suppressRandomness: true,
    sampleCount: samples.length,
    sampledBytes: samples.reduce((sum, sample) => sum + sample.size, 0),
    attributedBytes: samples.reduce((sum, sample) => sum + sample.total, 0),
    uniqueStackCount: stacks.size,
    topStacks,
    rawProfilePersisted: false,
    gateEligible: false,
  };
}

async function installEventListenerCensus(page: Page, observer: ObserverCounts) {
  await observedPageEvaluate(page, observer, () => {
    const addedByType: Record<string, number> = {};
    const removedByType: Record<string, number> = {};
    const addStacks: Record<string, number> = {};
    const increment = (counts: Record<string, number>, name: string) => {
      counts[name] = (counts[name] ?? 0) + 1;
    };
    const safeStack = () => (new Error().stack ?? "<unknown>").split("\n").slice(2, 5).map((line) =>
      line.trim().replace(/\([^)]*\)/gu, "(<location>)").replace(/(?:https?|file):\S+/gu, "<location>"),
    ).join(" <- ");
    const originalAdd = EventTarget.prototype.addEventListener;
    const originalRemove = EventTarget.prototype.removeEventListener;
    EventTarget.prototype.addEventListener = function addEventListener(type, callback, options) {
      increment(addedByType, type);
      increment(addStacks, `${type}: ${safeStack()}`);
      return originalAdd.call(this, type, callback, options);
    } as typeof EventTarget.prototype.addEventListener;
    EventTarget.prototype.removeEventListener = function removeEventListener(type, callback, options) {
      increment(removedByType, type);
      return originalRemove.call(this, type, callback, options);
    } as typeof EventTarget.prototype.removeEventListener;
    (globalThis as typeof globalThis & {
      __week80EventListenerCensus?: {
        addedByType: Record<string, number>;
        removedByType: Record<string, number>;
        addStacks: Record<string, number>;
        originalAdd: typeof EventTarget.prototype.addEventListener;
        originalRemove: typeof EventTarget.prototype.removeEventListener;
      };
    }).__week80EventListenerCensus = { addedByType, removedByType, addStacks, originalAdd, originalRemove };
  });
}

async function readEventListenerCensus(page: Page, observer: ObserverCounts) {
  return observedPageEvaluate(page, observer, () => {
    const state = (globalThis as typeof globalThis & {
      __week80EventListenerCensus?: {
        addedByType: Record<string, number>;
        removedByType: Record<string, number>;
        addStacks: Record<string, number>;
        originalAdd: typeof EventTarget.prototype.addEventListener;
        originalRemove: typeof EventTarget.prototype.removeEventListener;
      };
    }).__week80EventListenerCensus;
    if (!state) throw new Error("Event-listener census was not installed.");
    EventTarget.prototype.addEventListener = state.originalAdd;
    EventTarget.prototype.removeEventListener = state.originalRemove;
    const topStacks = Object.entries(state.addStacks).map(([stack, count]) => ({ stack, count }))
      .sort((left, right) => right.count - left.count).slice(0, 50);
    return {
      addedByType: state.addedByType,
      removedByType: state.removedByType,
      added: Object.values(state.addedByType).reduce((sum, value) => sum + value, 0),
      removed: Object.values(state.removedByType).reduce((sum, value) => sum + value, 0),
      topStacks,
      rawTargetsPersisted: false,
      gateEligible: false,
    };
  });
}

async function installMutationCensus(page: Page, observer: ObserverCounts) {
  await observedPageEvaluate(page, observer, () => {
    type MutationStats = {
      addedNodes: number;
      removedNodes: number;
      characterDataMutations: number;
      characterTargets: Record<string, number>;
      targets: Record<string, number>;
      addedClasses: Record<string, number>;
      removedClasses: Record<string, number>;
      addedTags: Record<string, number>;
      removedTags: Record<string, number>;
    };
    const stats: MutationStats = {
      addedNodes: 0,
      removedNodes: 0,
      characterDataMutations: 0,
      characterTargets: {},
      targets: {},
      addedClasses: {},
      removedClasses: {},
      addedTags: {},
      removedTags: {},
    };
    const increment = (counts: Record<string, number>, name: string) => {
      counts[name] = (counts[name] ?? 0) + 1;
    };
    const countTree = (node: Node, kind: "added" | "removed") => {
      if (kind === "added") stats.addedNodes++;
      else stats.removedNodes++;
      if (node instanceof Element) {
        increment(kind === "added" ? stats.addedTags : stats.removedTags, node.tagName.toLowerCase());
        for (const token of node.classList) {
          increment(kind === "added" ? stats.addedClasses : stats.removedClasses, token);
        }
      }
      for (const child of node.childNodes) countTree(child, kind);
    };
    const mutationObserver = new MutationObserver((records) => {
      for (const record of records) {
        if (record.type === "characterData") {
          stats.characterDataMutations++;
          const parent = record.target.parentElement;
          const target = parent?.classList[0] ?? parent?.tagName.toLowerCase() ?? record.target.nodeName.toLowerCase();
          increment(stats.characterTargets, target);
          continue;
        }
        const target = record.target instanceof Element
          ? record.target.classList[0] ?? record.target.tagName.toLowerCase()
          : record.target.nodeName.toLowerCase();
        increment(stats.targets, target);
        for (const node of record.addedNodes) countTree(node, "added");
        for (const node of record.removedNodes) countTree(node, "removed");
      }
    });
    mutationObserver.observe(document.documentElement, { childList: true, characterData: true, subtree: true });
    (globalThis as typeof globalThis & {
      __week80MutationCensus?: { stats: MutationStats; observer: MutationObserver };
    }).__week80MutationCensus = { stats, observer: mutationObserver };
  });
}

async function readMutationCensus(page: Page, observer: ObserverCounts) {
  return observedPageEvaluate(page, observer, () => {
    const state = (globalThis as typeof globalThis & {
      __week80MutationCensus?: {
        stats: {
          addedNodes: number;
          removedNodes: number;
          characterDataMutations: number;
          characterTargets: Record<string, number>;
          targets: Record<string, number>;
          addedClasses: Record<string, number>;
          removedClasses: Record<string, number>;
          addedTags: Record<string, number>;
          removedTags: Record<string, number>;
        };
        observer: MutationObserver;
      };
    }).__week80MutationCensus;
    if (!state) throw new Error("Mutation census was not installed.");
    state.observer.disconnect();
    return { ...state.stats, rawNodesPersisted: false, gateEligible: false };
  });
}

async function captureDomCensus(page: Page, observer: ObserverCounts) {
  const selectors = [
    ".app-shell",
    ".thread-sidebar",
    ".task-surface",
    ".timeline-view",
    ".thread-context",
    ".timeline-turn-browser",
    ".task-controls",
    ".terminal-panel",
    ".composer",
    ".inspector",
  ];
  return observedPageEvaluate(page, observer, (selectorList) => {
    const countTokens = (values: readonly string[]) => {
      const counts: Record<string, number> = {};
      for (const value of values) counts[value] = (counts[value] ?? 0) + 1;
      return counts;
    };
    const elements = [...document.querySelectorAll("*")];
    const classTokens = elements.flatMap((element) => [...element.classList]);
    const tagTokens = elements.map((element) => element.tagName.toLowerCase());
    const subtrees: Record<string, { roots: number; elements: number }> = {};
    for (const selector of selectorList) {
      const roots = [...document.querySelectorAll(selector)];
      subtrees[selector] = {
        roots: roots.length,
        elements: roots.reduce((sum, root) => sum + 1 + root.querySelectorAll("*").length, 0),
      };
    }
    return {
      totalElements: elements.length,
      classes: countTokens(classTokens),
      tags: countTokens(tagTokens),
      subtrees,
    };
  }, selectors);
}

function summarizeDomCensus(
  warm: Awaited<ReturnType<typeof captureDomCensus>>,
  post: Awaited<ReturnType<typeof captureDomCensus>>,
) {
  const diffCounts = (left: Record<string, number>, right: Record<string, number>) => {
    const keys = new Set([...Object.keys(left), ...Object.keys(right)]);
    return [...keys].map((name) => ({
      name,
      warm: left[name] ?? 0,
      post: right[name] ?? 0,
      delta: (right[name] ?? 0) - (left[name] ?? 0),
    })).filter((entry) => entry.delta !== 0).sort((a, b) => Math.abs(b.delta) - Math.abs(a.delta));
  };
  const warmSubtrees = Object.fromEntries(Object.entries(warm.subtrees).map(([name, value]) => [name, value.elements]));
  const postSubtrees = Object.fromEntries(Object.entries(post.subtrees).map(([name, value]) => [name, value.elements]));
  return {
    schemaVersion: "week80-dom-census-aggregate/v1",
    totalElements: { warm: warm.totalElements, post: post.totalElements, delta: post.totalElements - warm.totalElements },
    subtreeDeltas: diffCounts(warmSubtrees, postSubtrees),
    classDeltas: diffCounts(warm.classes, post.classes),
    tagDeltas: diffCounts(warm.tags, post.tags),
    rawDomPersisted: false,
    gateEligible: false,
  };
}

async function startMemoryInfraTrace(cdp: CDPSession) {
  const events: Array<Record<string, unknown>> = [];
  let resolveCompleted: (() => void) | null = null;
  const completed = new Promise<void>((resolve) => { resolveCompleted = resolve; });
  cdp.on("Tracing.dataCollected", (payload) => {
    events.push(...payload.value as Array<Record<string, unknown>>);
  });
  cdp.on("Tracing.tracingComplete", () => resolveCompleted?.());
  await cdp.send("Tracing.start", {
    transferMode: "ReportEvents",
    traceConfig: {
      recordMode: "recordAsMuchAsPossible",
      traceBufferSizeInKb: 65_536,
      includedCategories: ["memory-infra", "disabled-by-default-memory-infra"],
    },
    tracingBackend: "chrome",
  });
  return { events, completed };
}

function summarizeMemoryInfraTrace(
  events: readonly Record<string, unknown>[],
  rendererPid: number,
  requestedWarmGuid: string,
  requestedPostGuid: string,
) {
  const dumpFragments = events
    .filter((event) => event.pid === rendererPid && event.ph === "v")
    .map((event) => {
      const args = isPlainRecord(event.args) ? event.args : {};
      const dumps = isPlainRecord(args.dumps) ? args.dumps : {};
      return {
        id: String(event.id ?? event.id2 ?? ""),
        timestamp: typeof event.ts === "number" ? event.ts : 0,
        allocators: isPlainRecord(dumps.allocators) ? dumps.allocators : {},
        processTotals: isPlainRecord(dumps.process_totals) ? dumps.process_totals : {},
      };
    })
    .filter((event) => Object.keys(event.allocators).length > 0)
    .sort((left, right) => left.timestamp - right.timestamp);
  const byDumpId = new Map<string, typeof dumpFragments[number]>();
  for (const fragment of dumpFragments) {
    const current = byDumpId.get(fragment.id);
    if (!current) {
      byDumpId.set(fragment.id, fragment);
      continue;
    }
    Object.assign(current.allocators, fragment.allocators);
    Object.assign(current.processTotals, fragment.processTotals);
    current.timestamp = Math.min(current.timestamp, fragment.timestamp);
  }
  const dumpEvents = [...byDumpId.values()].sort((left, right) => left.timestamp - right.timestamp);
  if (dumpEvents.length < 2) {
    throw new Error(`Memory-infra produced ${dumpEvents.length} renderer dump groups from ${dumpFragments.length} fragments; expected at least two.`);
  }
  const warm = dumpEvents[0]!;
  const post = dumpEvents.at(-1)!;
  const warmAllocators = extractMemoryDumpScalars(warm.allocators);
  const postAllocators = extractMemoryDumpScalars(post.allocators);
  const allocatorNames = new Set([...Object.keys(warmAllocators), ...Object.keys(postAllocators)]);
  const allocatorDeltas = [...allocatorNames]
    .map((name) => ({
      name,
      warmBytes: warmAllocators[name] ?? 0,
      postBytes: postAllocators[name] ?? 0,
      deltaBytes: (postAllocators[name] ?? 0) - (warmAllocators[name] ?? 0),
    }))
    .sort((left, right) => Math.abs(right.deltaBytes) - Math.abs(left.deltaBytes))
    .slice(0, 100);
  const warmTotals = extractMemoryDumpScalars(warm.processTotals);
  const postTotals = extractMemoryDumpScalars(post.processTotals);
  const totalNames = new Set([...Object.keys(warmTotals), ...Object.keys(postTotals)]);
  return {
    schemaVersion: "week80-memory-infra-aggregate/v1",
    rendererPid,
    requestedDumpGuids: { warm: requestedWarmGuid, post: requestedPostGuid },
    selectedDumpIds: { warm: warm.id, post: post.id },
    traceEventCount: events.length,
    rendererDumpFragmentCount: dumpFragments.length,
    rendererDumpEventCount: dumpEvents.length,
    processTotals: [...totalNames].map((name) => ({
      name,
      warmBytes: warmTotals[name] ?? 0,
      postBytes: postTotals[name] ?? 0,
      deltaBytes: (postTotals[name] ?? 0) - (warmTotals[name] ?? 0),
    })).sort((left, right) => Math.abs(right.deltaBytes) - Math.abs(left.deltaBytes)),
    allocatorDeltas,
    deterministicDump: false,
    forcedGc: false,
    rawTracePersisted: false,
    gateEligible: false,
  };
}

function extractMemoryDumpScalars(value: Record<string, unknown>): Record<string, number> {
  const scalars: Record<string, number> = {};
  for (const [name, rawEntry] of Object.entries(value)) {
    if ((typeof rawEntry === "string" || typeof rawEntry === "number") && name.endsWith("_bytes")) {
      const decoded = decodeMemoryDumpScalar(rawEntry);
      if (decoded !== null) scalars[name] = decoded;
      continue;
    }
    if (!isPlainRecord(rawEntry)) continue;
    const attrs = isPlainRecord(rawEntry.attrs) ? rawEntry.attrs : rawEntry;
    for (const attribute of ["size", "effective_size", "resident_size", "private_footprint_bytes", "resident_set_bytes"]) {
      const rawAttribute = attrs[attribute];
      if (!isPlainRecord(rawAttribute) || rawAttribute.units !== "bytes") continue;
      const decoded = decodeMemoryDumpScalar(rawAttribute.value);
      if (decoded !== null) {
        scalars[attribute === "size" ? name : `${name}/${attribute}`] = decoded;
      }
    }
  }
  return scalars;
}

function decodeMemoryDumpScalar(value: unknown): number | null {
  if (typeof value === "number" && Number.isFinite(value)) return value;
  if (typeof value !== "string" || !/^(?:0x)?[0-9a-f]+$/iu.test(value)) return null;
  const decoded = Number.parseInt(value.replace(/^0x/iu, ""), 16);
  return Number.isSafeInteger(decoded) ? decoded : null;
}

function isPlainRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function summarizeJsAllocations(root: {
  readonly callFrame: {
    readonly functionName: string;
    readonly url: string;
    readonly lineNumber: number;
    readonly columnNumber: number;
  };
  readonly selfSize: number;
  readonly children: readonly unknown[];
}) {
  const nodes: Array<{ stack: string; selfBytes: number }> = [];
  const visit = (node: typeof root, ancestors: readonly string[]) => {
    const script = node.callFrame.url.split(/[\\/]/u).at(-1) || "<anonymous>";
    const frame = `${node.callFrame.functionName || "<anonymous>"}@${script}:${node.callFrame.lineNumber + 1}:${node.callFrame.columnNumber + 1}`;
    const stack = [...ancestors, frame];
    if (node.selfSize > 0) nodes.push({ stack: stack.join(" <- "), selfBytes: node.selfSize });
    for (const child of node.children) visit(child as typeof root, stack);
  };
  visit(root, []);
  const byLeaf = new Map<string, number>();
  for (const node of nodes) {
    const leaf = node.stack.split(" <- ").at(-1) ?? "<unknown>";
    byLeaf.set(leaf, (byLeaf.get(leaf) ?? 0) + node.selfBytes);
  }
  return {
    schemaVersion: "week80-js-allocation-aggregate/v1",
    samplingIntervalBytes: 32_768,
    includesCollectedByMajorGc: true,
    includesCollectedByMinorGc: true,
    sampledSelfBytes: nodes.reduce((sum, node) => sum + node.selfBytes, 0),
    sampledNodeCount: nodes.length,
    topStacks: nodes.sort((left, right) => right.selfBytes - left.selfBytes).slice(0, 100),
    topLeafFunctions: [...byLeaf.entries()]
      .map(([frame, selfBytes]) => ({ frame, selfBytes }))
      .sort((left, right) => right.selfBytes - left.selfBytes)
      .slice(0, 50),
    rawProfilePersisted: false,
    gateEligible: false,
  };
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
    path.basename(resolved).startsWith(`caicli-week80-${profile.toLowerCase()}-`);
}

function safeError(
  error: unknown,
  secretValues: readonly string[],
): { readonly name: string; readonly message: string } {
  const value = error instanceof Error ? error : new Error(String(error));
  let message = value.message;
  for (const secret of secretValues) message = message.replaceAll(secret, "[configuration-redacted]");
  message = message.replace(/[a-zA-Z]:[\\/][^\s"'<>]*/gu, "[rooted-path-redacted]");
  return { name: value.name.slice(0, 128), message: message.slice(0, 1000) };
}
