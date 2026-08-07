import { useCallback, useEffect, useState } from "react";
import type { SubagentData } from "../../generated/desktop-contracts";
import type { DesktopBridge } from "../../shared/bridge-contract";

export function SubagentWorkspacePanel({ bridge, threadId, active }: {
  readonly bridge?: DesktopBridge;
  readonly threadId: string | null;
  readonly active: boolean;
}) {
  const [agents, setAgents] = useState<readonly SubagentData[]>([]);
  const [prompt, setPrompt] = useState("");
  const [mode, setMode] = useState<"read-only" | "write">("read-only");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    if (!bridge || !threadId) { setAgents([]); return; }
    try {
      const result = await bridge.listSubagents({ parentThreadId: threadId });
      if (result.succeeded && result.data) { setAgents(result.data.agents); setError(null); }
      else setError(result.error?.safeMessage ?? "Sub-agent activity could not be loaded.");
    } catch { setError("Sub-agent activity could not be loaded."); }
  }, [bridge, threadId]);

  useEffect(() => {
    if (!active) return;
    void refresh();
    const timer = window.setInterval(() => void refresh(), 1500);
    return () => window.clearInterval(timer);
  }, [active, refresh]);

  const start = async () => {
    if (!bridge || !threadId || !prompt.trim() || busy) return;
    setBusy(true);
    try {
      const result = await bridge.startSubagent({
        parentThreadId: threadId,
        prompt: prompt.trim(),
        mode,
        confirmed: true,
        clientMutationId: `subagent-start-${crypto.randomUUID()}`,
      });
      if (result.succeeded && result.data) { setAgents(result.data.agents); setPrompt(""); setError(null); }
      else setError(result.error?.safeMessage ?? "Sub-agent could not be started.");
    } catch { setError("Sub-agent could not be started."); }
    finally { setBusy(false); }
  };

  const mutate = async (agent: SubagentData, action: "cancel" | "takeover") => {
    if (!bridge || busy) return;
    setBusy(true);
    const command = { agentId: agent.agentId, confirmed: true, clientMutationId: `subagent-${action}-${crypto.randomUUID()}` };
    try {
      const result = action === "cancel" ? await bridge.cancelSubagent(command) : await bridge.takeoverSubagent(command);
      if (result.succeeded && result.data) { setAgents(result.data.agents); setError(null); }
      else setError(result.error?.safeMessage ?? "Sub-agent action failed.");
    } catch { setError("Sub-agent action failed."); }
    finally { setBusy(false); }
  };

  const approval = async (agent: SubagentData, decision: "approve" | "deny") => {
    if (!bridge || busy) return;
    setBusy(true);
    try {
      const result = await bridge.resolveSubagentApproval({ agentId: agent.agentId, decision, clientMutationId: `subagent-approval-${crypto.randomUUID()}` });
      if (result.succeeded && result.data) { setAgents(result.data.agents); setError(null); }
      else setError(result.error?.safeMessage ?? "Sub-agent approval is stale.");
    } catch { setError("Sub-agent approval is stale."); }
    finally { setBusy(false); }
  };

  if (!threadId) return <div className="review-section"><p>Select a task before creating a Sub-agent.</p></div>;
  return <div className="review-section subagent-panel">
    <header><h2>Sub-agent activity</h2><span>{agents.length}/3</span></header>
    <p className="subagent-note">Agents only start from this form. Write agents receive an isolated managed Worktree.</p>
    <textarea aria-label="Sub-agent task" value={prompt} onChange={(event) => setPrompt(event.target.value)} placeholder="Describe a bounded task…" rows={3} />
    <div className="subagent-create-row">
      <select aria-label="Sub-agent mode" value={mode} onChange={(event) => setMode(event.target.value as "read-only" | "write")}>
        <option value="read-only">Read-only</option><option value="write">Write in Worktree</option>
      </select>
      <button type="button" className="primary-action" disabled={busy || !prompt.trim() || agents.filter((agent) => ["starting", "running", "waiting-approval"].includes(agent.status)).length >= 3} onClick={() => void start()}>Create Sub-agent</button>
    </div>
    {error ? <p role="alert">{error}</p> : null}
    <div className="subagent-list">
      {agents.length === 0 ? <p>No Sub-agents for this task.</p> : agents.map((agent) => <article key={agent.agentId} className="subagent-card">
        <header><strong>{agent.mode === "write" ? "Write agent" : "Read-only agent"}</strong><span>{agent.status}</span></header>
        {agent.branch ? <code>{agent.branch}</code> : null}
        {agent.resultSummary ? <p>{agent.resultSummary}</p> : null}
        {agent.approval ? <div className="subagent-approval"><p>{agent.approval.safeSummary}</p><button type="button" disabled={busy} onClick={() => void approval(agent, "approve")}>Approve</button><button type="button" disabled={busy} onClick={() => void approval(agent, "deny")}>Deny</button></div> : null}
        {(["starting", "running", "waiting-approval"].includes(agent.status)) ? <div className="subagent-actions"><button type="button" disabled={busy} onClick={() => void mutate(agent, "cancel")}>Cancel</button>{agent.mode === "write" ? <button type="button" disabled={busy} onClick={() => void mutate(agent, "takeover")}>Take over Worktree</button> : null}</div> : null}
      </article>)}
    </div>
  </div>;
}
