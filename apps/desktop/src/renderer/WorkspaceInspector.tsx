import { FileText, GitCompare, Package, ScanLine, SquareTerminal, X } from "lucide-react";
import { useRef, type KeyboardEvent } from "react";
import type { ReviewState } from "./desktop-state";
import { ReviewPanel, type ReviewCommands } from "./ReviewInspector";
import { TerminalPanel } from "./TerminalPanel";

export type WorkspacePanel = "changes" | "terminal" | "reports" | "artifacts" | "preview";

const panels = [
  { id: "changes", label: "Changes", icon: GitCompare },
  { id: "terminal", label: "Terminal", icon: SquareTerminal },
  { id: "reports", label: "Reports", icon: FileText },
  { id: "artifacts", label: "Artifacts", icon: Package },
  { id: "preview", label: "Preview", icon: ScanLine },
] as const;

interface WorkspaceInspectorProps {
  activePanel: WorkspacePanel;
  visible: boolean;
  review: ReviewState;
  workspaceReady: boolean;
  workspaceLabel: string;
  threadLabel: string;
  turnLabel: string;
  commands: ReviewCommands;
  onPanel(panel: WorkspacePanel): void;
  onReport(reportId: string): void;
  onArtifact(artifactId: string): void;
  onClose(): void;
}

export function WorkspaceInspector({
  activePanel,
  visible,
  review,
  workspaceReady,
  workspaceLabel,
  threadLabel,
  turnLabel,
  commands,
  onPanel,
  onReport,
  onArtifact,
  onClose,
}: WorkspaceInspectorProps) {
  const tabRefs = useRef<Array<HTMLButtonElement | null>>([]);

  function navigateTabs(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    let next: number;
    if (event.key === "ArrowRight" || event.key === "ArrowDown") next = (index + 1) % panels.length;
    else if (event.key === "ArrowLeft" || event.key === "ArrowUp") next = (index - 1 + panels.length) % panels.length;
    else if (event.key === "Home") next = 0;
    else if (event.key === "End") next = panels.length - 1;
    else return;
    event.preventDefault();
    onPanel(panels[next]!.id);
    tabRefs.current[next]?.focus();
  }

  return <div className="workspace-inspector">
    <div className="panel-heading context-heading">
      <div className="context-title">
        <span>Workspace context</span>
        <small title={`${workspaceLabel} · ${threadLabel} · ${turnLabel}`}>{threadLabel} · {turnLabel}</small>
      </div>
      <button className="icon-button" type="button" title="Close workspace inspector" aria-label="Close review inspector" onClick={onClose}><X size={17} aria-hidden="true" /></button>
    </div>

    <div className="context-tabs" role="tablist" aria-label="Workspace tools">
      {panels.map((panel, index) => {
        const Icon = panel.icon;
        const badge = panel.id === "reports" ? review.reports.length
          : panel.id === "artifacts" || panel.id === "preview" ? review.artifacts.length
            : panel.id === "changes" && review.changes?.dirty ? 1 : 0;
        return <button
          id={`context-tab-${panel.id}`}
          aria-controls={`context-panel-${panel.id}`}
          aria-selected={activePanel === panel.id}
          ref={(value) => { tabRefs.current[index] = value; }}
          tabIndex={activePanel === panel.id ? 0 : -1}
          key={panel.id}
          type="button"
          role="tab"
          onKeyDown={(event) => navigateTabs(event, index)}
          onClick={() => onPanel(panel.id)}
        >
          <Icon size={14} aria-hidden="true" />
          <span>{panel.label}</span>
          {badge ? <span className="context-badge" aria-hidden="true" title={`${badge} items`}>{badge}</span> : null}
        </button>;
      })}
    </div>

    <div className="context-panels">
      {panels.map((panel) => <section
        id={`context-panel-${panel.id}`}
        aria-labelledby={`context-tab-${panel.id}`}
        className="context-panel"
        hidden={activePanel !== panel.id}
        key={panel.id}
        role="tabpanel"
        tabIndex={0}
      >
        {panel.id === "terminal"
          ? <TerminalPanel workspaceReady={workspaceReady} active={visible && activePanel === "terminal"} />
          : activePanel === panel.id
            ? <div className="review-content"><ReviewPanel
                review={{ ...review, activeTab: panel.id }}
                workspaceReady={workspaceReady}
                onReport={onReport}
                onArtifact={onArtifact}
                commands={commands}
              /></div>
            : null}
      </section>)}
    </div>
  </div>;
}
