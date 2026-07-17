import type { ArtifactMetadataData } from "../generated/desktop-contracts";
import type { ReviewState } from "./desktop-state";

const tabs = ["changes", "reports", "artifacts", "preview"] as const;

export interface ReviewInspectorProps {
  review: ReviewState;
  workspaceReady: boolean;
  onTab(tab: ReviewState["activeTab"]): void;
  onReport(reportId: string): void;
  onArtifact(artifactId: string): void;
}

export function ReviewInspector({ review, workspaceReady, onTab, onReport, onArtifact }: ReviewInspectorProps) {
  return (
    <div className="review-inspector">
      <div className="review-tabs" role="tablist" aria-label="Read-only review panels">
        {tabs.map((tab) => <button key={tab} type="button" role="tab" aria-selected={review.activeTab === tab} onClick={() => onTab(tab)}>{title(tab)}</button>)}
      </div>
      <div className="review-content" role="tabpanel">
        {!workspaceReady ? <div className="empty-list">Open a workspace to review results.</div> : null}
        {workspaceReady && review.status === "loading" ? <div className="empty-list">Loading review data…</div> : null}
        {workspaceReady && review.status === "error" ? <div className="inline-error" role="alert">{review.error}</div> : null}
        {workspaceReady && review.truncated ? <div className="capped-banner">Showing a bounded result set.</div> : null}
        {workspaceReady && review.activeTab === "changes" ? <ChangesPanel review={review} /> : null}
        {workspaceReady && review.activeTab === "reports" ? <ReportsPanel review={review} onReport={onReport} /> : null}
        {workspaceReady && review.activeTab === "artifacts" ? <ArtifactsPanel review={review} onArtifact={onArtifact} /> : null}
        {workspaceReady && review.activeTab === "preview" ? <PreviewPanel artifacts={review.artifacts} selected={review.selectedArtifact} onArtifact={onArtifact} /> : null}
      </div>
    </div>
  );
}

function ChangesPanel({ review }: { review: ReviewState }) {
  const value = review.changes;
  if (!value && review.status === "ready") return <div className="empty-list">No change summary is available.</div>;
  if (!value) return null;
  return <div className="review-section">
    <h2>Workspace changes</h2>
    <dl><dt>Status</dt><dd>{value.status}</dd><dt>Dirty</dt><dd>{value.dirty ? "Yes" : "No"}</dd><dt>Session</dt><dd>{value.sessionName ?? "Current workspace"}</dd></dl>
    <h3>Git status summary</h3><pre>{value.gitStatusSummary || "No status output"}</pre>
    <h3>Diff statistics</h3><pre>{value.diffStatSummary || "No diff statistics"}</pre>
    <h3>Changed files</h3>
    {value.changedFiles.length ? <ul>{value.changedFiles.map((file) => <li key={`${file.status}:${file.path}`}><span className="status-chip">{file.status}</span> <span className="plain-path">{file.path}</span></li>)}</ul> : <p>No changed files.</p>}
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

function ArtifactsPanel({ review, onArtifact }: { review: ReviewState; onArtifact(id: string): void }) {
  return <div className="review-section"><h2>Artifacts</h2>
    {review.artifacts.length ? <div className="review-list">{review.artifacts.map((artifact) => <button type="button" key={artifact.artifactId} onClick={() => onArtifact(artifact.artifactId)}><span>{artifact.kind}</span><span>{artifact.availability}</span></button>)}</div> : review.status === "ready" ? <p>No artifacts.</p> : null}
    {review.selectedArtifact ? <ArtifactDetail artifact={review.selectedArtifact} /> : null}
  </div>;
}

function PreviewPanel({ artifacts, selected, onArtifact }: { artifacts: readonly ArtifactMetadataData[]; selected: ArtifactMetadataData | null; onArtifact(id: string): void }) {
  const managed = artifacts.filter(isManagedPreview);
  return <div className="review-section"><h2>Managed preview metadata</h2>
    <p className="disclaimer">Metadata and verification status do not prove manufacturing or image correctness. No file bytes are opened or rendered here.</p>
    {managed.length ? <div className="review-list">{managed.map((artifact) => <button type="button" key={artifact.artifactId} onClick={() => onArtifact(artifact.artifactId)}><span>{artifact.kind}</span><span>{artifact.verification}</span></button>)}</div> : <p>No managed Gerber/TIFF preview artifacts.</p>}
    {selected && isManagedPreview(selected) ? <ArtifactDetail artifact={selected} /> : null}
  </div>;
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

function title(value: string): string { return value[0]?.toUpperCase() + value.slice(1); }
