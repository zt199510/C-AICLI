import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { CatalogItemData, ContextDescriptorData } from "../generated/desktop-contracts";
import { Composer } from "./Composer";
import { initialComposerUiState, type ComposerDraft, type MentionResults } from "./composer-state";

const contextFile: ContextDescriptorData = {
  selectionId: "context-file",
  relativePath: "src/first.ts",
  kind: "file",
  byteCount: 10,
  fileCount: 1,
  availability: "available",
};
const contextFolder: ContextDescriptorData = {
  selectionId: "context-folder",
  relativePath: "src/components",
  kind: "folder",
  byteCount: 200,
  fileCount: 4,
  availability: "available",
};
const skill = catalog("skill-1", "Fixture skill", "Runs fixture checks");
const expert = catalog("expert-1", "UI expert", "Reviews interface behavior");
const automation = catalog("automation-1", "Nightly automation", "Runs each night");
const mentions: MentionResults = {
  loading: false,
  error: null,
  truncated: false,
  context: [contextFile, contextFolder],
  skills: [skill],
  experts: [expert],
  automations: [automation],
  revisions: { skills: "skills-r1", experts: "experts-r1", automations: "automations-r1" },
};
const draft: ComposerDraft = { text: "hello", contextSelections: [], catalogSelections: [], status: "editing", error: null };

