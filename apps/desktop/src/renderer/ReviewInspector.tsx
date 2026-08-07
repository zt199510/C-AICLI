import { useRef, useState, type KeyboardEvent } from "react";
import type { ArtifactMetadataData, GerberReviewData } from "../generated/desktop-contracts";
import type { ReviewState } from "./desktop-state";
import type { DesktopBridge } from "../shared/bridge-contract";

const tabs = ["changes", "reports", "artifacts", "preview"] as const;

export interface ReviewCommands {
  mutateChanges(command: Parameters<DesktopBridge["mutateChanges"]>[0]): Promise<string>;
  previewArtifact(artifactId: string): Promise<string>;
  verifyArtifact(artifactId: string): Promise<string>;
  exportArtifact(artifactId: string): Promise<string>;
  loadGerber(runId: string, preview: boolean): Promise<GerberReviewData>;
  decideGerber(runId: string, expectedRevision: number, reason: string, accept: boolean): Promise<GerberReviewData>;
}

export interface ReviewInspectorProps {
  review: ReviewState;
  workspaceReady: boolean;
  onTab(tab: ReviewState["activeTab"]): void;
  onReport(reportId: string): void;
  onArtifact(artifactId: string): void;
  commands?: ReviewCommands;
}

export function ReviewInspector({ review, workspaceReady, onTab, onReport, onArtifact, commands }: ReviewInspectorProps) {
  const tabRefs = useRef<Array<HTMLButtonElement | null>>([]);
  function navigateTabs(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    let next: number;
    if (event.key === "ArrowRight" || event.key === "ArrowDown") next = (index + 1) % tabs.length;
    else if (event.key === "ArrowLeft" || event.key === "ArrowUp") next = (index - 1 + tabs.length) % tabs.length;
    else if (event.key === "Home") next = 0;
    else if (event.key === "End") next = tabs.length - 1;
    else return;
    event.preventDefault();
    onTab(tabs[next]!);
    tabRefs.current[next]?.focus();
  }
  return (
    <div className="review-inspector">
      <div className="review-tabs" role="tablist" aria-label="Read-only review panels">
        {tabs.map((tab, index) => <button id={`review-tab-${tab}`} aria-controls={`review-panel-${tab}`} ref={(value) => { tabRefs.current[index] = value; }} tabIndex={review.activeTab === tab ? 0 : -1} key={tab} type="button" role="tab" aria-selected={review.activeTab === tab} onKeyDown={(event) => navigateTabs(event, index)} onClick={() => onTab(tab)}>{title(tab)}</button>)}
      </div>
      <div id={`review-panel-${review.activeTab}`} aria-labelledby={`review-tab-${review.activeTab}`} className="review-content" role="tabpanel" tabIndex={0}>
        <ReviewPanel review={review} workspaceReady={workspaceReady} onReport={onReport} onArtifact={onArtifact} commands={commands} />
      </div>
    </div>
  );
}

export function ReviewPanel({
  review,
  workspaceReady,
  onReport,
  onArtifact,
  commands,
}: Omit<ReviewInspectorProps, "onTab">) {
  return <>
    {!workspaceReady ? <div className="empty-list">Open a workspace to review results.</div> : null}
    {workspaceReady && review.status === "loading" ? <div className="empty-list">Loading review data…</div> : null}
    {workspaceReady && review.status === "error" ? <div className="inline-error" role="alert">{review.error}</div> : null}
    {workspaceReady && review.truncated ? <div className="capped-banner">Showing a bounded result set.</div> : null}
    {workspaceReady && review.activeTab === "changes" ? <ChangesPanel review={review} commands={commands} /> : null}
    {workspaceReady && review.activeTab === "reports" ? <ReportsPanel review={review} onReport={onReport} /> : null}
    {workspaceReady && review.activeTab === "artifacts" ? <ArtifactsPanel review={review} onArtifact={onArtifact} commands={commands} /> : null}
    {workspaceReady && review.activeTab === "preview" ? <GerberPanel artifacts={review.artifacts} selected={review.selectedArtifact} onArtifact={onArtifact} commands={commands} /> : null}
  </>;
}

