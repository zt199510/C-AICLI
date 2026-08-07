import type { ReviewState } from "../desktop-state";

export interface WorkspaceSummaryOverlayProps {
  readonly review: ReviewState;
  readonly workspaceReady: boolean;
  readonly workspaceLabel: string;
  readonly threadLabel: string;
  readonly turnLabel: string;
  readonly runtimeLabel: string;
}

export function WorkspaceSummaryOverlay({
  review,
  workspaceReady,
  workspaceLabel,
  threadLabel,
  turnLabel,
  runtimeLabel,
}: WorkspaceSummaryOverlayProps) {
  const changedFileCount = review.changes?.changedFiles.length ?? 0;
  return <section id="workspace-summary-overlay" className="workspace-summary-overlay" aria-labelledby="workspace-summary-title">
    <header className="workspace-summary-heading">
      <div><span>工作区摘要</span><strong id="workspace-summary-title">当前上下文</strong></div>
      <span className={`workspace-summary-state ${workspaceReady ? "is-ready" : "is-unavailable"}`}>
        {workspaceReady ? "就绪" : "未打开工作区"}
      </span>
    </header>
    <dl className="workspace-summary-grid">
      <dt>工作区</dt><dd className="plain-path" title={workspaceLabel}>{workspaceLabel}</dd>
      <dt>对话</dt><dd title={threadLabel}>{threadLabel}</dd>
      <dt>最近任务</dt><dd title={turnLabel}>{turnLabel}</dd>
      <dt>运行时</dt><dd>{runtimeLabel}</dd>
    </dl>
    <div className="workspace-summary-counts" aria-label="工作区证据计数">
      <span><strong>{changedFileCount}</strong> 个变更文件</span>
      <span><strong>{review.reports.length}</strong> 份报告</span>
      <span><strong>{review.artifacts.length}</strong> 个产物</span>
    </div>
  </section>;
}
