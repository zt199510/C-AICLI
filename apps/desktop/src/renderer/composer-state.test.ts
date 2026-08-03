import { describe, expect, it } from "vitest";
import type { ComposerStateData } from "../generated/desktop-contracts";
import { composerReducer, currentDraft, draftKey, initialComposerUiState } from "./composer-state";

describe("composer state", () => {
  it("keeps drafts scoped by workspace and thread without persistence", () => {
    const first = draftKey("ws-a", "thread-a");
    const second = draftKey("ws-a", "thread-b");
    let state = composerReducer(initialComposerUiState, { type: "text", key: first, text: "first" });
    state = composerReducer(state, { type: "text", key: second, text: "second" });
    expect(currentDraft(state, first).text).toBe("first");
    expect(currentDraft(state, second).text).toBe("second");
    expect(Object.keys(state)).not.toContain("localStorage");
    expect(composerReducer(state, { type: "reset" })).toBe(initialComposerUiState);
  });

  it("preserves text and selections on errors and clears only after confirmation", () => {
    const key = draftKey("ws", "thread");
    let state = composerReducer(initialComposerUiState, { type: "text", key, text: "keep me" });
    state = composerReducer(state, { type: "add-context", key, item: { selectionId: "ctx_1", relativePath: "src/a.ts", kind: "file", byteCount: 4, fileCount: 1, availability: "available" } });
    state = composerReducer(state, { type: "status", key, status: "error", error: "stale" });
    expect(currentDraft(state, key).text).toBe("keep me");
    expect(currentDraft(state, key).contextSelections).toHaveLength(1);
    state = composerReducer(state, { type: "clear-draft", key });
    expect(currentDraft(state, key).text).toBe("");
    expect(currentDraft(state, key).status).toBe("queued");
  });

  it("settles transient send states without hiding a terminal error", () => {
    const key = draftKey("ws", "thread");
    let state = composerReducer(initialComposerUiState, { type: "status", key, status: "enqueueing" });
    state = composerReducer(state, { type: "settle", key });
    expect(currentDraft(state, key).status).toBe("editing");

    state = composerReducer(state, { type: "status", key, status: "error", error: "queue failed" });
    state = composerReducer(state, { type: "settle", key });
    expect(currentDraft(state, key)).toMatchObject({ status: "error", error: "queue failed" });
  });

  it("does not let an older pending snapshot replace a consumed composer queue", () => {
    const consumed = snapshot(3, false);
    const stalePending = snapshot(2, true);
    let state = composerReducer(initialComposerUiState, { type: "snapshot", snapshot: consumed });

    state = composerReducer(state, { type: "snapshot", snapshot: stalePending });

    expect(state.snapshot).toBe(consumed);
    expect(state.snapshot?.pendingIntent).toBeNull();
  });
});

function snapshot(queueRevision: number, pending: boolean): ComposerStateData {
  return {
    workspaceId: "workspace-1",
    threadId: "thread-1",
    threadRevision: 4,
    queueRevision,
    pendingIntent: pending ? {
      intentId: "intent-1",
      delivery: "current-turn",
      createdAtUtc: "2026-08-03T00:00:00.000Z",
      contextCount: 0,
      catalogCount: 0,
    } : null,
    effectiveModel: "gpt-test",
    modelSource: "test",
    approvalMode: "ask",
    approvalModeSource: "test",
    controlledContext: true,
  };
}