function ChangesPanel({ review, commands }: { review: ReviewState; commands?: ReviewCommands }) {
  const value = review.changes;
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [confirmation, setConfirmation] = useState<{ action: "revert" | "commit" | "push"; path?: string; area?: string; hunkId?: string } | null>(null);
  const [commitMessage, setCommitMessage] = useState("");
  const [setUpstream, setSetUpstream] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  if (!value && review.status === "ready") return <div className="empty-list">No change summary is available.</div>;
  if (!value) return null;
  const snapshot = value;
  const files = value.files ?? [];
  const selected = files.find((file) => `${file.area}:${file.path}` === selectedKey) ?? files[0] ?? null;
  const writable = Boolean(commands && value.workspaceId && value.repositoryId && value.revision);

  async function mutate(action: "stage" | "unstage" | "revert" | "commit" | "push", target?: { path?: string; area?: string; hunkId?: string }, confirmed = false) {
    if (!commands || !snapshot.workspaceId || !snapshot.repositoryId || !snapshot.revision) return;
    setBusy(true); setMessage(null);
    try {
      const result = await commands.mutateChanges({
        workspaceId: snapshot.workspaceId,
        repositoryId: snapshot.repositoryId,
        expectedRevision: snapshot.revision,
        action,
        path: target?.path ?? null,
        area: target?.area ?? null,
        hunkId: target?.hunkId ?? null,
        message: action === "commit" ? commitMessage : null,
        setUpstream: action === "push" && setUpstream,
        confirmed,
        clientMutationId: `changes-${action}-${Date.now()}-${crypto.randomUUID()}`,
      });
      setMessage(result);
      setConfirmation(null);
      if (action === "commit") setCommitMessage("");
    } catch (error) { setMessage(error instanceof Error ? error.message : "Git action failed safely."); }
    finally { setBusy(false); }
  }

  function requestRevert(target: { path: string; area: string; hunkId?: string }) {
    setConfirmation({ action: "revert", ...target });
  }

  return <div className="changes-workspace-panel review-section" aria-label="Changes">
    <div className="changes-summary-bar">
      <div><h2>Changes</h2><p>{value.diffStatSummary || "No changes"}</p></div>
      <div className="changes-branch-meta"><span>{value.branch ?? "detached HEAD"}</span><code>{value.head?.slice(0, 8) ?? "unknown"}</code></div>
    </div>
    {!writable ? <p className="capped-banner">当前投影是旧版只读 Changes；刷新真实工作区以启用 Git 操作。</p> : null}
    <div className="changes-review-layout">
      <nav className="changes-file-list" aria-label="Changed files">
        {(["conflicted", "staged", "unstaged", "untracked"] as const).map((area) => {
          const areaFiles = files.filter((file) => file.area === area);
          if (!areaFiles.length) return null;
          return <section key={area} aria-labelledby={`changes-area-${area}`}><h3 id={`changes-area-${area}`}>{area} <span>{areaFiles.length}</span></h3>
            {areaFiles.map((file) => <button type="button" key={`${area}:${file.path}`} aria-current={selected === file ? "page" : undefined} onClick={() => setSelectedKey(`${area}:${file.path}`)}>
              <span className="status-chip">{file.status}</span><span className="plain-path">{file.path}</span>
            </button>)}
          </section>;
        })}
        {!files.length ? <p>No changed files.</p> : null}
      </nav>
      <section className="changes-diff-viewer" aria-label="Diff viewer">
        {selected ? <>
          <header><div><strong className="plain-path">{selected.path}</strong><span>{selected.area}</span></div>
            <div className="diff-action-bar">
              {selected.area !== "staged" ? <button type="button" disabled={!writable || busy} onClick={() => void mutate("stage", selected)}>Stage file</button> : null}
              {selected.area === "staged" ? <button type="button" disabled={!writable || busy} onClick={() => void mutate("unstage", selected)}>Unstage file</button> : null}
              {selected.area !== "untracked" ? <button type="button" className="danger" disabled={!writable || busy} onClick={() => requestRevert(selected)}>Revert file</button> : null}
            </div>
          </header>
          <pre className="changes-diff" tabIndex={0}>{selected.diff || "No textual diff is available."}</pre>
          {selected.hunks.map((hunk) => <article className="diff-hunk" key={hunk.hunkId}>
            <header><code>{hunk.header}</code><div>
              {selected.area === "unstaged" ? <button type="button" disabled={!writable || busy} onClick={() => void mutate("stage", { ...selected, hunkId: hunk.hunkId })}>Stage hunk</button> : null}
              {selected.area === "staged" ? <button type="button" disabled={!writable || busy} onClick={() => void mutate("unstage", { ...selected, hunkId: hunk.hunkId })}>Unstage hunk</button> : null}
              {selected.area === "unstaged" ? <button type="button" className="danger" disabled={!writable || busy} onClick={() => requestRevert({ ...selected, hunkId: hunk.hunkId })}>Revert hunk</button> : null}
            </div></header>
          </article>)}
        </> : <p>Select a changed file to review its diff.</p>}
      </section>
    </div>
    <section className="git-action-bar" aria-label="Commit and push">
      <label><span>Commit message</span><textarea value={commitMessage} maxLength={8192} onChange={(event) => setCommitMessage(event.target.value)} placeholder="Describe the staged changes" /></label>
      <div><button type="button" disabled={!writable || busy || !commitMessage.trim() || !files.some((file) => file.area === "staged")} onClick={() => setConfirmation({ action: "commit" })}>Review commit</button>
        <button type="button" disabled={!writable || busy || value.head === "0000000000000000000000000000000000000000"} onClick={() => setConfirmation({ action: "push" })}>Review push</button></div>
    </section>
    {confirmation ? <section className="git-confirmation" role="alertdialog" aria-modal="false" aria-label={`Confirm ${confirmation.action}`}>
      <h3>Confirm {confirmation.action}</h3>
      {confirmation.action === "revert" ? <p>This permanently discards the selected tracked {confirmation.hunkId ? "hunk" : "file"}. Untracked files cannot be deleted here.</p> : null}
      {confirmation.action === "commit" ? <><p>Commit staged changes only. Hooks remain enabled; amend and no-verify are unavailable.</p><pre>{commitMessage}</pre></> : null}
      {confirmation.action === "push" ? <><dl><dt>Remote</dt><dd>{value.remote ?? "Not configured"}</dd><dt>Branch</dt><dd>{value.branch ?? "Detached"}</dd><dt>Upstream</dt><dd>{value.upstream ?? "Not set"}</dd></dl>
        {!value.upstream ? <label><input type="checkbox" checked={setUpstream} onChange={(event) => setSetUpstream(event.target.checked)} /> Set upstream on first push</label> : null}<p>Force push is permanently unavailable.</p></> : null}
      <div><button type="button" onClick={() => setConfirmation(null)}>Cancel</button><button type="button" className={confirmation.action === "revert" ? "danger" : ""} disabled={busy || (confirmation.action === "push" && !value.upstream && !setUpstream)} onClick={() => void mutate(confirmation.action, confirmation, true)}>Confirm {confirmation.action}</button></div>
    </section> : null}
    {message ? <p className="review-status" role="status" aria-live="polite">{message}</p> : null}
    {value.warnings.length ? <div className="warning-list">{value.warnings.map((warning) => <p key={warning}>{warning}</p>)}</div> : null}
  </div>;
}

