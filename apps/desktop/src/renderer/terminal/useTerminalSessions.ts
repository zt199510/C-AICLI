import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { TerminalProfileData, TerminalStateData } from "../../generated/desktop-contracts";
import type { DesktopBridge, TerminalProfileId } from "../../shared/bridge-contract";
import type { TerminalDimensions } from "./XtermAdapter";

export type TerminalCommands = Pick<
  DesktopBridge,
  "openTerminal" | "inputTerminal" | "resizeTerminal" | "cancelTerminal" | "closeTerminal" | "getTerminal" | "listTerminalProfiles"
>;

interface TaskSessions {
  readonly sessions: readonly TerminalStateData[];
  readonly activeSessionId: string | null;
}

export interface TerminalSessionsController {
  readonly sessions: readonly TerminalStateData[];
  readonly activeSession: TerminalStateData | null;
  readonly busy: boolean;
  readonly error: string | null;
  readonly profiles: readonly TerminalProfileData[];
  readonly canOpen: boolean;
  readonly open: (profile?: TerminalProfileId) => Promise<void>;
  readonly select: (sessionId: string) => void;
  readonly input: (sessionId: string, data: string) => void;
  readonly resize: (sessionId: string, dimensions: TerminalDimensions) => void;
  readonly interrupt: (sessionId: string) => Promise<void>;
  readonly close: (sessionId: string) => Promise<void>;
  readonly reconnect: (sessionId: string) => Promise<void>;
}

const emptyTask: TaskSessions = Object.freeze({ sessions: [], activeSessionId: null });

