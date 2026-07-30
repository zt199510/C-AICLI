import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { Composer } from "./Composer";
import { initialComposerUiState, type ComposerDraft } from "./composer-state";

const draft: ComposerDraft = { text: "hello", contextSelections: [], catalogSelections: [], status: "editing", error: null };

describe("Composer", () => {
  it("sends on Enter and inserts a newline on Shift Enter", () => {
    const props = makeProps();
    render(<Composer {...props} />);
    const prompt = screen.getByRole("textbox", { name: "Composer prompt" });
    fireEvent.keyDown(prompt, { key: "Enter", shiftKey: false });
    expect(props.onSend).toHaveBeenCalledOnce();
    fireEvent.keyDown(prompt, { key: "Enter", shiftKey: true });
    expect(props.onSend).toHaveBeenCalledOnce();
  });

  it("does not send while IME composition is active", () => {
    const props = makeProps();
    render(<Composer {...props} />);
    const prompt = screen.getByRole("textbox", { name: "Composer prompt" });
    fireEvent.compositionStart(prompt);
    fireEvent.keyDown(prompt, { key: "Enter" });
    expect(props.onSend).not.toHaveBeenCalled();
    fireEvent.compositionEnd(prompt);
    fireEvent.keyDown(prompt, { key: "Enter" });
    expect(props.onSend).toHaveBeenCalledOnce();
  });

  it("opens mention search and exposes accessible picker controls", async () => {
    const props = makeProps();
    render(<Composer {...props} />);
    fireEvent.change(screen.getByRole("textbox", { name: "Composer prompt" }), { target: { value: "hello @src" } });
    expect(props.onSearch).toHaveBeenLastCalledWith("src");
    expect(screen.getByRole("button", { name: "Attach workspace file" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Attach workspace folder" })).toBeTruthy();
  });

  it("renders next-turn pending state and clear action", async () => {
    const props = makeProps({
      composer: { ...initialComposerUiState, snapshotStatus: "ready", snapshot: {
        workspaceId: "ws", threadId: "thread", threadRevision: 1, queueRevision: 2,
        pendingIntent: { intentId: "intent", delivery: "next-turn", createdAtUtc: "2026-07-17T00:00:00Z", contextCount: 1, catalogCount: 2 },
        effectiveModel: "gpt-test", modelSource: "test", approvalMode: "OnRequest", approvalModeSource: "test", controlledContext: true,
      } },
    });
    render(<Composer {...props} />);
    expect(screen.getByText("Queued for next turn")).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "Clear pending input" }));
    expect(props.onClear).toHaveBeenCalledOnce();
    expect((screen.getByRole("textbox", { name: "Composer prompt" }) as HTMLTextAreaElement).disabled).toBe(true);
  });

  it("explains disabled input and offers a direct recovery action", async () => {
    const onDisabledAction = vi.fn();
    render(<Composer {...makeProps({
      disabledReason: "Select a thread to compose.",
      disabledActionLabel: "New conversation",
      onDisabledAction,
      historyTurnCount: 3,
      historyMessageCount: 6,
    })} />);
    expect((screen.getByRole("textbox", { name: "Composer prompt" }) as HTMLTextAreaElement).disabled).toBe(true);
    expect(screen.getByText("3 turns / 6 messages")).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "New conversation" }));
    expect(onDisabledAction).toHaveBeenCalledOnce();
  });

  it("reuses the queued-intent DOM across pending state transitions", () => {
    const initial = makeProps();
    const view = render(<Composer {...initial} />);
    const stableNode = document.querySelector(".queued-intent");
    expect(stableNode).not.toBeNull();
    expect((stableNode as HTMLElement).hidden).toBe(true);
    const composer = { ...initialComposerUiState, snapshotStatus: "ready" as const, snapshot: {
      workspaceId: "ws", threadId: "thread", threadRevision: 1, queueRevision: 2,
      pendingIntent: { intentId: "intent", delivery: "next-turn" as const, createdAtUtc: "2026-07-17T00:00:00Z", contextCount: 1, catalogCount: 2 },
      effectiveModel: "gpt-test", modelSource: "test", approvalMode: "OnRequest", approvalModeSource: "test", controlledContext: true,
    } };
    view.rerender(<Composer {...makeProps({ composer })} />);
    expect(document.querySelector(".queued-intent")).toBe(stableNode);
    expect((stableNode as HTMLElement).hidden).toBe(false);
    view.rerender(<Composer {...makeProps()} />);
    expect(document.querySelector(".queued-intent")).toBe(stableNode);
    expect((stableNode as HTMLElement).hidden).toBe(true);
  });

  it("updates draft and status text without replacing DOM child nodes", async () => {
    const initial = makeProps({ draft: { ...draft, text: "", status: "editing" } });
    const view = render(<Composer {...initial} />);
    const records: MutationRecord[] = [];
    const observer = new MutationObserver((batch) => records.push(...batch));
    observer.observe(document.querySelector(".composer")!, { childList: true, subtree: true });
    view.rerender(<Composer {...makeProps({ draft: { ...draft, text: "provider prompt", status: "queued" } })} />);
    view.rerender(<Composer {...initial} />);
    await Promise.resolve();
    observer.disconnect();
    expect(records.flatMap((record) => [...record.addedNodes])).toHaveLength(0);
    expect(records.flatMap((record) => [...record.removedNodes])).toHaveLength(0);
  });

  it("navigates mention options with a roving active descendant and restores the prompt", async () => {
    const props = makeProps({ composer: { ...initialComposerUiState, mentions: {
      loading: false, error: null, truncated: false,
      context: [
        { selectionId: "first", relativePath: "src/first.ts", kind: "file", byteCount: 10, fileCount: 1, availability: "available" },
        { selectionId: "second", relativePath: "src/second.ts", kind: "file", byteCount: 20, fileCount: 1, availability: "available" },
      ], skills: [], experts: [], automations: [], revisions: { skills: "", experts: "", automations: "" },
    } } });
    render(<Composer {...props} />);
    const prompt = screen.getByRole("textbox", { name: "Composer prompt" });
    expect(prompt.getAttribute("aria-activedescendant")).toBe("mention-context-first");
    fireEvent.keyDown(prompt, { key: "End" });
    expect(prompt.getAttribute("aria-activedescendant")).toBe("mention-context-second");
    fireEvent.keyDown(prompt, { key: "Enter" });
    expect(props.onContext).toHaveBeenCalledWith(expect.objectContaining({ selectionId: "second" }));
    expect(props.onCloseMentions).toHaveBeenCalledOnce();
    expect(document.activeElement).toBe(prompt);
  });
});

function makeProps(overrides: Record<string, unknown> = {}) {
  return {
    draft,
    composer: initialComposerUiState,
    disabledReason: null,
    onText: vi.fn(), onSearch: vi.fn(), onCloseMentions: vi.fn(), onContext: vi.fn(), onCatalog: vi.fn(),
    onRemoveContext: vi.fn(), onRemoveCatalog: vi.fn(), onPickFile: vi.fn(), onPickFolder: vi.fn(), onSend: vi.fn(), onClear: vi.fn(),
    ...overrides,
  };
}
