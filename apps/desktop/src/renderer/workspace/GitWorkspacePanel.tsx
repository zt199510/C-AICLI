import { useState } from "react";
import type { ChangesData } from "../../generated/desktop-contracts";
import type { ReviewCommands } from "../ReviewInspector";

export function GitWorkspacePanel({ mode, changes, commands, threadId }: {
  readonly mode: "branch" | "worktrees" | "compare" | "pull-request" | "git-actions";
  readonly changes: ChangesData | null;
  readonly commands: ReviewCommands;
  readonly threadId: string | null;
}) {
  const [branch, setBranch] = useState("");
  const [base, setBase] = useState(changes?.compareBase ?? "main");
  const [title, setTitle] = useState("");
  const [body, setBody] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  if (!changes?.workspaceId || !changes.repositoryId || !changes.revision) return <p>Refresh Changes to load authoritative Git identity.</p>;

  async function act(action: Parameters<ReviewCommands["mutateChanges"]>[0]["action"], extra: Partial<Parameters<ReviewCommands["mutateChanges"]>[0]> = {}) {
    setBusy(true); setMessage(null);
    try {
      const result = await commands.mutateChanges({
        workspaceId: changes!.workspaceId!, repositoryId: changes!.repositoryId!, expectedRevision: changes!.revision!,
        action, path: null, area: null, hunkId: null, message: null, setUpstream: false, confirmed: true,
        clientMutationId: `git-${action}-${Date.now()}-${crypto.randomUUID()}`, ...extra,
      });
      setMessage(result);
    } catch (error) { setMessage(error instanceof Error ? error.message : "Git action failed safely."); }
    finally { setBusy(false); }
  }

  if (mode === "branch") return <div className="git-workspace-view review-section"><h2>Branches</h2>
    <p>Current: <strong>{changes.branch ?? "detached HEAD"}</strong></p>
    <label>Branch name<input aria-label="Branch name" value={branch} onChange={(event) => setBranch(event.target.value)} placeholder="caicli/my-task" /></label>
    <div className="review-actions"><button disabled={busy || !branch} onClick={() => void act("create-branch", { targetBranch: branch })}>Create branch</button><button disabled={busy || !branch || changes.dirty} onClick={() => void act("switch-branch", { targetBranch: branch })}>Switch branch</button></div>
    <div className="branch-list">{changes.branches?.map((item) => <button key={item} disabled={busy || item === changes.branch || changes.dirty} onClick={() => void act("switch-branch", { targetBranch: item })}>{item}</button>)}</div>{status(message)}</div>;

  if (mode === "compare") return <div className="git-workspace-view review-section"><h2>Compare branches</h2>
    <dl><dt>Base</dt><dd>{changes.compareBase ?? "No default base"}</dd><dt>Head</dt><dd>{changes.branch ?? changes.head}</dd></dl>
    <pre className="changes-diff" tabIndex={0}>{changes.compareDiff || "No differences from the detected base."}</pre></div>;

  if (mode === "worktrees") return <div className="git-workspace-view review-section"><h2>Managed Worktrees</h2>
    <p>Only Worktrees created here are manageable. Creation uses an external app-owned directory and a <code>caicli/</code> branch.</p>
    <label>Managed branch<input aria-label="Managed Worktree branch" value={branch} onChange={(event) => setBranch(event.target.value)} placeholder="caicli/task-name" /></label>
    <button disabled={busy || !threadId || !branch.startsWith("caicli/")} onClick={() => void act("create-worktree", { targetBranch: branch, threadId })}>Review and create for current task</button>
    {changes.worktrees?.map((worktree) => <article key={worktree.worktreeId}><strong>{worktree.branch}</strong><span className="plain-path">{worktree.path}</span><small>{worktree.dirty ? "dirty" : "clean"} · {worktree.merged ? "merged" : "unmerged"}</small><button disabled={busy || worktree.dirty || !worktree.merged} onClick={() => void act("remove-worktree", { worktreeId: worktree.worktreeId, baseBranch: base })}>Confirm remove Worktree</button></article>)}{status(message)}</div>;

  if (mode === "pull-request") return <div className="git-workspace-view review-section"><h2>Pull request</h2>
    {!changes.ghAvailable ? <p>GitHub CLI is not installed or not on PATH. Sign-in and credentials stay with <code>gh</code>; C-AICLI never stores a PAT.</p> : <>
      <label>Base branch<input value={base} onChange={(event) => setBase(event.target.value)} /></label><label>Title<input value={title} onChange={(event) => setTitle(event.target.value)} /></label><label>Body<textarea value={body} onChange={(event) => setBody(event.target.value)} /></label>
      <div className="review-actions"><button disabled={busy || !title} onClick={() => void act("create-pr", { baseBranch: base, title, body })}>Confirm create Draft PR</button><button disabled={busy || !title} onClick={() => void act("update-pr", { title, body })}>Confirm update</button><button disabled={busy} onClick={() => void act("open-pr")}>Open in browser</button></div></>}{status(message)}</div>;

  return <div className="git-workspace-view review-section"><h2>Commit or push</h2><p>Commit and Push are separately previewed and confirmed in Changes. Commit never stages implicitly; hooks stay enabled; force push is unavailable.</p></div>;
}

function status(value: string | null) { return value ? <p role="status" aria-live="polite">{value}</p> : null; }
