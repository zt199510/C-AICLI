import { PanelBottom, PanelRight, PanelTop } from "lucide-react";
import type { RefObject } from "react";

export interface WorkspacePanelToggleGroupProps {
  readonly summaryOpen: boolean;
  readonly bottomPanelOpen: boolean;
  readonly toolSidebarOpen: boolean;
  readonly summaryTrigger: RefObject<HTMLButtonElement | null>;
  readonly bottomPanelTrigger: RefObject<HTMLButtonElement | null>;
  readonly toolSidebarTrigger: RefObject<HTMLButtonElement | null>;
  readonly onSummary: () => void;
  readonly onBottomPanel: () => void;
  readonly onToolSidebar: () => void;
}

export function WorkspacePanelToggleGroup({
  summaryOpen,
  bottomPanelOpen,
  toolSidebarOpen,
  summaryTrigger,
  bottomPanelTrigger,
  toolSidebarTrigger,
  onSummary,
  onBottomPanel,
  onToolSidebar,
}: WorkspacePanelToggleGroupProps) {
  return <div className="panel-toggle-group" aria-label="工作区面板">
    <button ref={summaryTrigger} className="icon-button panel-toggle" type="button" title="显示或隐藏工作区摘要（Ctrl+Shift+1）" aria-label="Toggle workspace summary" aria-keyshortcuts="Control+Shift+1" aria-controls="workspace-summary-overlay" aria-pressed={summaryOpen} onClick={onSummary}><PanelTop size={17} aria-hidden="true" /></button>
    <button ref={bottomPanelTrigger} className="icon-button panel-toggle" type="button" title="显示或隐藏底部终端（Ctrl+Shift+2）" aria-label="Toggle workspace bottom panel" aria-keyshortcuts="Control+Shift+2" aria-controls="workspace-bottom-panel" aria-pressed={bottomPanelOpen} onClick={onBottomPanel}><PanelBottom size={17} aria-hidden="true" /></button>
    <button id="workspace-tool-sidebar-toggle" ref={toolSidebarTrigger} className="icon-button panel-toggle" type="button" title="显示或隐藏右侧工作台（Ctrl+Shift+3）" aria-label="Toggle workspace tool sidebar" aria-keyshortcuts="Control+Shift+3" aria-controls="workspace-tool-sidebar" aria-pressed={toolSidebarOpen} onClick={onToolSidebar}><PanelRight size={17} aria-hidden="true" /></button>
  </div>;
}
