import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { ComposerStateData, ThreadSummaryData, WorkspaceSnapshotData } from "../generated/desktop-contracts";
import { createRuntimeStatus, type DesktopBridge } from "../shared/bridge-contract";
import { useDesktopController } from "./use-desktop-controller";

describe("continuous conversations", () => {
  it("creates on the first send and appends later turns to the same conversation", async () => {
    let created = false;
    let pending = false;
    const createThread = vi.fn(async () => {
      created = true;
      return ok(thread);
    });
    const enqueueComposer = vi.fn(async () => {
      pending = true;
      return ok(composer(true));
    });
    const startTurn = vi.fn(async () => {
      pending = false;
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
      getComposer: vi.fn(async () => ok(composer(pending))),
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
});

function ConversationHarness({ bridge }: { readonly bridge: DesktopBridge }) {
  const controller = useDesktopController(bridge);
  return (
    <>
      <span data-testid="composer-state">{controller.composerDisabledReason ?? "enabled"}</span>
      <span data-testid="selected-thread">{controller.state.selectedThreadId ?? "new"}</span>
      <span data-testid="detail-state">{controller.state.detailStatus}</span>
      <span data-testid="threads-state">{controller.state.threadsStatus}</span>
      <span data-testid="optimistic-count">{controller.optimisticExchanges.length}</span>
      <span data-testid="optimistic-text">{controller.optimisticExchanges[0]?.text ?? ""}</span>
      <textarea aria-label="Prompt" value={controller.composerDraft.text} onChange={(event) => controller.setComposerText(event.target.value)} />
      <button type="button" onClick={() => void controller.enqueueComposer()}>Send</button>
    </>
  );
}

const workspace: WorkspaceSnapshotData = {
  workspaceId: "workspace-1", rootPath: "C:\\workspace", status: "ready",
  capabilities: { readOnlyQueries: true, gitQueries: true, localCatalogs: true, managedArtifacts: true, controlledContext: true },
  configuration: { hasApiKey: true, apiKeySource: "environment", effectiveModel: "gpt-test", modelSource: "environment", agentBackendSource: "default", approvalMode: "ask", approvalModeSource: "default", loadedSourceCount: 1 },
};

const thread: ThreadSummaryData = {
  threadId: "thread-1", revision: 1, workspaceId: workspace.workspaceId, title: "Review this workspace", status: "active",
  createdAtUtc: "2026-07-30T00:00:00.000Z", updatedAtUtc: "2026-07-30T00:00:00.000Z", archivedAtUtc: null,
  turnCount: 0, timelineItemCount: 0, activeTurnId: null,
  origin: { kind: "desktop", sourceKind: null, sourceId: null, sourceFingerprint: null },
};

function composer(hasPendingIntent: boolean): ComposerStateData {
  return {
    workspaceId: workspace.workspaceId, threadId: thread.threadId, threadRevision: thread.revision, queueRevision: hasPendingIntent ? 2 : 1,
    pendingIntent: hasPendingIntent ? { intentId: "intent-1", delivery: "current-turn", createdAtUtc: "2026-07-30T00:00:00.000Z", contextCount: 0, catalogCount: 0 } : null,
    effectiveModel: "gpt-test", modelSource: "environment", approvalMode: "ask", approvalModeSource: "default", controlledContext: true,
  };
}

function ok<T>(data: T) {
  return { schemaVersion: 1, succeeded: true, data, error: null, diagnostics: [], truncated: false } as const;
}
