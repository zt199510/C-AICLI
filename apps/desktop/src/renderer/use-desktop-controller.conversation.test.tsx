import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { ComposerStateData, ThreadSummaryData, WorkspaceSnapshotData } from "../generated/desktop-contracts";
import { createRuntimeStatus, type DesktopBridge } from "../shared/bridge-contract";
import { useDesktopController } from "./use-desktop-controller";

describe("continuous conversations", () => {
  it("creates on the first send and appends later turns to the same conversation", async () => {
    let created = false;
    let pending = false;
    let queueRevision = 1;
    const createThread = vi.fn(async () => {
      created = true;
      return ok(thread);
    });
    const enqueueComposer = vi.fn(async () => {
      pending = true;
      queueRevision++;
      return ok(composer(true, queueRevision));
    });
    const startTurn = vi.fn(async () => {
      pending = false;
      queueRevision++;
      return ok({
        workspaceId: workspace.workspaceId, threadId: thread.threadId, turnId: "turn-1",
        threadRevision: 1, turnRevision: 1, status: "running", committedSequence: 0,
        recoveryRequired: false, idempotent: false, approval: null,
      });
    });
    const bridge = {
      getRuntimeStatus: vi.fn(async () => createRuntimeStatus("runtime-ready")),
      getWorkspaceSnapshot: vi.fn(async () => workspace),
      listThreads: vi.fn(async () => ok({ threads: created ? [thread] : [], truncated: false })),
      createThread,
      getThread: vi.fn(async () => ok({
        thread, turns: [], timeline: [], nextSequence: null,
        timelineTruncated: false, recoveryRequired: false,
      })),
      getComposer: vi.fn(async () => ok(composer(pending, queueRevision))),
      enqueueComposer,
      startTurn,
      onRuntimeStatus: vi.fn(() => () => undefined),
      onThreadChanged: vi.fn(() => () => undefined),
    } as unknown as DesktopBridge;

    render(<ConversationHarness bridge={bridge} />);
    await waitFor(() => expect(screen.getByTestId("composer-state").textContent).toBe("enabled"));
    await waitFor(() => expect(screen.getByTestId("threads-state").textContent).toBe("ready"));

    fireEvent.change(screen.getByLabelText("Prompt"), { target: { value: "Review this workspace" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));
    expect(screen.getByTestId("optimistic-count").textContent).toBe("1");
    expect(screen.getByTestId("optimistic-text").textContent).toBe("Review this workspace");
    expect((screen.getByLabelText("Prompt") as HTMLTextAreaElement).value).toBe("");
    await waitFor(() => expect(startTurn).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.getByTestId("detail-state").textContent).toBe("ready"));
    await waitFor(() => expect((screen.getByLabelText("Prompt") as HTMLTextAreaElement).value).toBe(""));
    await waitFor(() => {
      expect(screen.getByTestId("composer-status").textContent).toBe("editing");
      expect((screen.getByLabelText("Prompt") as HTMLTextAreaElement).disabled).toBe(false);
      expect((screen.getByRole("button", { name: "Send" }) as HTMLButtonElement).disabled).toBe(false);
    });

    expect(createThread).toHaveBeenCalledWith({ title: "Review this workspace" });
    expect(enqueueComposer).toHaveBeenLastCalledWith(expect.objectContaining({
      threadId: "thread-1", prompt: "Review this workspace",
    }));
    expect(screen.getByTestId("selected-thread").textContent).toBe("thread-1");

    fireEvent.change(screen.getByLabelText("Prompt"), { target: { value: "Continue with the same context" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));
    await waitFor(() => expect(startTurn).toHaveBeenCalledTimes(2));

    expect(createThread).toHaveBeenCalledTimes(1);
    expect(enqueueComposer).toHaveBeenLastCalledWith(expect.objectContaining({
      threadId: "thread-1", prompt: "Continue with the same context",
    }));
  });

  it("clears a consumed ready intent before waiting for thread projection refresh", async () => {
    let pending = false;
    let started = false;
    let queueRevision = 1;
    const detail = () => ok({
      thread, turns: [], timeline: [], nextSequence: null,
      timelineTruncated: false as const, recoveryRequired: false as const,
    });
    const delayedDetail = deferred<ReturnType<typeof detail>>();
    const getThread = vi.fn(async () => started ? delayedDetail.promise : detail());
    const bridge = {
      getRuntimeStatus: vi.fn(async () => createRuntimeStatus("runtime-ready")),
      getWorkspaceSnapshot: vi.fn(async () => workspace),
      listThreads: vi.fn(async () => ok({ threads: [thread], truncated: false })),
      getThread,
      getComposer: vi.fn(async () => ok(composer(pending, queueRevision))),
      enqueueComposer: vi.fn(async () => {
        pending = true;
        queueRevision++;
        return ok(composer(true, queueRevision));
      }),
      startTurn: vi.fn(async () => {
        pending = false;
        queueRevision++;
        started = true;
        return ok({
          workspaceId: workspace.workspaceId, threadId: thread.threadId, turnId: "turn-1",
          threadRevision: 2, turnRevision: 1, status: "running", committedSequence: 1,
          recoveryRequired: false, idempotent: false, approval: null,
        });
      }),
      onRuntimeStatus: vi.fn(() => () => undefined),
      onThreadChanged: vi.fn(() => () => undefined),
    } as unknown as DesktopBridge;

    render(<ConversationHarness bridge={bridge} autoSelect />);
    await waitFor(() => expect(screen.getByTestId("selected-thread").textContent).toBe("thread-1"));
    fireEvent.change(screen.getByLabelText("Prompt"), { target: { value: "Start now" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));
    await waitFor(() => expect(getThread.mock.calls.length).toBeGreaterThanOrEqual(2));
    const pendingBeforeDetailRelease = screen.getByTestId("composer-pending").textContent;
    delayedDetail.resolve(detail());

    expect(pendingBeforeDetailRelease).toBe("none");
  });

  it("selects the next available conversation after archiving the current one", async () => {
    let archived = false;
    const nextThread: ThreadSummaryData = {
      ...thread,
      threadId: "thread-2",
      title: "Next conversation",
      updatedAtUtc: "2026-07-29T00:00:00.000Z",
    };
    const archivedThread: ThreadSummaryData = {
      ...thread,
      status: "archived",
      archivedAtUtc: "2026-07-30T01:00:00.000Z",
    };
    const archiveThread = vi.fn(async () => {
      archived = true;
      return ok(archivedThread);
    });
    const bridge = {
      getRuntimeStatus: vi.fn(async () => createRuntimeStatus("runtime-ready")),
      getWorkspaceSnapshot: vi.fn(async () => workspace),
      listThreads: vi.fn(async () => ok({ threads: archived ? [archivedThread, nextThread] : [thread, nextThread], truncated: false })),
      getThread: vi.fn(async ({ threadId }: { readonly threadId: string }) => ok({
        thread: threadId === nextThread.threadId ? nextThread : thread,
        turns: [], timeline: [], nextSequence: null,
        timelineTruncated: false, recoveryRequired: false,
      })),
      getComposer: vi.fn(async () => ok(composer(false))),
      archiveThread,
      onRuntimeStatus: vi.fn(() => () => undefined),
      onThreadChanged: vi.fn(() => () => undefined),
    } as unknown as DesktopBridge;

    render(<ArchiveHarness bridge={bridge} />);
    await waitFor(() => expect(screen.getByTestId("selected-thread").textContent).toBe("thread-1"));
    fireEvent.click(screen.getByRole("button", { name: "Archive current" }));

    await waitFor(() => expect(screen.getByTestId("selected-thread").textContent).toBe("thread-2"));
    expect(archiveThread).toHaveBeenCalledWith({ threadId: "thread-1", expectedRevision: 1 });
  });

  it("keeps the current selection when a background conversation is archived", async () => {
    let archived = false;
    const background: ThreadSummaryData = { ...thread, threadId: "thread-2", title: "Background conversation" };
    const bridge = {
      getRuntimeStatus: vi.fn(async () => createRuntimeStatus("runtime-ready")),
      getWorkspaceSnapshot: vi.fn(async () => workspace),
      listThreads: vi.fn(async () => ok({
        threads: archived ? [thread, { ...background, status: "archived", archivedAtUtc: "2026-07-30T01:00:00.000Z" }] : [thread, background],
        truncated: false,
      })),
      getThread: vi.fn(async ({ threadId }: { readonly threadId: string }) => ok({
        thread: threadId === background.threadId ? background : thread,
        turns: [], timeline: [], nextSequence: null,
        timelineTruncated: false, recoveryRequired: false,
      })),
      getComposer: vi.fn(async () => ok(composer(false))),
      archiveThread: vi.fn(async () => {
        archived = true;
        return ok({ ...background, status: "archived", archivedAtUtc: "2026-07-30T01:00:00.000Z" });
      }),
      onRuntimeStatus: vi.fn(() => () => undefined),
      onThreadChanged: vi.fn(() => () => undefined),
    } as unknown as DesktopBridge;

    render(<ArchiveHarness bridge={bridge} />);
    await waitFor(() => expect(screen.getByTestId("selected-thread").textContent).toBe("thread-1"));
    fireEvent.click(screen.getByRole("button", { name: "Archive background" }));
    await waitFor(() => expect(bridge.listThreads).toHaveBeenCalledTimes(2));
    expect(screen.getByTestId("selected-thread").textContent).toBe("thread-1");
  });
});

function ArchiveHarness({ bridge }: { readonly bridge: DesktopBridge }) {
  const controller = useDesktopController(bridge, { autoSelectConversation: true });
  const selected = controller.state.threads.find((entry) => entry.threadId === controller.state.selectedThreadId);
  const background = controller.state.threads.find((entry) => entry.threadId !== controller.state.selectedThreadId && !entry.archivedAtUtc);
  return (
    <>
      <span data-testid="selected-thread">{controller.state.selectedThreadId ?? "new"}</span>
      <button
        type="button"
        disabled={!selected}
        onClick={() => selected && void controller.archiveThread(selected.threadId, selected.revision)}
      >
        Archive current
      </button>
      <button
        type="button"
        disabled={!background}
        onClick={() => background && void controller.archiveThread(background.threadId, background.revision)}
      >
        Archive background
      </button>
    </>
  );
}

function ConversationHarness({ bridge, autoSelect = false }: { readonly bridge: DesktopBridge; readonly autoSelect?: boolean }) {
  const controller = useDesktopController(bridge, { autoSelectConversation: autoSelect });
  const composerBusy =
    controller.composerDraft.status === "validating" ||
    controller.composerDraft.status === "enqueueing";
  const composerDisabled = composerBusy || Boolean(controller.composerDisabledReason);
  return (
    <>
      <span data-testid="composer-state">{controller.composerDisabledReason ?? "enabled"}</span>
      <span data-testid="composer-status">{controller.composerDraft.status}</span>
      <span data-testid="selected-thread">{controller.state.selectedThreadId ?? "new"}</span>
      <span data-testid="detail-state">{controller.state.detailStatus}</span>
      <span data-testid="threads-state">{controller.state.threadsStatus}</span>
      <span data-testid="optimistic-count">{controller.optimisticExchanges.length}</span>
      <span data-testid="optimistic-text">{controller.optimisticExchanges[0]?.text ?? ""}</span>
      <span data-testid="composer-pending">{controller.composer.snapshot?.pendingIntent ? "pending" : "none"}</span>
      <textarea
        aria-label="Prompt"
        disabled={composerDisabled}
        value={controller.composerDraft.text}
        onChange={(event) => controller.setComposerText(event.target.value)}
      />
      <button
        type="button"
        disabled={composerDisabled}
        onClick={() => void controller.enqueueComposer()}
      >
        Send
      </button>
    </>
  );
}

const workspace: WorkspaceSnapshotData = {
  workspaceId: "workspace-1", rootPath: "C:\\workspace", status: "ready",
  capabilities: { readOnlyQueries: true, gitQueries: true, localCatalogs: true, managedArtifacts: true, controlledContext: true },
  configuration: { hasApiKey: true, apiKeySource: "environment", effectiveModel: "gpt-test", modelSource: "environment", agentBackendSource: "default", approvalMode: "ask", approvalModeSource: "default", loadedSourceCount: 1 },
};

const thread: ThreadSummaryData = {
  threadId: "thread-1", revision: 1, workspaceId: workspace.workspaceId, title: "Review this workspace", status: "idle",
  createdAtUtc: "2026-07-30T00:00:00.000Z", updatedAtUtc: "2026-07-30T00:00:00.000Z", archivedAtUtc: null,
  turnCount: 0, timelineItemCount: 0, activeTurnId: null,
  origin: { kind: "desktop", sourceKind: null, sourceId: null, sourceFingerprint: null },
};

function composer(hasPendingIntent: boolean, queueRevision = hasPendingIntent ? 2 : 1): ComposerStateData {
  return {
    workspaceId: workspace.workspaceId, threadId: thread.threadId, threadRevision: thread.revision, queueRevision,
    pendingIntent: hasPendingIntent ? { intentId: "intent-1", delivery: "current-turn", createdAtUtc: "2026-07-30T00:00:00.000Z", contextCount: 0, catalogCount: 0 } : null,
    effectiveModel: "gpt-test", modelSource: "environment", approvalMode: "ask", approvalModeSource: "default", controlledContext: true,
  };
}

function ok<T>(data: T) {
  return { schemaVersion: 1, succeeded: true, data, error: null, diagnostics: [], truncated: false } as const;
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((complete) => { resolve = complete; });
  return { promise, resolve };
}
