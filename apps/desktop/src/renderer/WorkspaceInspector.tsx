import {
  Bot,
  CircleAlert,
  FileText,
  GitBranch,
  GitCompare,
  GitPullRequest,
  HardDrive,
  Package,
  ScanLine,
  Send,
  SquareTerminal,
} from "lucide-react";
import { useEffect, useMemo, useRef, type KeyboardEvent } from "react";
import type { ReviewState } from "./desktop-state";
import { ReviewPanel, type ReviewCommands } from "./ReviewInspector";
import { TerminalPanel, type TerminalCommands } from "./TerminalPanel";

export type WorkspacePanel =
  | "changes"
  | "local"
  | "branch"
  | "terminal"
  | "git-actions"
  | "pull-request"
  | "compare"
  | "reports"
  | "artifacts"
  | "preview"
  | "agents";

type ReviewPanelId = Extract<WorkspacePanel, "changes" | "reports" | "artifacts" | "preview">;
type ToolStatus = "unknown" | "loading" | "ready" | "warning" | "error" | "disabled";

interface ToolDefinition {
  readonly id: WorkspacePanel;
  readonly label: string;
  readonly icon: typeof GitCompare;
  readonly status: ToolStatus;
  readonly statusLabel: string;
  readonly badge?: number | string;
  readonly disabledReason?: string;
  readonly selectable: boolean;
}

interface ToolGroup {
  readonly id: "environment" | "evidence" | "agents";
  readonly label: string;
  readonly tools: readonly ToolDefinition[];
}

interface WorkspaceSurfaceContext {
  activePanel: WorkspacePanel;
  review: ReviewState;
  workspaceReady: boolean;
  workspaceLabel: string;
  threadLabel: string;
  turnLabel: string;
}

interface WorkspaceToolSidebarProps extends WorkspaceSurfaceContext {
  visible: boolean;
  terminalCommands: TerminalCommands | undefined;
  onPanel(panel: WorkspacePanel): void;
}

interface WorkspaceBottomPanelProps extends WorkspaceSurfaceContext {
  visible: boolean;
  commands: ReviewCommands;
  terminalCommands: TerminalCommands | undefined;
  onReport(reportId: string): void;
  onArtifact(artifactId: string): void;
}

interface WorkspaceSummaryOverlayProps extends Omit<WorkspaceSurfaceContext, "activePanel"> {
  runtimeLabel: string;
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
      <div><span>Workspace summary</span><strong id="workspace-summary-title">Current context</strong></div>
      <span className={`workspace-summary-state ${workspaceReady ? "is-ready" : "is-unavailable"}`}>
        {workspaceReady ? "Ready" : "No workspace"}
      </span>
    </header>
    <dl className="workspace-summary-grid">
      <dt>Workspace</dt><dd className="plain-path" title={workspaceLabel}>{workspaceLabel}</dd>
      <dt>Conversation</dt><dd title={threadLabel}>{threadLabel}</dd>
      <dt>Recent task</dt><dd title={turnLabel}>{turnLabel}</dd>
      <dt>Runtime</dt><dd>{runtimeLabel}</dd>
    </dl>
    <div className="workspace-summary-counts" aria-label="Workspace evidence counts">
      <span><strong>{changedFileCount}</strong> changed files</span>
      <span><strong>{review.reports.length}</strong> reports</span>
      <span><strong>{review.artifacts.length}</strong> artifacts</span>
    </div>
  </section>;
}

