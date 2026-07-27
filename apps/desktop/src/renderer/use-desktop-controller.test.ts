import { describe, expect, it } from "vitest";
import type { ThreadChangedParams } from "../generated/desktop-contracts";
import { shouldQueueThreadResync } from "./use-desktop-controller";

describe("thread projection resync policy", () => {
  it("resyncs only at the two lifecycle boundaries in an eight-notification turn", () => {
    const committed = [7, 7, 8, 9, 10, 11, 12, 12];
    let previous: ThreadChangedParams | null = {
      schemaVersion: 1, eventSequence: 0, workspaceId: "workspace-1", threadId: "thread-1",
      revision: 9, committedSequence: 6, changeKind: "updated", emittedAtUtc: "2026-07-17T00:00:00.000Z",
    };
    let resyncs = 0;
    for (const [index, committedSequence] of committed.entries()) {
      const event: ThreadChangedParams = {
        schemaVersion: 1,
        eventSequence: index + 1,
        workspaceId: "workspace-1",
        threadId: "thread-1",
        revision: index + 10,
        committedSequence,
        changeKind: "updated",
        emittedAtUtc: "2026-07-17T00:00:00.000Z",
      };
      if (shouldQueueThreadResync(previous, event)) resyncs++;
      previous = event;
    }
    expect(resyncs).toBe(2);
  });
});
