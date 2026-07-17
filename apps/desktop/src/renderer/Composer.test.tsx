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
