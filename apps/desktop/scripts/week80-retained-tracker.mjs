import { summarizeRetainedSnapshot } from "./week80-retained-analyzer.mjs";

export async function startRetainedObjectTracking(cdp) {
  let baselineLastSeenObjectId = null;
  let resolveBaseline;
  let rejectBaseline;
  const observed = new Promise((resolve, reject) => {
    resolveBaseline = resolve;
    rejectBaseline = reject;
  });
  const listener = (event) => {
    if (!Number.isSafeInteger(event.lastSeenObjectId) || event.lastSeenObjectId < 0) return;
    baselineLastSeenObjectId = event.lastSeenObjectId;
    resolveBaseline(event.lastSeenObjectId);
  };
  cdp.on("HeapProfiler.lastSeenObjectId", listener);
  try {
    await cdp.send("HeapProfiler.enable");
    await cdp.send("HeapProfiler.startTrackingHeapObjects", { trackAllocations: false });
    const timeout = setTimeout(
      () => rejectBaseline(new Error("Retained-object baseline id was not observed.")),
      15_000,
    );
    try {
      await observed;
    } finally {
      clearTimeout(timeout);
    }
    if (baselineLastSeenObjectId === null) throw new Error("Retained-object baseline id is unavailable.");
    return { baselineLastSeenObjectId, lastSeenListener: listener };
  } catch (error) {
    cdp.off("HeapProfiler.lastSeenObjectId", listener);
    await cdp.send("HeapProfiler.disable").catch(() => undefined);
    throw error;
  }
}

export async function finishRetainedObjectTracking(cdp, tracker) {
  const chunks = [];
  const chunkListener = (event) => { chunks.push(event.chunk); };
  cdp.on("HeapProfiler.addHeapSnapshotChunk", chunkListener);
  try {
    await cdp.send("HeapProfiler.stopTrackingHeapObjects", { reportProgress: false });
    const serialized = chunks.join("");
    chunks.length = 0;
    const transientSnapshotBytes = Buffer.byteLength(serialized, "utf8");
    const snapshot = JSON.parse(serialized);
    return {
      ...summarizeRetainedSnapshot(snapshot, tracker.baselineLastSeenObjectId),
      transientSnapshotBytes,
      trackingMode: "single-post-snapshot-with-last-seen-object-id-boundary",
      gateEligible: false,
      snapshotMayTriggerGc: true,
    };
  } finally {
    cdp.off("HeapProfiler.addHeapSnapshotChunk", chunkListener);
    cdp.off("HeapProfiler.lastSeenObjectId", tracker.lastSeenListener);
    await cdp.send("HeapProfiler.disable").catch(() => undefined);
  }
}

export async function abortRetainedObjectTracking(cdp, tracker) {
  cdp.off("HeapProfiler.lastSeenObjectId", tracker.lastSeenListener);
  await cdp.send("HeapProfiler.disable").catch(() => undefined);
}