describe("Composer", () => {
  it("sends on Enter, keeps Shift+Enter for newlines, and ignores IME Enter", () => {
    const props = makeProps();
    render(<Composer {...props} />);
    const prompt = screen.getByRole("textbox", { name: "Composer prompt" });

    fireEvent.keyDown(prompt, { key: "Enter", shiftKey: false });
    expect(props.onSend).toHaveBeenCalledOnce();
    fireEvent.keyDown(prompt, { key: "Enter", shiftKey: true });
    expect(props.onSend).toHaveBeenCalledOnce();

    fireEvent.compositionStart(prompt);
    fireEvent.keyDown(prompt, { key: "Enter" });
    fireEvent.compositionEnd(prompt);
    fireEvent.keyDown(prompt, { key: "Enter", isComposing: true });
    expect(props.onSend).toHaveBeenCalledOnce();
    fireEvent.keyDown(prompt, { key: "Enter" });
    expect(props.onSend).toHaveBeenCalledTimes(2);
  });

  it("auto-grows between 48px and 180px, then enables internal scrolling", () => {
    const props = makeProps({ draft: { ...draft, text: "" } });
    render(<Composer {...props} />);
    const prompt = screen.getByRole("textbox", { name: "Composer prompt" }) as HTMLTextAreaElement;

    Object.defineProperty(prompt, "scrollHeight", { configurable: true, value: 20 });
    fireEvent.change(prompt, { target: { value: "short" } });
    expect(prompt.style.height).toBe("48px");
    expect(prompt.style.overflowY).toBe("hidden");

    Object.defineProperty(prompt, "scrollHeight", { configurable: true, value: 124 });
    fireEvent.change(prompt, { target: { value: "several\nlines" } });
    expect(prompt.style.height).toBe("124px");
    expect(prompt.style.overflowY).toBe("hidden");

    Object.defineProperty(prompt, "scrollHeight", { configurable: true, value: 260 });
    fireEvent.change(prompt, { target: { value: "many\nlines\ninside\nthe\ncomposer" } });
    expect(prompt.style.height).toBe("180px");
    expect(prompt.style.overflowY).toBe("auto");
  });

  it("opens and closes the add menu and keeps Composer menus mutually exclusive", async () => {
    const user = userEvent.setup();
    const props = makeProps({ composer: { ...initialComposerUiState, mentions } });
    render(<Composer {...props} />);
    const add = screen.getByRole("button", { name: "Add context" });

    await user.click(add);
    expect(add.getAttribute("aria-expanded")).toBe("true");
    expect(screen.getByRole("menu", { name: "添加上下文" })).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "工具" }));
    expect(screen.queryByRole("menu", { name: "添加上下文" })).toBeNull();
    expect(screen.getByRole("menu", { name: "工具目录" })).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "工具" }));
    expect(screen.queryByRole("menu", { name: "工具目录" })).toBeNull();
  });

  it("runs file and directory picker actions from the add menu", async () => {
    const user = userEvent.setup();
    const props = makeProps();
    render(<Composer {...props} />);

    await user.click(screen.getByRole("button", { name: "Add context" }));
    await user.click(screen.getByRole("menuitem", { name: /添加工作区文件/ }));
    expect(props.onPickFile).toHaveBeenCalledOnce();
    expect(screen.queryByRole("menu", { name: "添加上下文" })).toBeNull();

    await user.click(screen.getByRole("button", { name: "Add context" }));
    await user.click(screen.getByRole("menuitem", { name: /添加工作区目录/ }));
    expect(props.onPickFolder).toHaveBeenCalledOnce();
  });

  it("shows only Catalog items in the tools menu and preserves catalog revisions", async () => {
    const user = userEvent.setup();
    const props = makeProps({ composer: { ...initialComposerUiState, mentions } });
    render(<Composer {...props} />);

    await user.click(screen.getByRole("button", { name: "工具" }));
    const menu = screen.getByRole("menu", { name: "工具目录" });
    expect(props.onSearch).toHaveBeenLastCalledWith("");
    expect(withinText(menu, "Skills")).toBe(true);
    expect(withinText(menu, "Experts")).toBe(true);
    expect(withinText(menu, "Automations")).toBe(true);
    expect(withinText(menu, "src/first.ts")).toBe(false);

    await user.click(screen.getByRole("menuitem", { name: /UI expert/ }));
    expect(props.onCatalog).toHaveBeenCalledWith("expert", expert, "experts-r1");
  });

  it("supports menu arrow keys, Escape, and trigger focus restoration", async () => {
    const user = userEvent.setup();
    render(<Composer {...makeProps()} />);
    const trigger = screen.getByRole("button", { name: "Add context" });
    await user.click(trigger);
    const file = screen.getByRole("menuitem", { name: /添加工作区文件/ });
    const folder = screen.getByRole("menuitem", { name: /添加工作区目录/ });
    await waitFor(() => expect(document.activeElement).toBe(file));

    fireEvent.keyDown(file, { key: "End" });
    expect(document.activeElement).toBe(folder);
    fireEvent.keyDown(folder, { key: "Home" });
    expect(document.activeElement).toBe(file);
    fireEvent.keyDown(file, { key: "Escape" });
    await waitFor(() => expect(document.activeElement).toBe(trigger));
    expect(screen.queryByRole("menu", { name: "添加上下文" })).toBeNull();
  });

  it("searches mentions, navigates results, closes on Escape, and restores prompt focus", async () => {
    const props = makeProps({ composer: { ...initialComposerUiState, mentions } });
    render(<Composer {...props} />);
    const prompt = screen.getByRole("textbox", { name: "Composer prompt" });
    fireEvent.change(prompt, { target: { value: "hello @src" } });
    expect(props.onSearch).toHaveBeenLastCalledWith("src");
    expect(prompt.getAttribute("aria-activedescendant")).toBe("mention-context-context-file");

    fireEvent.keyDown(prompt, { key: "End" });
    expect(prompt.getAttribute("aria-activedescendant")).toBe("mention-automation-automation-1");
    fireEvent.keyDown(prompt, { key: "Enter" });
    expect(props.onCatalog).toHaveBeenCalledWith("automation", automation, "automations-r1");
    await waitFor(() => expect(document.activeElement).toBe(prompt));

    fireEvent.change(prompt, { target: { value: "hello @" } });
    fireEvent.keyDown(prompt, { key: "Escape" });
    expect(props.onCloseMentions).toHaveBeenCalled();
    await waitFor(() => expect(document.activeElement).toBe(prompt));
  });

  it("removes Context and Catalog chips independently", async () => {
    const user = userEvent.setup();
    const selectedCatalog = { kind: "skill" as const, id: skill.id, label: skill.displayName, catalogRevision: "skills-r1" };
    const props = makeProps({ draft: { ...draft, contextSelections: [contextFile], catalogSelections: [selectedCatalog] } });
    render(<Composer {...props} />);

    await user.click(screen.getByRole("button", { name: "Remove src/first.ts" }));
    expect(props.onRemoveContext).toHaveBeenCalledWith("context-file");
    await user.click(screen.getByRole("button", { name: "Remove Fixture skill" }));
    expect(props.onRemoveCatalog).toHaveBeenCalledWith(selectedCatalog);
  });

  it("uses the authoritative snapshot for Effective Model and Approval Mode", () => {
    render(<Composer {...makeProps({
      modelLabel: "workspace-fallback",
      approvalModeLabel: "FallbackMode",
      composer: { ...initialComposerUiState, snapshotStatus: "ready", snapshot: snapshot({ effectiveModel: "gpt-authoritative", approvalMode: "OnRequest" }) },
    })} />);
    expect(screen.getByLabelText("Effective model: gpt-authoritative")).toBeTruthy();
    expect(screen.getByLabelText("Approval mode: OnRequest")).toBeTruthy();
    expect(screen.queryByText("workspace-fallback")).toBeNull();
    expect(screen.queryByText("FallbackMode")).toBeNull();
  });

  it("renders pending intent, disables composition, and clears it", async () => {
    const user = userEvent.setup();
    const props = makeProps({
      composer: { ...initialComposerUiState, snapshotStatus: "ready", snapshot: snapshot({
        pendingIntent: { intentId: "intent", delivery: "next-turn", createdAtUtc: "2026-07-17T00:00:00Z", contextCount: 1, catalogCount: 2 },
      }) },
    });
    render(<Composer {...props} />);
    expect(screen.getByText("Queued for next turn")).toBeTruthy();
    expect((screen.getByRole("textbox", { name: "Composer prompt" }) as HTMLTextAreaElement).disabled).toBe(true);
    expect((screen.getByRole("button", { name: "Add context" }) as HTMLButtonElement).disabled).toBe(true);
    await user.click(screen.getByRole("button", { name: "Clear pending input" }));
    expect(props.onClear).toHaveBeenCalledOnce();
  });

  it("switches between Send, Stop, and Stopping primary states", async () => {
    const user = userEvent.setup();
    const sendProps = makeProps();
    const view = render(<Composer {...sendProps} />);
    await user.click(screen.getByRole("button", { name: "Send prompt" }));
    expect(sendProps.onSend).toHaveBeenCalledOnce();

    const onStop = vi.fn();
    view.rerender(<Composer {...makeProps({ onStop })} />);
    expect(screen.queryByRole("button", { name: "Send prompt" })).toBeNull();
    await user.click(screen.getByRole("button", { name: "Stop response" }));
    expect(onStop).toHaveBeenCalledOnce();

    view.rerender(<Composer {...makeProps({ onStop, stopping: true })} />);
    expect((screen.getByRole("button", { name: "Stopping response" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("disables empty sends", () => {
    render(<Composer {...makeProps({ draft: { ...draft, text: "   " } })} />);
    expect((screen.getByRole("button", { name: "Send prompt" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("explains disabled input and offers the existing direct recovery action", async () => {
    const user = userEvent.setup();
    const onDisabledAction = vi.fn();
    render(<Composer {...makeProps({
      disabledReason: "Select a thread to compose.",
      disabledActionLabel: "New conversation",
      onDisabledAction,
    })} />);
    expect((screen.getByRole("textbox", { name: "Composer prompt" }) as HTMLTextAreaElement).disabled).toBe(true);
    expect(screen.getByRole("status").textContent).toContain("Select a thread to compose.");
    await user.click(screen.getByRole("button", { name: "New conversation" }));
    expect(onDisabledAction).toHaveBeenCalledOnce();
  });

  it("uses alert semantics for errors", () => {
    render(<Composer {...makeProps({ draft: { ...draft, status: "error", error: "Catalog revision expired." } })} />);
    expect(screen.getByRole("alert").textContent).toContain("Catalog revision expired.");
  });

  it("reuses pending, prompt, and Composer DOM across ordinary state updates", () => {
    const initial = makeProps({ draft: { ...draft, text: "", status: "editing" } });
    const view = render(<Composer {...initial} />);
    const composerNode = document.querySelector(".composer");
    const pendingNode = document.querySelector(".queued-intent");
    const promptNode = screen.getByRole("textbox", { name: "Composer prompt" });

    view.rerender(<Composer {...makeProps({
      draft: { ...draft, text: "provider prompt", status: "queued" },
      composer: { ...initialComposerUiState, snapshotStatus: "ready", snapshot: snapshot({
        pendingIntent: { intentId: "intent", delivery: "next-turn", createdAtUtc: "2026-07-17T00:00:00Z", contextCount: 1, catalogCount: 2 },
      }) },
    })} />);
    expect(document.querySelector(".composer")).toBe(composerNode);
    expect(document.querySelector(".queued-intent")).toBe(pendingNode);
    expect(screen.getByRole("textbox", { name: "Composer prompt" })).toBe(promptNode);
    expect((pendingNode as HTMLElement).hidden).toBe(false);

    view.rerender(<Composer {...initial} />);
    expect(document.querySelector(".composer")).toBe(composerNode);
    expect(document.querySelector(".queued-intent")).toBe(pendingNode);
    expect((pendingNode as HTMLElement).hidden).toBe(true);
  });
});

function makeProps(overrides: Record<string, unknown> = {}) {
  return {
    draft,
    composer: initialComposerUiState,
    disabledReason: null,
    onText: vi.fn(),
    onSearch: vi.fn(),
    onCloseMentions: vi.fn(),
    onContext: vi.fn(),
    onCatalog: vi.fn(),
    onRemoveContext: vi.fn(),
    onRemoveCatalog: vi.fn(),
    onPickFile: vi.fn(),
    onPickFolder: vi.fn(),
    onSend: vi.fn(),
    onClear: vi.fn(),
    ...overrides,
  };
}

function snapshot(overrides: Record<string, unknown> = {}) {
  return {
    workspaceId: "ws",
    threadId: "thread",
    threadRevision: 1,
    queueRevision: 2,
    pendingIntent: null,
    effectiveModel: "gpt-test",
    modelSource: "test",
    approvalMode: "OnRequest",
    approvalModeSource: "test",
    controlledContext: true,
    ...overrides,
  };
}

function withinText(element: HTMLElement, text: string) {
  return element.textContent?.includes(text) ?? false;
}

function catalog(id: string, displayName: string, description: string): CatalogItemData {
  return { id, displayName, description, version: null, sourceKind: "fixture", readOnly: true, toolBoundary: "fixture", capabilities: [] };
}
