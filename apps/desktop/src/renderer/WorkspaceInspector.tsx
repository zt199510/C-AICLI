// Compatibility facade for callers that still import the original inspector module.
// New code should import the component boundary from ./workspace directly.
import { X } from "lucide-react";
import type { ReviewState } from "./desktop-state";
import type { ReviewCommands } from "./ReviewInspector";
import { TerminalPanel, type TerminalCommands } from "./TerminalPanel";
import { TerminalWorkspacePanel } from "./terminal/TerminalWorkspacePanel";
import type { TerminalSessionsController } from "./terminal/useTerminalSessions";
import type { TerminalProfileId } from "../shared/bridge-contract";
import {
  WorkspaceToolSidebar as WorkspaceToolSidebarView,
} from "./workspace/WorkspaceToolSidebar";

export { WorkspaceSummaryOverlay } from "./workspace/WorkspaceSummaryOverlay";
export { WorkspacePanelHost } from "./workspace/WorkspacePanelHost";
export { buildWorkspaceToolGroups as WorkspaceToolRegistry } from "./workspace/WorkspaceToolRegistry";
export type { WorkspacePanel } from "./workspace/WorkspaceToolRegistry";

import type { WorkspacePanel } from "./workspace/WorkspaceToolRegistry";

interface WorkspaceSurfaceContext {
  readonly activePanel: WorkspacePanel;
  readonly review: ReviewState;
  readonly workspaceReady: boolean;
  readonly workspaceLabel: string;
  readonly threadLabel: string;
  readonly threadId?: string | null;
  readonly turnLabel: string;
}

interface WorkspaceToolSidebarProps extends WorkspaceSurfaceContext {
  readonly visible: boolean;
  readonly openPanels: readonly WorkspacePanel[];
  readonly commands: ReviewCommands;
  readonly terminalCommands: TerminalCommands | undefined;
  readonly terminalController?: TerminalSessionsController;
  readonly defaultShell?: TerminalProfileId;
  readonly onReport: (reportId: string) => void;
  readonly onArtifact: (artifactId: string) => void;
  readonly onShareTerminalSelection?: (text: string) => void;
  readonly onPanel: (panel: WorkspacePanel) => void;
  readonly onClosePanel: (panel: WorkspacePanel) => void;
}

interface WorkspaceBottomPanelProps extends WorkspaceSurfaceContext {
  readonly visible: boolean;
  readonly commands: ReviewCommands;
  readonly terminalCommands: TerminalCommands | undefined;
  readonly terminalController?: TerminalSessionsController;
  readonly defaultShell?: TerminalProfileId;
  readonly onReport: (reportId: string) => void;
  readonly onArtifact: (artifactId: string) => void;
  readonly onClose?: () => void;
  readonly onShareTerminalSelection?: (text: string) => void;
}

export function WorkspaceToolSidebar({
  activePanel,
  visible,
  openPanels,
  review,
  workspaceReady,
  workspaceLabel,
  threadLabel,
  threadId,
  turnLabel,
  commands,
  terminalCommands,
  terminalController,
  defaultShell,
  onReport,
  onArtifact,
  onShareTerminalSelection,
  onPanel,
  onClosePanel,
}: WorkspaceToolSidebarProps) {
  return <WorkspaceToolSidebarView
    activePanel={activePanel}
    visible={visible}
    openPanels={openPanels}
    review={review}
    workspaceReady={workspaceReady}
    workspaceLabel={workspaceLabel}
    threadLabel={threadLabel}
    threadId={threadId}
    turnLabel={turnLabel}
    commands={commands}
    terminalCommands={terminalCommands}
    terminalController={terminalController}
    defaultShell={defaultShell}
    onReport={onReport}
    onArtifact={onArtifact}
    onShareTerminalSelection={onShareTerminalSelection}
    onPanel={onPanel}
    onClosePanel={onClosePanel}
  />;
}

export function WorkspaceBottomPanel(props: WorkspaceBottomPanelProps) {
  return <section id="workspace-bottom-panel" aria-label="终端控制台" className="workspace-bottom-panel" data-panel="terminal">
    <div className="workspace-bottom-panel-body">
      {props.terminalController ? <TerminalWorkspacePanel
        controller={props.terminalController}
        active={props.visible}
        defaultShell={props.defaultShell}
        onClosePanel={props.onClose}
        onShareSelection={props.onShareTerminalSelection}
      /> : <>
        {props.onClose ? <button className="workspace-bottom-panel-close icon-button" type="button" aria-label="关闭底部面板" onClick={props.onClose}><X size={15} aria-hidden="true" /></button> : null}
        <TerminalPanel workspaceReady={props.workspaceReady} commands={props.terminalCommands} active={props.visible} />
      </>}
    </div>
  </section>;
}