function ReportsPanel({ review, onReport }: { review: ReviewState; onReport(id: string): void }) {
  return <div className="review-section">
    <h2>Reports</h2>
    {review.reports.length ? <div className="review-list">{review.reports.map((report) => <button type="button" key={report.reportId} onClick={() => onReport(report.reportId)}><span>{report.sourceKind}: {report.sourceId}</span><span>{report.status}</span></button>)}</div> : review.status === "ready" ? <p>No reports.</p> : null}
    {review.selectedReport ? <div className="review-detail"><h3>Structured report</h3><p>{review.selectedReport.summary ?? "No summary."}</p><TextList title="Changed files" values={review.selectedReport.changedFiles} /><TextList title="Commands" values={review.selectedReport.commands} /><TextList title="Verification" values={review.selectedReport.verification} /><TextList title="Risks" values={review.selectedReport.risks} /><TextList title="Artifact pointers" values={review.selectedReport.artifactPointers} /></div> : null}
  </div>;
}

function ArtifactsPanel({ review, onArtifact, commands }: { review: ReviewState; onArtifact(id: string): void; commands?: ReviewCommands }) {
  return <div className="review-section"><h2>Artifacts</h2>
    {review.artifacts.length ? <div className="review-list">{review.artifacts.map((artifact) => <button type="button" key={artifact.artifactId} onClick={() => onArtifact(artifact.artifactId)}><span>{artifact.kind}</span><span>{artifact.availability}</span></button>)}</div> : review.status === "ready" ? <p>No artifacts.</p> : null}
    {review.selectedArtifact ? <><ArtifactDetail artifact={review.selectedArtifact} /><ArtifactActions artifact={review.selectedArtifact} commands={commands} /></> : null}
  </div>;
}

