import { useEffect, useRef, useState } from "react";
import type { TerminalStateData } from "../generated/desktop-contracts";

export function TerminalPanel({ workspaceReady }: { workspaceReady: boolean }) {
  const bridge = typeof window === "undefined" ? undefined : window.caicli;
  const [expanded, setExpanded] = useState(false);
  const [terminal, setTerminal] = useState<TerminalStateData | null>(null);
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const outputRef = useRef<HTMLPreElement>(null);

  useEffect(() => {
    if (!expanded || !terminal || terminal.status !== "running" || !bridge) return;
    const timer = window.setInterval(() => {
      void bridge.getTerminal({ sessionId: terminal.sessionId, afterCursor: 0 }).then((result) => {
        if (result.succeeded && result.data) setTerminal(result.data);
      }).catch(() => setError("Terminal status could not be refreshed."));
    }, 500);
    return () => window.clearInterval(timer);
  }, [bridge, expanded, terminal?.sessionId, terminal?.status]);

  async function open() {
    if (!bridge) return;
    setBusy(true); setError(null);
    try {
      const result = await bridge.openTerminal({ shellProfile: "system-default", clientMutationId: mutation("open") });
      if (!result.succeeded || !result.data) throw new Error(result.error?.safeMessage ?? "Terminal could not be opened.");
      setTerminal(result.data); setExpanded(true);
    } catch (value) { setError(value instanceof Error ? value.message : "Terminal could not be opened."); }
    finally { setBusy(false); }
  }

  async function send() {
    if (!bridge || !terminal || !input) return;
    setBusy(true); setError(null);
    try {
      const result = await bridge.inputTerminal({ sessionId: terminal.sessionId, text: input + "\n", clientMutationId: mutation("input") });
      if (!result.succeeded || !result.data) throw new Error(result.error?.safeMessage ?? "Terminal input failed.");
      setTerminal(result.data); setInput("");
    } catch (value) { setError(value instanceof Error ? value.message : "Terminal input failed."); }
    finally { setBusy(false); }
  }

  async function stop(close: boolean) {
    if (!bridge || !terminal) return;
    setBusy(true); setError(null);
    try {
      const command = { sessionId: terminal.sessionId, clientMutationId: mutation(close ? "close" : "cancel") };
      const result = close ? await bridge.closeTerminal(command) : await bridge.cancelTerminal(command);
      if (!result.succeeded || !result.data) throw new Error(result.error?.safeMessage ?? "Terminal action failed.");
      if (close) {
        setTerminal(null);
        setInput("");
        setExpanded(false);
      } else {
        setTerminal(result.data);
      }
    } catch (value) { setError(value instanceof Error ? value.message : "Terminal action failed."); }
    finally { setBusy(false); }
  }

  async function copySelection() {
    const selected = document.getSelection()?.toString() ?? "";
    if (selected && outputRef.current?.contains(document.getSelection()?.anchorNode ?? null)) {
      await navigator.clipboard.writeText(selected);
    }
  }

  return <section className={`terminal-panel ${expanded ? "expanded" : ""}`} aria-label="User terminal">
    <div className="terminal-heading">
      <strong>User terminal</strong>
      <span role="status" aria-live="polite">{terminal ? `${terminal.status}${terminal.exitCode === null ? "" : ` · exit ${terminal.exitCode}`}${terminal.truncated ? " · output truncated" : ""}` : "Closed"}</span>
      <div>
        <button type="button" hidden={!expanded} onClick={() => setExpanded(false)}>Collapse</button>
        <button type="button" hidden={Boolean(terminal && terminal.status !== "closed")} disabled={!workspaceReady || busy} onClick={() => void open()}>Open terminal</button>
        <button type="button" hidden={terminal?.status !== "running"} disabled={busy} onClick={() => void stop(false)}>Cancel process</button>
        <button type="button" hidden={!terminal || terminal.status === "closed"} disabled={busy} onClick={() => void stop(true)}>Close terminal</button>
      </div>
    </div>
    <div className="inline-error" role="alert" hidden={!error}>{error ?? ""}</div>
    <div className="terminal-session" hidden={!expanded || !terminal}>
      <pre ref={outputRef} className="terminal-output" tabIndex={0}>{terminal?.truncated ? "[earlier output truncated]\n" : ""}{terminal?.output ?? ""}</pre>
      <div className="terminal-input-row">
        <input aria-label="Terminal input" value={input} maxLength={8192} disabled={busy || terminal?.status !== "running"} onChange={(event) => setInput(event.target.value)} onKeyDown={(event) => { if (event.key === "Enter") { event.preventDefault(); void send(); } }} />
        <button type="button" disabled={busy || terminal?.status !== "running" || !input} onClick={() => void send()}>Send</button>
        <button type="button" onClick={() => void copySelection()}>Copy selection</button>
      </div>
    </div>
  </section>;
}

function mutation(kind: string): string {
  return `terminal-${kind}-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}
