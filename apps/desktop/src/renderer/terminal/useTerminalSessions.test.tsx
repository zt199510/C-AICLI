import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { TerminalProfileListResult, TerminalStateData, TerminalStateResult } from "../../generated/desktop-contracts";
import type { TerminalCommands } from "./useTerminalSessions";
import { useTerminalSessions } from "./useTerminalSessions";

describe("useTerminalSessions", () => {
  it("keeps multiple tabs owned by each task while switching tasks", async () => {
    let sequence = 0;
    const states = new Map<string, TerminalStateData>();
    const commands: TerminalCommands = {
      listTerminalProfiles: vi.fn(async () => profileResult()),
      openTerminal: vi.fn(async ({ shellProfile }) => {
        const session = state(`terminal-${++sequence}`, shellProfile);
        states.set(session.sessionId, session);
        return result(session);
      }),
      inputTerminal: vi.fn(async ({ sessionId }) => result(states.get(sessionId)!)),
      resizeTerminal: vi.fn(async ({ sessionId }) => result(states.get(sessionId)!)),
      cancelTerminal: vi.fn(async ({ sessionId }) => result(states.get(sessionId)!)),
      closeTerminal: vi.fn(async ({ sessionId }) => {
        const closed = { ...states.get(sessionId)!, status: "closed" };
        states.set(sessionId, closed);
        return result(closed);
      }),
      getTerminal: vi.fn(async ({ sessionId }) => result({ ...states.get(sessionId)!, output: "" })),
    };
    const { result: hook, rerender } = renderHook(
      ({ threadId }) => useTerminalSessions(commands, "workspace-1", threadId),
      { initialProps: { threadId: "thread-1" } },
    );

    await waitFor(() => expect(hook.current.profiles.map((profile) => profile.profileId)).toEqual(["system-default", "cmd"]));
    await act(() => hook.current.open("system-default"));
    await act(() => hook.current.open("cmd"));
    expect(hook.current.sessions).toHaveLength(2);
    const firstTaskSessions = hook.current.sessions.map((session) => session.sessionId);

    rerender({ threadId: "thread-2" });
    expect(hook.current.sessions).toHaveLength(0);
    await act(() => hook.current.open("cmd"));
    expect(hook.current.sessions).toHaveLength(1);

    rerender({ threadId: "thread-1" });
    expect(hook.current.sessions.map((session) => session.sessionId)).toEqual(firstTaskSessions);
    await act(() => hook.current.close(firstTaskSessions[0]!));
    expect(hook.current.sessions.map((session) => session.sessionId)).toEqual([firstTaskSessions[1]]);
  });
});

function state(sessionId: string, shellProfile: string): TerminalStateData {
  return {
    sessionId,
    status: "running",
    shellProfile,
    output: "",
    cursor: 0,
    truncated: false,
    exitCode: null,
    startedAtUtc: "2026-08-06T00:00:00Z",
    exitedAtUtc: null,
  };
}

function result(data: TerminalStateData): TerminalStateResult {
  return { schemaVersion: 1, succeeded: true, data, error: null, diagnostics: [], truncated: data.truncated };
}

function profileResult(): TerminalProfileListResult {
  return {
    schemaVersion: 1,
    succeeded: true,
    data: {
      profiles: [
        { profileId: "system-default", displayName: "PowerShell (default)", isDefault: true },
        { profileId: "cmd", displayName: "Command Prompt", isDefault: false },
      ],
    },
    error: null,
    diagnostics: [],
    truncated: false,
  };
}
