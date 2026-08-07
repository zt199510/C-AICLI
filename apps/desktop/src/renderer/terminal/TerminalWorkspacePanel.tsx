import { Plus, X } from "lucide-react";
import { useRef } from "react";
import { PtyTerminalViewport } from "./PtyTerminalViewport";
import { TerminalSessionTabs } from "./TerminalSessionTabs";
import { TerminalToolbar } from "./TerminalToolbar";
import type { XtermAdapter } from "./XtermAdapter";
import type { TerminalSessionsController } from "./useTerminalSessions";
import type { TerminalProfileId } from "../../shared/bridge-contract";

export function TerminalWorkspacePanel({
  controller,
  active,
  onShareSelection,
  onClosePanel,
  defaultShell,
  chrome = "dock",
}: {
  readonly controller: TerminalSessionsController;
  readonly active: boolean;
  readonly onShareSelection?: (text: string) => void;
  readonly onClosePanel?: () => void;
  readonly defaultShell?: TerminalProfileId;
  readonly chrome?: "dock" | "embedded";
}) {
  const adapterRef = useRef<XtermAdapter | null>(null);
  const session = controller.activeSession;

  const embeddedSessionTabs = chrome === "embedded" && controller.sessions.length > 1;

  return <section className="terminal-workspace-panel" aria-label="User terminal" data-active={active} data-chrome={chrome} data-has-session-tabs={embeddedSessionTabs}>
    {chrome === "dock" ? <div className="terminal-panel-tabbar">
      <TerminalSessionTabs sessions={controller.sessions} activeSessionId={session?.sessionId ?? null} onSelect={controller.select} />
      <button className="terminal-panel-new icon-button" type="button" aria-label="新建终端" disabled={!controller.canOpen || controller.busy} onClick={() => void controller.open(defaultShell ?? "system-default")}><Plus size={16} aria-hidden="true" /></button>
      {onClosePanel ? <button className="terminal-panel-close icon-button" type="button" aria-label="关闭底部面板" onClick={onClosePanel}><X size={15} aria-hidden="true" /></button> : null}
    </div> : null}
    {embeddedSessionTabs ? <TerminalSessionTabs sessions={controller.sessions} activeSessionId={session?.sessionId ?? null} onSelect={controller.select} /> : null}
    <TerminalToolbar
      session={session}
      profiles={controller.profiles}
      canOpen={controller.canOpen}
      busy={controller.busy}
      adapter={adapterRef.current}
      defaultProfile={defaultShell}
      showOpenButton={chrome === "embedded"}
      onOpen={(profile) => void controller.open(profile)}
      onInterrupt={() => { if (session) void controller.interrupt(session.sessionId); }}
      onReconnect={() => { if (session) void controller.reconnect(session.sessionId); }}
      onClose={() => { if (session) void controller.close(session.sessionId); }}
      onShareSelection={onShareSelection}
    />
    {session ? <div
      id={`terminal-session-${session.sessionId}`}
      className="terminal-session-host"
      role={chrome === "dock" || embeddedSessionTabs ? "tabpanel" : undefined}
      aria-labelledby={chrome === "dock" || embeddedSessionTabs ? `terminal-tab-${session.sessionId}` : undefined}
    >
      <PtyTerminalViewport
        output={session.output}
        active={active}
        readOnly={session.status !== "running"}
        adapterRef={adapterRef}
        onData={(data) => controller.input(session.sessionId, data)}
        onResize={(dimensions) => controller.resize(session.sessionId, dimensions)}
      />
      <div className="terminal-session-status" role="status" aria-live="polite">
        <span>{session.status}</span>
        <span>{session.exitCode === null ? "" : `exit ${session.exitCode}`}</span>
        <span>{session.truncated ? "较早输出已截断" : ""}</span>
      </div>
    </div> : <div className="terminal-empty-state">
      <strong>没有终端会话</strong>
      <span>选择 shell 后新建；切换工具或隐藏底部终端不会结束会话。</span>
    </div>}
    {controller.error ? <div className="terminal-error" role="alert">{controller.error}</div> : null}
  </section>;
}
