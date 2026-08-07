import type { TerminalStateData } from "../../generated/desktop-contracts";

export function TerminalSessionTabs({
  sessions,
  activeSessionId,
  onSelect,
}: {
  readonly sessions: readonly TerminalStateData[];
  readonly activeSessionId: string | null;
  readonly onSelect: (sessionId: string) => void;
}) {
  return <div className="terminal-session-tabs" role="tablist" aria-label="Terminal sessions">
    {sessions.map((session, index) => <button
      key={session.sessionId}
      type="button"
      id={`terminal-tab-${session.sessionId}`}
      role="tab"
      aria-selected={session.sessionId === activeSessionId}
      aria-controls={`terminal-session-${session.sessionId}`}
      tabIndex={session.sessionId === activeSessionId ? 0 : -1}
      onClick={() => onSelect(session.sessionId)}
    >
      <span>{session.shellProfile === "system-default" ? `Terminal ${index + 1}` : session.shellProfile}</span>
      <span className={`terminal-tab-status status-${session.status}`} aria-label={session.status} />
    </button>)}
  </div>;
}