export function useTerminalSessions(
  commands: TerminalCommands | undefined,
  workspaceId: string | null,
  threadId: string | null,
): TerminalSessionsController {
  const taskKey = workspaceId && threadId ? `${workspaceId}:${threadId}` : null;
  const [tasks, setTasks] = useState<Record<string, TaskSessions>>({});
  const [busyKeys, setBusyKeys] = useState<ReadonlySet<string>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const [profiles, setProfiles] = useState<readonly TerminalProfileData[]>([]);
  const tasksRef = useRef(tasks);
  tasksRef.current = tasks;
  const inputBuffers = useRef(new Map<string, string>());
  const inputTimers = useRef(new Map<string, ReturnType<typeof setTimeout>>());
  const resizeTimers = useRef(new Map<string, ReturnType<typeof setTimeout>>());
  const lastResize = useRef(new Map<string, string>());
  const task = taskKey ? tasks[taskKey] ?? emptyTask : emptyTask;
  const activeSession = task.sessions.find((session) => session.sessionId === task.activeSessionId) ?? task.sessions[0] ?? null;

  const replaceSession = useCallback((key: string, value: TerminalStateData, select = false) => {
    setTasks((current) => {
      const previous = current[key] ?? emptyTask;
      const existing = previous.sessions.find((session) => session.sessionId === value.sessionId);
      const normalized = existing && value.cursor === existing.cursor && !value.output
        ? { ...value, output: existing.output }
        : value;
      const sessions = existing
        ? previous.sessions.map((session) => session.sessionId === value.sessionId ? normalized : session)
        : [...previous.sessions, normalized];
      return { ...current, [key]: { sessions, activeSessionId: select ? value.sessionId : previous.activeSessionId ?? value.sessionId } };
    });
  }, []);

  const runMutation = useCallback(async <T,>(identity: string, action: () => Promise<T>): Promise<T | null> => {
    setBusyKeys((current) => new Set(current).add(identity));
    setError(null);
    try {
      return await action();
    } catch {
      setError("终端操作失败，请重试。");
      return null;
    } finally {
      setBusyKeys((current) => {
        const next = new Set(current);
        next.delete(identity);
        return next;
      });
    }
  }, []);

  const refresh = useCallback(async (sessionId: string) => {
    if (!commands) return;
    const owner = Object.entries(tasksRef.current).find(([, value]) => value.sessions.some((session) => session.sessionId === sessionId));
    if (!owner) return;
    const current = owner[1].sessions.find((session) => session.sessionId === sessionId);
    if (!current) return;
    try {
      const result = await commands.getTerminal({ sessionId, afterCursor: current.cursor });
      if (result.succeeded && result.data) replaceSession(owner[0], result.data);
      else setError(result.error?.safeMessage ?? "无法重新连接终端会话。");
    } catch {
      setError("无法重新连接终端会话。");
    }
  }, [commands, replaceSession]);

  useEffect(() => {
    if (!commands) return;
    let active = true;
    void commands.listTerminalProfiles().then((result) => {
      if (!active) return;
      if (result.succeeded && result.data) setProfiles(result.data.profiles);
      else setError(result.error?.safeMessage ?? "无法读取 Shell 配置。");
    }).catch(() => { if (active) setError("无法读取 Shell 配置。"); });
    return () => { active = false; };
  }, [commands]);

  useEffect(() => {
    if (!commands) return;
    const interval = window.setInterval(() => {
      for (const value of Object.values(tasksRef.current)) {
        for (const session of value.sessions) {
          if (session.status === "running") void refresh(session.sessionId);
        }
      }
    }, 80);
    return () => window.clearInterval(interval);
  }, [commands, refresh]);

  useEffect(() => () => {
    for (const timer of inputTimers.current.values()) clearTimeout(timer);
    for (const timer of resizeTimers.current.values()) clearTimeout(timer);
  }, []);

  const open = useCallback(async (profile: TerminalProfileId = "system-default") => {
    if (!commands || !taskKey) return;
    const result = await runMutation(`open:${taskKey}`, () => commands.openTerminal({
      shellProfile: profile,
      clientMutationId: mutationId("open"),
    }));
    if (!result) return;
    if (result.succeeded && result.data) replaceSession(taskKey, result.data, true);
    else setError(result.error?.safeMessage ?? "无法打开终端。");
  }, [commands, replaceSession, runMutation, taskKey]);

  const select = useCallback((sessionId: string) => {
    if (!taskKey) return;
    setTasks((current) => {
      const currentTask = current[taskKey] ?? emptyTask;
      if (!currentTask.sessions.some((session) => session.sessionId === sessionId)) return current;
      return { ...current, [taskKey]: { ...currentTask, activeSessionId: sessionId } };
    });
  }, [taskKey]);

  const input = useCallback((sessionId: string, data: string) => {
    if (!commands || !data) return;
    inputBuffers.current.set(sessionId, (inputBuffers.current.get(sessionId) ?? "") + data);
    const existing = inputTimers.current.get(sessionId);
    if (existing) clearTimeout(existing);
    inputTimers.current.set(sessionId, setTimeout(() => {
      inputTimers.current.delete(sessionId);
      const text = inputBuffers.current.get(sessionId) ?? "";
      inputBuffers.current.delete(sessionId);
      if (!text) return;
      void commands.inputTerminal({ sessionId, text, clientMutationId: mutationId("input") }).then((result) => {
        const owner = Object.keys(tasksRef.current).find((key) => tasksRef.current[key]?.sessions.some((session) => session.sessionId === sessionId));
        if (owner && result.succeeded && result.data) replaceSession(owner, result.data);
        else if (!result.succeeded) setError(result.error?.safeMessage ?? "终端输入失败。");
      }).catch(() => setError("终端输入失败。"));
    }, 12));
  }, [commands, replaceSession]);

  const resize = useCallback((sessionId: string, dimensions: TerminalDimensions) => {
    if (!commands) return;
    const digest = `${dimensions.cols}:${dimensions.rows}`;
    if (lastResize.current.get(sessionId) === digest) return;
    lastResize.current.set(sessionId, digest);
    const existing = resizeTimers.current.get(sessionId);
    if (existing) clearTimeout(existing);
    resizeTimers.current.set(sessionId, setTimeout(() => {
      resizeTimers.current.delete(sessionId);
      void commands.resizeTerminal({
        sessionId,
        cols: dimensions.cols,
        rows: dimensions.rows,
        clientMutationId: mutationId("resize"),
      }).catch(() => setError("终端尺寸同步失败。"));
    }, 100));
  }, [commands]);

  const interrupt = useCallback(async (sessionId: string) => {
    if (!commands) return;
    const owner = Object.keys(tasksRef.current).find((key) => tasksRef.current[key]?.sessions.some((session) => session.sessionId === sessionId));
    if (!owner) return;
    const result = await runMutation(`interrupt:${sessionId}`, () => commands.cancelTerminal({ sessionId, clientMutationId: mutationId("interrupt") }));
    if (result) {
      if (result.succeeded && result.data) replaceSession(owner, result.data);
      else setError(result.error?.safeMessage ?? "无法中断终端进程。");
    }
  }, [commands, replaceSession, runMutation]);

  const close = useCallback(async (sessionId: string) => {
    if (!commands) return;
    const owner = Object.keys(tasksRef.current).find((key) => tasksRef.current[key]?.sessions.some((session) => session.sessionId === sessionId));
    if (!owner) return;
    const result = await runMutation(`close:${sessionId}`, () => commands.closeTerminal({ sessionId, clientMutationId: mutationId("close") }));
    if (result) {
      if (!result.succeeded) setError(result.error?.safeMessage ?? "无法关闭终端会话。");
      else setTasks((current) => {
        const currentTask = current[owner] ?? emptyTask;
        const sessions = currentTask.sessions.filter((session) => session.sessionId !== sessionId);
        return { ...current, [owner]: { sessions, activeSessionId: sessions[0]?.sessionId ?? null } };
      });
    }
  }, [commands, runMutation]);

  return useMemo(() => ({
    sessions: task.sessions,
    activeSession,
    busy: busyKeys.size > 0,
    error,
    profiles,
    canOpen: Boolean(commands && taskKey),
    open,
    select,
    input,
    resize,
    interrupt,
    close,
    reconnect: refresh,
  }), [activeSession, busyKeys.size, close, commands, error, input, interrupt, open, profiles, refresh, resize, select, task.sessions, taskKey]);
}

function mutationId(kind: string) {
  return `terminal-${kind}-${crypto.randomUUID()}`;
}
