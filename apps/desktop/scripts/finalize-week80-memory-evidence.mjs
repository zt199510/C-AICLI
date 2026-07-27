import fs from "node:fs";
import path from "node:path";

const desktopRoot = path.resolve(import.meta.dirname, "..");
const repositoryRoot = path.resolve(desktopRoot, "..", "..");
const evidenceRoot = process.env.CAICLI_WEEK80_EVIDENCE_DIR
  ? path.resolve(process.env.CAICLI_WEEK80_EVIDENCE_DIR)
  : path.join(repositoryRoot, "artifacts", "week80-renderer-private-bytes");
const profileNames = ["c0", "c1", "c2", "c3", "c4", "c5", "c6", "c7"];
const profiles = Object.fromEntries(profileNames.map((name) => {
  const profilePath = path.join(evidenceRoot, `profile-${name}.json`);
  if (!fs.existsSync(profilePath)) throw new Error(`Missing Week 80 profile ${name.toUpperCase()}.`);
  return [name.toUpperCase(), JSON.parse(fs.readFileSync(profilePath, "utf8"))];
}));
const providerProfiles = Object.fromEntries(["p1", "p5", "p10"].map((name) => {
  const profilePath = path.join(evidenceRoot, `provider-profile-${name}.json`);
  if (!fs.existsSync(profilePath)) throw new Error(`Missing Week 80 provider profile ${name.toUpperCase()}.`);
  return [name.toUpperCase(), JSON.parse(fs.readFileSync(profilePath, "utf8"))];
}));

for (const [name, profile] of [...Object.entries(profiles), ...Object.entries(providerProfiles)]) {
  if (profile.status !== "Passed") throw new Error(`Week 80 profile ${name} did not complete.`);
  for (const delta of Object.values(profile.cleanupDelta)) {
    if (delta !== 0) throw new Error(`Week 80 profile ${name} has a non-zero cleanup delta.`);
  }
}
if (providerProfiles.P5.retention.privateBytesPercent <= 15) {
  throw new Error("P5 did not reproduce the frozen 15% private-bytes failure.");
}
if (providerProfiles.P10.retention.privateBytesPercent <= 15) {
  throw new Error("P10 was required by P5 failure but did not preserve the diagnostic failure direction.");
}

const identity = {
  schemaVersion: "week80-renderer-private-bytes/v1",
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
};
const zeroCleanup = { process: 0, temporary: 0, configuration: 0 };
const capturedAtUtc = new Date().toISOString();
const allProfiles = [...Object.values(profiles), ...Object.values(providerProfiles)];
const totalDuration = allProfiles.reduce((sum, profile) => sum + profile.durationMilliseconds, 0);
const common = (evidenceKind, status, summary, durationMilliseconds = 0) => ({
  ...identity,
  evidenceKind,
  status,
  capturedAtUtc,
  durationMilliseconds,
  cleanupDelta: zeroCleanup,
  summary,
});
const compactProvider = (profile) => ({
  profile: profile.profile,
  status: profile.status,
  settings: profile.settings,
  observer: profile.observer,
  turns: profile.turns,
  warm: profile.warm,
  post: profile.post,
  retention: profile.retention,
  gate15Percent: profile.gate15Percent,
  boundary: profile.boundary,
  cleanupDelta: profile.cleanupDelta,
  sourceMapAudit: profile.sourceMapAudit,
});
const measuredCoverage = (profile) => profile.turns
  .filter((turn) => turn.phase === "measured")
  .map((turn) => ({
    ordinal: turn.ordinal,
    timelineItems: turn.timelineItems,
    coverage: turn.coverage,
  }));