export function WorkspaceToolSidebar({
  activePanel,
  visible,
  review,
  workspaceReady,
  terminalCommands,
  onPanel,
}: WorkspaceToolSidebarProps) {
  const toolRefs = useRef(new Map<WorkspacePanel, HTMLButtonElement>());
  const rootRef = useRef<HTMLDivElement>(null);
  const wasVisible = useRef(visible);
  const groups = useMemo(
    () => buildToolGroups(review, workspaceReady, Boolean(terminalCommands)),
    [review, terminalCommands, workspaceReady],
  );
  const tools = useMemo(() => groups.flatMap((group) => group.tools), [groups]);
  useEffect(() => {
    const opened = visible && !wasVisible.current;
    wasVisible.current = visible;
    if (opened && window.innerWidth <= 1120) {
      const target = tools.find((tool) => tool.id === activePanel && isSelectable(tool)) ?? tools.find(isSelectable);
      if (target) toolRefs.current.get(target.id)?.focus();
    }
  }, [activePanel, tools, visible]);

  function navigateTools(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    let next: number;
    if (event.key === "ArrowDown" || event.key === "ArrowRight") next = nextEnabledTool(tools, index, 1);
    else if (event.key === "ArrowUp" || event.key === "ArrowLeft") next = nextEnabledTool(tools, index, -1);
    else if (event.key === "Home") next = tools.findIndex(isSelectable);
    else if (event.key === "End") next = lastEnabledTool(tools);
    else return;
    event.preventDefault();
    const tool = tools[next];
    if (!tool) return;
    onPanel(tool.id);
    toolRefs.current.get(tool.id)?.focus();
  }

  function trapDrawerFocus(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key !== "Tab" || window.innerWidth > 1120 || !rootRef.current) return;
    const focusable = [...rootRef.current.querySelectorAll<HTMLElement>(
      'button:not([disabled]), [href], input:not([disabled]), [tabindex]:not([tabindex="-1"])',
    )].filter((element) => !element.hidden && element.getAttribute("aria-hidden") !== "true");
    const first = focusable[0];
    const last = focusable.at(-1);
    if (!first || !last) return;
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  return <div className="workspace-tool-sidebar" ref={rootRef} onKeyDown={trapDrawerFocus}>
    <section className="inspector-nav-card" aria-label="Workspace tools">
      <div className="inspector-card-heading">
        <div className="context-title">
          <span>Workspace tools</span>
          <small>Navigation and status</small>
        </div>
      </div>

      <nav className="inspector-tool-nav" aria-label="Workspace tool groups">
        {groups.map((group) => <section className="inspector-tool-group" aria-labelledby={`inspector-group-${group.id}`} key={group.id}>
          <h2 id={`inspector-group-${group.id}`}>{group.label}</h2>
          <div role="group" aria-labelledby={`inspector-group-${group.id}`}>
            {group.tools.map((tool) => {
              const Icon = tool.icon;
              const index = tools.findIndex((candidate) => candidate.id === tool.id);
              const selected = activePanel === tool.id;
              const statusId = `inspector-tool-status-${tool.id}`;
              return <button
                id={`inspector-tool-${tool.id}`}
                aria-label={tool.label}
                aria-controls="workspace-bottom-panel"
                aria-describedby={statusId}
                aria-pressed={selected}
                className="inspector-tool-row"
                data-status={tool.status}
                ref={(value) => {
                  if (value) toolRefs.current.set(tool.id, value);
                  else toolRefs.current.delete(tool.id);
                }}
                tabIndex={selected && isSelectable(tool) ? 0 : -1}
                key={tool.id}
                type="button"
                title={tool.disabledReason ? `${tool.label}: ${tool.disabledReason}` : tool.label}
                onKeyDown={(event) => navigateTools(event, index)}
                onClick={() => { if (isSelectable(tool)) onPanel(tool.id); }}
                disabled={!isSelectable(tool)}
              >
                <Icon size={15} aria-hidden="true" />
                <span className="inspector-tool-label">{tool.label}</span>
                <span className="inspector-tool-tail" aria-hidden="true">
                  {tool.badge !== undefined ? <span className="context-badge">{formatBadge(tool.badge)}</span> : null}
                  <span className="inspector-tool-status-dot" />
                </span>
                <span className="sr-only" id={statusId}>{tool.statusLabel}{tool.disabledReason ? `. ${tool.disabledReason}` : ""}</span>
              </button>;
            })}
          </div>
        </section>)}
      </nav>
    </section>
  </div>;
}

export function WorkspaceBottomPanel({
  activePanel,
  review,
  workspaceReady,
  workspaceLabel,
  threadLabel,
  turnLabel,
  commands,
  terminalCommands,
  visible,
  onReport,
  onArtifact,
}: WorkspaceBottomPanelProps) {
  const groups = useMemo(
    () => buildToolGroups(review, workspaceReady, Boolean(terminalCommands)),
    [review, terminalCommands, workspaceReady],
  );
  const tools = useMemo(() => groups.flatMap((group) => group.tools), [groups]);
  const activeTool = tools.find((tool) => tool.id === activePanel) ?? tools[0]!;

  return <section
      id="workspace-bottom-panel"
      aria-labelledby="workspace-bottom-panel-title"
      className="workspace-bottom-panel inspector-detail-card"
    >
      <header className="inspector-detail-heading">
        <span>Current tool</span>
        <strong id="workspace-bottom-panel-title">{activeTool.label}</strong>
        <span className={`inspector-detail-status status-${activeTool.status}`}>{activeTool.statusLabel}</span>
      </header>
      <div className="inspector-detail-scroll" tabIndex={0}>
        <InspectorDetail
          activePanel={activePanel}
          review={review}
          workspaceReady={workspaceReady}
          workspaceLabel={workspaceLabel}
          threadLabel={threadLabel}
          turnLabel={turnLabel}
          commands={commands}
          terminalCommands={terminalCommands}
          visible={visible}
          onReport={onReport}
          onArtifact={onArtifact}
        />
      </div>
    </section>;
}

