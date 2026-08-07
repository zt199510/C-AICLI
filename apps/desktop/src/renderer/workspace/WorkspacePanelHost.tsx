import { CircleAlert } from "lucide-react";
import type { ReviewState } from "../desktop-state";
import { ReviewPanel, type ReviewCommands } from "../ReviewInspector";
import { TerminalPanel, type TerminalCommands } from "../TerminalPanel";
import { TerminalWorkspacePanel } from "../terminal/TerminalWorkspacePanel";
import type { TerminalSessionsController } from "../terminal/useTerminalSessions";
import type { TerminalProfileId } from "../../shared/bridge-contract";
import { GitWorkspacePanel } from "./GitWorkspacePanel";
import { SubagentWorkspacePanel } from "./SubagentWorkspacePanel";
import { isReviewWorkspacePanel, type ToolStatus, type WorkspacePanel } from "./WorkspaceToolRegistry";

export interface WorkspacePanelHostProps {
  readonly activePanel: WorkspacePanel;
  readonly visible: boolean;
  readonly review: ReviewState;
  readonly workspaceReady: boolean;
  readonly workspaceLabel: string;
  readonly threadLabel: string;
  readonly threadId?: string | null;
  readonly turnLabel: string;
  readonly commands: ReviewCommands;
  readonly terminalCommands: TerminalCommands | undefined;
  readonly terminalController?: TerminalSessionsController;
  readonly defaultShell?: TerminalProfileId;
  readonly onReport: (reportId: string) => void;
  readonly onArtifact: (artifactId: string) => void;
  readonly onClose?: () => void;
  readonly onShareTerminalSelection?: (text: string) => void;
}

export function WorkspacePanelHost(props: WorkspacePanelHostProps) {
  return <section
    id="workspace-panel-host"
    role="tabpanel"
    aria-labelledby={`workspace-workbench-tab-${props.activePanel}`}
    className="workspace-panel-host"
    data-panel={props.activePanel}
  >
    <div className="workspace-panel-scroll" tabIndex={0}>
      <WorkspacePanelDetail {...props} />
    </div>
  </section>;
}

export function WorkspacePanelDetail({
  activePanel,
  review,
  workspaceReady,
  workspaceLabel,
  threadLabel,
  threadId,
  turnLabel,
  commands,
  terminalCommands,
  terminalController,
  visible,
  onReport,
  onArtifact,
  onShareTerminalSelection,
  defaultShell,
}: WorkspacePanelHostProps) {
  if (activePanel === "terminal") {
    if (terminalController) return <TerminalWorkspacePanel controller={terminalController} active={visible} chrome="embedded" defaultShell={defaultShell} onShareSelection={onShareTerminalSelection} />;
    return <TerminalPanel workspaceReady={workspaceReady} commands={terminalCommands} active={visible} />;
  }
  if (isReviewWorkspacePanel(activePanel)) {
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
      <h2>本地环境</h2>
      <dl>
        <dt>工作区</dt><dd className="plain-path">{workspaceLabel}</dd>
        <dt>连接</dt><dd>{workspaceReady ? "工作区就绪" : "未打开工作区"}</dd>
        <dt>对话</dt><dd>{threadLabel}</dd>
        <dt>最近任务</dt><dd>{turnLabel}</dd>
      </dl>
    </div>;
  }
  if (activePanel === "branch" || activePanel === "worktrees" || activePanel === "pull-request" || activePanel === "git-actions" || activePanel === "compare")
    return <GitWorkspacePanel mode={activePanel} changes={review.changes} commands={commands} threadId={threadId ?? null} />;
  if (activePanel === "agents") return <SubagentWorkspacePanel bridge={window.caicli} threadId={threadId ?? null} active={visible} />;
  return <InspectorStatusView
    title="Sub-agent 活动不可用"
    tone="unknown"
    message="当前 desktop 协议没有运行中、等待处理和已完成的 Sub-agent 计数。"
    detail="客户端不会从对话文本推断或伪造执行状态。"
  />;
}

function InspectorStatusView({ title, tone, message, detail }: {
  readonly title: string;
  readonly tone: ToolStatus;
  readonly message: string;
  readonly detail: string;
}) {
  return <div className="inspector-status-view review-section" data-tone={tone}>
    <CircleAlert size={18} aria-hidden="true" />
    <div><h2>{title}</h2><p>{message}</p><small>{detail}</small></div>
  </div>;
}