const documents = {
  "manifest.json": {
    ...common("manifest", "Blocked", "Week 80 completed the authorized diagnosis matrix; provider retention reproduced, but no safe single product fix met the root-cause gate.", totalDuration),
    sourceBranch: "codex/week80-renderer-memory-diagnosis",
    authorization: {
      credentialFreePhases: "Granted",
      providerBackedPhase3: "Granted",
      priorAuthorizationReusable: false,
      selectedConfigurationKeys: 3,
      parentEnvironmentInjected: false,
      packagedChildInjected: true,
      configurationValuesPersisted: false,
      allowedModelTools: ["workspace.read_text"],
      providerTurns: 19,
      readTextToolCalls: 19,
      unauthorizedToolCalls: 0,
      unauthorizedNetworkEvents: 0,
      sensitiveDisclosureEvents: 0,
    },
    settings: {
      gatePercent: 15,
      workers: 1,
      retries: 0,
      warmWindowSeconds: 30,
      postWindowSeconds: 30,
      forcedGc: false,
      rendererReloadUsedForGate: false,
    },
    counts: {
      credentialFreeProfiles: 8,
      providerProfiles: 3,
      completedProfiles: 11,
      expectedEvidenceFiles: 10,
      profileResourceSamples: allProfiles.reduce((sum, profile) => sum + profile.observer.resourceSamples, 0),
    },
  },
  "week79-baseline.json": {
    ...common("week79-baseline", "Passed", "Week 79 first failure and both corrected private-bytes failures remain frozen and unmodified."),
    sourceReviewRevision: "962d5dda4ae875299a96ba2c825bd13ec683240a",
    attempts: [
      { attempt: 1, correction: "provider-path-not-warm-before-baseline", workingSetPercent: 18.736259570919568, privateBytesPercent: 47.766453947995046 },
      { attempt: 2, correction: "provider-warm-baseline", workingSetPercent: 10.835977376189819, privateBytesPercent: 36.772841575859175 },
      { attempt: 3, correction: "one-second-terminal-poll", workingSetPercent: 10.773861328691616, privateBytesPercent: 35.836712126796904 },
    ],
    finalCounts: {
      providerTurns: 6,
      toolCalls: 6,
      timelineItems: 36,
      resourceSamples: 31,
      processRoles: 5,
      ownedPids: 6,
    },
    observerAudit: {
      terminalPollIntervalMilliseconds: 1000,
      pageEvaluateCallsRecorded: false,
      rendererBridgeCallsRecorded: false,
      resourceSampleCalls: 31,
      extraDomCreatedByHarness: false,
      persistentRendererReferencesRecorded: false,
      auditGap: "Week 79 retained aggregate samples but not exact page-evaluation or bridge-observer call counts.",
    },
  },
  "control-idle.json": {
    ...common("control-idle", "Passed", "C0 packaged zero-turn idle control completed with symmetric windows.", profiles.C0.durationMilliseconds),
    profile: profiles.C0,
  },
  "fixture-control.json": {
    ...common("fixture-control", "Passed", "Credential-free controls covered ordinary growth, frozen load, resync churn, provider-volume projection, observer polling, and distinct timestamps."),
    profiles: {
      C1: profiles.C1,
      C2: profiles.C2,
      C4: profiles.C4,
      C5: profiles.C5,
      C6: profiles.C6,
      C7: profiles.C7,
    },
  },
  "provider-1-turn.json": {
    ...common("provider-1-turn", "Passed", "Authorized P1 remained below the private-bytes gate while preserving the read-only boundary.", providerProfiles.P1.durationMilliseconds),
    result: compactProvider(providerProfiles.P1),
  },
  "provider-5-turn.json": {
    ...common("provider-5-turn", "Failed", "Authorized P5 reproduced the private-bytes failure at 22.67% with zero authorization or cleanup delta.", providerProfiles.P5.durationMilliseconds),
    result: compactProvider(providerProfiles.P5),
  },
  "provider-10-turn.json": {
    ...common("provider-10-turn", "Failed", "Conditional P10 extended the same failure direction to 40.04% private bytes.", providerProfiles.P10.durationMilliseconds),
    conditionalOnP5Failure: true,
    result: compactProvider(providerProfiles.P10),
  },
  "resync-counts.json": {
    ...common("resync-counts", "Passed", "Corrected source-map counters show eight durable notifications and eight non-overlapping full projection resyncs per provider turn."),
    sourceAttribution: {
      notificationCommitPath: [
        "src/CSharpAiCli.Application/TurnExecutionApplicationService.cs",
        "src/CSharpAiCli.AppHost/Protocol/DesktopRpcServer.cs",
        "apps/desktop/src/renderer/use-desktop-controller.ts",
      ],
      perProviderTurn: {
        finalTimelineItems: 6,
        threadChangedNotifications: 8,
        resyncRunners: 8,
        exactFullProjectionCalls: 8,
      },
      amplificationPerFinalTimelineItem: 8 / 6,
      semanticExplanation: "One start response, one running transition, five persisted runtime events, and one terminal transition each notify the renderer. Spaced events complete independently, so in-flight coalescing does not reduce them.",
    },
    providerMeasuredCoverage: {
      P1: measuredCoverage(providerProfiles.P1),
      P5: measuredCoverage(providerProfiles.P5),
      P10: measuredCoverage(providerProfiles.P10),
    },
    controls: {
      C3: profiles.C3,
      C5: profiles.C5,
      C7: profiles.C7,
    },
    correctionAudit: {
      invalidAsyncCoverageCountsUsedInConclusion: false,
      correction: "Declaration-level and async-resume coverage ranges were rejected. Final counts use synchronous queue and runner ranges from a source-map build whose renderer bundle SHA-256 exactly matched the packaged bundle.",
      harnessRetriesAdded: false,
    },
  },
  "heap-summary.json": {
    ...common("heap-summary", "Passed", "Live/total heap, DOM/listener, projection size, observer volume, and a disposable fixture heap aggregate were collected without persisting raw snapshots."),
    profiles: Object.fromEntries(Object.entries(profiles).map(([name, profile]) => [name, {
      warmSettled: profile.warm.rendererSettledMedian,
      postSettled: profile.post.rendererSettledMedian,
      retention: profile.retention,
      workloadDiagnostics: profile.workloadDiagnostics,
    }])),
    providerProfiles: Object.fromEntries(Object.entries(providerProfiles).map(([name, profile]) => [name, {
      warmSettled: profile.warm.rendererSettledMedian,
      postSettled: profile.post.rendererSettledMedian,
      retention: profile.retention,
      observer: profile.observer,
      projectionSizes: profile.turns.map((turn) => ({
        ordinal: turn.ordinal,
        phase: turn.phase,
        timelineItems: turn.timelineItems,
        projectionJsonUtf8Bytes: turn.projectionJsonUtf8Bytes ?? null,
        timelineSummaryUtf8Bytes: turn.timelineSummaryUtf8Bytes ?? null,
        timelinePayloadJsonUtf8Bytes: turn.timelinePayloadJsonUtf8Bytes ?? null,
        distinctTimelineTimestamps: turn.distinctTimelineTimestamps ?? null,
      })),
    }])),
    disposableFixtureHeapAggregate: profiles.C5.heapSummary,
    completeHeapSnapshotsPersisted: 0,
  },
  "diagnosis.json": {
    ...common("diagnosis", "Blocked", "Provider P5/P10 reproduced the Renderer failure, but the evidence does not isolate a single safely modifiable retention source."),
    phases: {
      phase0: "Passed",
      phase1: "Passed",
      phase2: "Passed",
      phase3: "Passed",
      phase4: "CompletedWithoutRootCause",
      phase5: "FailedRootCauseGate",
    },
    conclusion: "Blocked",
    gateAssessment: {
      identityAndBaseline: true,
      observerBounded: true,
      metricCoverage: true,
      credentialFreeCleanup: true,
      providerP5Reproduced: true,
      repeatedControlledRootCause: false,
      redactionAndCleanup: true,
    },
    findings: [
      "P1 private bytes remained below 15%; P5 and P10 rose above 15% in the same read-only boundary.",
      "Provider turns produce exactly eight durable notifications and eight full projection replacements for six final timeline items.",
      "C5 matched P10 at 66 items and 80 full projection replacements but stayed within the private-bytes gate and did not retain live heap.",
      "C6 matched P10 observer getThread poll volume without renderer updates and did not retain private bytes or live heap.",
      "C7 added distinct per-item timestamps to C5 and remained within the gate.",
      "Projection byte size, DOM node growth, and fixture heap aggregates did not supply a repeated retained-type explanation for provider-only growth.",
    ],
    unsupportedFixes: [
      "Changing queueResync or replacing full projection fetches is not supported as the sole fix because C5/C7 matched the count without reproducing provider heap retention.",
      "Changing timeline DOM or timestamp formatting is not supported because higher control node growth and distinct timestamps remained within the gate.",
      "Changing the observer or Gate is not supported because C6 excluded observer polling as a sufficient cause.",
    ],
    nextRequiredEvidence: "A separately designed provider-safe pre/post retained-object comparison or bounded product instrumentation at the AppHost-to-IPC-to-renderer serialization boundary is required before choosing a product fix.",
    productFixImplemented: false,
    diagnosisHandoffCreated: false,
    prohibitedShortcutsUsed: {
      gateChanged: false,
      retryAdded: false,
      forcedGcUsedForGate: false,
      reloadUsedToPassGate: false,
    },
  },
};

fs.mkdirSync(evidenceRoot, { recursive: true });
for (const [name, document] of Object.entries(documents)) {
  fs.writeFileSync(path.join(evidenceRoot, name), `${JSON.stringify(document, null, 2)}\n`, "utf8");
}
console.log(`Finalized ${Object.keys(documents).length} redacted Week 80 evidence envelopes.`);