function InspectorDetail({
  activePanel,
  review,
  workspaceReady,
  workspaceLabel,
  threadLabel,
  turnLabel,
  commands,
  terminalCommands,
  visible,
  onReport,
  onArtifact,
}: WorkspaceBottomPanelProps) {
  if (activePanel === "terminal") {
    return <TerminalPanel workspaceReady={workspaceReady} commands={terminalCommands} active={visible} />;
  }
  if (isReviewPanel(activePanel)) {
    return <div className="review-content"><ReviewPanel
      review={{ ...review, activeTab: activePanel }}
      workspaceReady={workspaceReady}
      onReport={onReport}
      onArtifact={onArtifact}
      commands={commands}
    /></div>;
  }
  if (activePanel === "local") {
    return <div className="review-section inspector-summary-detail">
      <h2>Local environment</h2>
      <dl>
        <dt>Workspace</dt><dd className="plain-path">{workspaceLabel}</dd>
        <dt>Connection</dt><dd>{workspaceReady ? "Workspace ready" : "No workspace open"}</dd>
        <dt>Conversation</dt><dd>{threadLabel}</dd>
        <dt>Latest turn</dt><dd>{turnLabel}</dd>
      </dl>
    </div>;
  }
  if (activePanel === "branch") {
    const branch = detectBranch(review.changes?.gitStatusSummary);
    return <InspectorStatusView
      title="Current branch"
      tone={branch ? "ready" : "unknown"}
      message={branch ?? "Branch name is unavailable in the current workspace snapshot."}
      detail="The inspector keeps unknown separate from an empty branch value. Open Changes for the authoritative Git summary."
    />;
  }
  if (activePanel === "pull-request") {
    return <InspectorStatusView
      title="Pull request status unavailable"
      tone="error"
      message="The current desktop protocol does not expose pull request status."
      detail="This is shown as unavailable rather than as ‘no pull request’. Reopen this tool after a provider is connected."
    />;
  }
  if (activePanel === "git-actions") {
    return <InspectorStatusView
      title="Commit or push is disabled"
      tone="disabled"
      message="No supported Git write command is available in this release."
      detail="Selecting a navigation row never commits or pushes. Any future write flow must show a separate confirmation and prevent duplicate submission."
    />;
  }
  if (activePanel === "compare") {
    return <InspectorStatusView
      title="Compare branches is unavailable"
      tone="disabled"
      message="No compare-branch endpoint is available in the current desktop protocol."
      detail="Use Changes for the current bounded diff summary."
    />;
  }
  return <InspectorStatusView
    title="Sub-agent activity unavailable"
    tone="unknown"
    message="Running, attention, and completed sub-agent counts are not exposed by the current desktop protocol."
    detail="The inspector does not infer or fabricate execution status from conversation text."
  />;
}

function InspectorStatusView({
  title,
  tone,
  message,
  detail,
}: {
  title: string;
  tone: ToolStatus;
  message: string;
  detail: string;
}) {
  return <div className="inspector-status-view review-section" data-tone={tone}>
    <CircleAlert size={18} aria-hidden="true" />
    <div><h2>{title}</h2><p>{message}</p><small>{detail}</small></div>
  </div>;
}

