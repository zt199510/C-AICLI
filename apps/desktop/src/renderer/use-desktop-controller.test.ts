import { describe, expect, it } from "vitest";
import type { ThreadChangedParams } from "../generated/desktop-contracts";
import { conversationTitle, shouldQueueThreadResync } from "./use-desktop-controller";

describe("conversation title", () => {
  it("derives a compact title from the first prompt", () => {
    expect(conversationTitle("  Help me\nreview this workspace  ")).toBe("Help me review this workspace");
  });

  it("truncates safely by UTF-8 bytes", () => {
    const title = conversationTitle("连续对话".repeat(30));
    expect(new TextEncoder().encode(title).length).toBeLessThanOrEqual(96);
    expect(title.endsWith("�")).toBe(false);
  });
});

describe("thread projection resync policy", () => {
  it("resyncs every monotonic lifecycle or committed timeline advance", () => {
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
    expect(resyncs).toBe(8);
  });

  it("does not resync a regressive revision duplicate at the same committed sequence", () => {
    const previous: ThreadChangedParams = {
      schemaVersion: 1, eventSequence: 2, workspaceId: "workspace-1", threadId: "thread-1",
      revision: 2, committedSequence: 1, changeKind: "updated", emittedAtUtc: "2026-07-28T00:00:00.000Z",
    };
    const event: ThreadChangedParams = {
      ...previous,
      eventSequence: 3,
      revision: 1,
    };

    expect(shouldQueueThreadResync(previous, event)).toBe(false);
  });
});
