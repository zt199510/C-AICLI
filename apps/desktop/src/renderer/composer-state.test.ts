import { describe, expect, it } from "vitest";
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
});