function buildToolGroups(review: ReviewState, workspaceReady: boolean, terminalReady: boolean): readonly ToolGroup[] {
  const changesStatus = reviewStatus(review, "changes", Boolean(review.changes));
  const reportsStatus = reviewStatus(review, "reports", review.reports.length > 0);
  const artifactsStatus = reviewStatus(review, "artifacts", review.artifacts.length > 0);
  const previewStatus = reviewStatus(review, "preview", review.artifacts.length > 0);
  const branch = detectBranch(review.changes?.gitStatusSummary);
  return [
    {
      id: "environment",
      label: "Environment",
      tools: [
        tool("changes", "Changes", GitCompare, workspaceReady ? changesStatus : "disabled", review.changes?.dirty ? review.changes.changedFiles.length || 1 : undefined, workspaceReady ? undefined : "Open a workspace to inspect changes."),
        tool("local", "Local", HardDrive, workspaceReady ? "ready" : "disabled", workspaceReady ? "Connected" : undefined, workspaceReady ? undefined : "Open a workspace to inspect the local environment."),
        tool("branch", branch ?? "Branch", GitBranch, branch ? "ready" : "unknown"),
        tool("terminal", "Terminal", SquareTerminal, workspaceReady && terminalReady ? "ready" : "disabled", undefined, workspaceReady ? "Terminal commands are unavailable." : "Open a workspace to use the terminal."),
        tool("git-actions", "Commit or push", Send, "disabled", undefined, "Git write actions require a supported, separately confirmed command flow.", false),
        tool("pull-request", "Pull request", GitPullRequest, "error", "Unavailable"),
        tool("compare", "Compare branches", GitCompare, "disabled", undefined, "Branch comparison is not exposed by the current desktop protocol.", false),
      ],
    },
    {
      id: "evidence",
      label: "Results and evidence",
      tools: [
        tool("reports", "Reports", FileText, workspaceReady ? reportsStatus : "disabled", countBadge(reportsStatus, review.reports.length), workspaceReady ? undefined : "Open a workspace to review reports."),
        tool("artifacts", "Artifacts", Package, workspaceReady ? artifactsStatus : "disabled", countBadge(artifactsStatus, review.artifacts.length), workspaceReady ? undefined : "Open a workspace to review artifacts."),
        tool("preview", "Preview", ScanLine, workspaceReady ? previewStatus : "disabled", previewStatus === "ready" ? "Available" : undefined, workspaceReady ? undefined : "Open a workspace to review previews."),
      ],
    },
    {
      id: "agents",
      label: "Sub-agents",
      tools: [tool("agents", "Activity", Bot, "unknown", "Unknown")],
    },
  ];
}

function tool(
  id: WorkspacePanel,
  label: string,
  icon: ToolDefinition["icon"],
  status: ToolStatus,
  badge?: number | string,
  disabledReason?: string,
  selectable = true,
): ToolDefinition {
  return { id, label, icon, status, statusLabel: statusLabel(status), badge, disabledReason, selectable };
}

function reviewStatus(review: ReviewState, panel: ReviewPanelId, hasData: boolean): ToolStatus {
  if (review.activeTab === panel && review.status === "loading") return "loading";
  if (review.activeTab === panel && review.status === "error") return "error";
  if (hasData || (review.activeTab === panel && review.status === "ready")) return "ready";
  return "unknown";
}

function statusLabel(status: ToolStatus): string {
  if (status === "loading") return "Loading";
  if (status === "ready") return "Ready";
  if (status === "warning") return "Needs attention";
  if (status === "error") return "Unavailable";
  if (status === "disabled") return "Disabled";
  return "Unknown";
}

function isSelectable(tool: ToolDefinition): boolean {
  return tool.selectable;
}

function nextEnabledTool(tools: readonly ToolDefinition[], index: number, direction: 1 | -1): number {
  for (let offset = 1; offset <= tools.length; offset++) {
    const candidate = (index + offset * direction + tools.length) % tools.length;
    if (isSelectable(tools[candidate]!)) return candidate;
  }
  return index;
}

function lastEnabledTool(tools: readonly ToolDefinition[]): number {
  for (let index = tools.length - 1; index >= 0; index--) {
    if (isSelectable(tools[index]!)) return index;
  }
  return 0;
}

function countBadge(status: ToolStatus, count: number): number | undefined {
  return status === "ready" ? count : undefined;
}

function formatBadge(value: number | string): string {
  if (typeof value === "number" && value > 999) return "999+";
  return String(value);
}

function isReviewPanel(panel: WorkspacePanel): panel is ReviewPanelId {
  return panel === "changes" || panel === "reports" || panel === "artifacts" || panel === "preview";
}

function detectBranch(summary: string | undefined): string | null {
  const match = summary?.match(/^##\s+([^\s.]+)(?:\.\.\.|\s|$)/m);
  return match?.[1] ?? null;
}