function GerberPanel({ artifacts, selected, onArtifact, commands }: { artifacts: readonly ArtifactMetadataData[]; selected: ArtifactMetadataData | null; onArtifact(id: string): void; commands?: ReviewCommands }) {
  const managed = artifacts.filter(isManagedPreview);
  const [review, setReview] = useState<GerberReviewData | null>(null);
  const [reason, setReason] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const runId = selected?.owner.runId ?? managed[0]?.owner.runId;

  async function load(preview: boolean) {
    if (!runId || !commands) return;
    try {
      const result = await commands.loadGerber(runId, preview);
      setReview(result);
      setMessage(preview ? "Managed preview metadata refreshed; correctness remains unproven." : null);
    } catch (value) { setMessage(value instanceof Error ? value.message : "Review unavailable."); }
  }

  async function decide(accept: boolean) {
    if (!runId || !review || !commands) return;
    try {
      const result = await commands.decideGerber(runId, review.revision, reason || (accept ? "Reviewed in Desktop" : ""), accept);
      setReview(result);
      setMessage(`Decision recorded: ${result.decision ?? result.state}.`);
    } catch (value) { setMessage(value instanceof Error ? value.message : "Decision failed closed."); }
  }

  return <div className="review-section"><h2>Managed preview metadata</h2>
    <p className="disclaimer">Metadata and verification status do not prove manufacturing or image correctness. No file bytes are opened or rendered here.</p>
    {managed.length ? <div className="review-list">{managed.map((artifact) => <button type="button" key={artifact.artifactId} onClick={() => onArtifact(artifact.artifactId)}><span>{artifact.kind}</span><span>{artifact.verification}</span></button>)}</div> : <p>No managed Gerber/TIFF preview artifacts.</p>}
    {selected && isManagedPreview(selected) ? <ArtifactDetail artifact={selected} /> : null}
    {runId ? <div className="review-actions"><button type="button" disabled={!commands} onClick={() => void load(false)}>Load verification</button><button type="button" disabled={!commands} onClick={() => void load(true)}>Refresh preview</button></div> : null}
    {review ? <>
      <dl><dt>Run state</dt><dd>{review.state}</dd><dt>Hard verification</dt><dd>{review.hardVerificationPassed ? "Passed" : "Not passed"}</dd><dt>Preview</dt><dd>{review.previewAvailable ? "Available" : "Missing"}</dd><dt>Correctness proof</dt><dd>No</dd></dl>
      <input className="decision-reason" aria-label="Human decision reason" maxLength={1024} value={reason} onChange={(event) => setReason(event.target.value)} placeholder="Reject reason (required)" />
      <div className="review-actions"><button type="button" disabled={!review.humanDecisionEligible} onClick={() => void decide(true)}>Accept verified run</button><button type="button" disabled={!review.humanDecisionEligible || !reason.trim()} onClick={() => void decide(false)}>Reject run</button></div>
      {review.disabledReason ? <p>{review.disabledReason}</p> : null}
    </> : null}
    {message ? <p className="review-status" role="status" aria-live="polite">{message}</p> : null}
  </div>;
}

function ArtifactActions({ artifact, commands }: { artifact: ArtifactMetadataData; commands?: ReviewCommands }) {
  const [message, setMessage] = useState<string | null>(null);
  async function act(kind: "preview" | "verify" | "export") {
    if (!commands) return;
    try {
      const result = kind === "preview"
        ? await commands.previewArtifact(artifact.artifactId)
        : kind === "verify"
          ? await commands.verifyArtifact(artifact.artifactId)
          : await commands.exportArtifact(artifact.artifactId);
      setMessage(result);
    } catch { setMessage("Artifact action failed safely."); }
  }
  return <><div className="review-actions"><button type="button" disabled={!commands} onClick={() => void act("verify")}>Verify identity</button><button type="button" disabled={!commands} onClick={() => void act("preview")}>Preview metadata</button><button type="button" disabled={!commands} onClick={() => void act("export")}>Export…</button></div>{message ? <p className="review-status" role="status" aria-live="polite">{message}</p> : null}</>;
}

function ArtifactDetail({ artifact }: { artifact: ArtifactMetadataData }) {
  return <dl className="artifact-detail">
    <dt>Kind</dt><dd>{artifact.kind}</dd><dt>Ownership</dt><dd>{artifact.ownership}</dd><dt>Availability</dt><dd>{artifact.availability}</dd>
    <dt>Verification</dt><dd>{artifact.verification}</dd><dt>Relative path</dt><dd className="plain-path">{artifact.relativePath}</dd>
    <dt>Size</dt><dd>{artifact.size ?? "Unknown"}</dd><dt>SHA-256</dt><dd className="plain-path">{artifact.sha256 ?? "Unavailable"}</dd>
    <dt>Retention</dt><dd>{artifact.retention.class} · {artifact.retention.owned ? "owned" : "external"}</dd>
  </dl>;
}

function TextList({ title: heading, values }: { title: string; values: readonly string[] }) {
  return <section><h4>{heading}</h4>{values.length ? <ul>{values.map((value, index) => <li key={`${index}:${value}`}>{value}</li>)}</ul> : <p>None.</p>}</section>;
}

function isManagedPreview(artifact: ArtifactMetadataData): boolean {
  const kind = artifact.kind.toLowerCase();
  return artifact.ownership.toLowerCase() === "managed" && (kind.includes("gerber") || kind.includes("tiff") || kind.includes("tif"));
}

function title(value: string): string { return value === "preview" ? "Gerber" : value[0]?.toUpperCase() + value.slice(1); }
