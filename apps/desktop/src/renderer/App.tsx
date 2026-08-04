import { Archive, FolderOpen, PanelLeft, PanelRight, RefreshCw, SquarePen } from "lucide-react";
import { useEffect, useState, type CSSProperties } from "react";
import { useShellPanels } from "./app/use-shell-panels";
import { Composer } from "./Composer";
import { ThreadSidebar } from "./ThreadSidebar";
import type { ThreadFilter } from "./thread-sidebar-model";
import { TimelineView } from "./TimelineView";
import { useDesktopController } from "./use-desktop-controller";
import { WorkspaceInspector, type WorkspacePanel } from "./WorkspaceInspector";

export function App() {
  const bridge = typeof window !== "undefined" ? window.caicli : undefined;
  const controller = useDesktopController(bridge, { autoSelectConversation: true });
  const { state } = controller;
  const [opening, setOpening] = useState(false);
  const [workspaceError, setWorkspaceError] = useState<string | null>(null);
  const [workspacePanel, setWorkspacePanel] = useState<WorkspacePanel>("changes");
  const [stopping, setStopping] = useState(false);
  const [threadFilter, setThreadFilter] = useState<ThreadFilter>("all");
  const panels = useShellPanels();
  const { leftOpen, inspectorOpen, showThreadsTrigger, showInspectorTrigger } = panels;

  useEffect(() => {
    if (state.workspace) controller.setReviewTab("changes");
  }, [state.workspace?.workspaceId]);

  async function openWorkspace() {
    setOpening(true);
    setWorkspaceError(null);
    try {
      const result = await controller.openWorkspace();
      if (result && !result.succeeded) setWorkspaceError(result.error?.safeMessage ?? "Workspace could not be opened.");
    } catch { setWorkspaceError("Workspace could not be opened."); }
    finally { setOpening(false); }
  }

  function selectWorkspacePanel(panel: WorkspacePanel) {
    setWorkspacePanel(panel);
    if (panel !== "terminal") controller.setReviewTab(panel);
  }

  const workspacePath = state.workspace?.rootPath ?? null;
  const bridgeUnavailable = !bridge;
  const statusMessage = bridgeUnavailable ? "Desktop bridge unavailable" : state.runtime.message;
  const stopTurn = state.runtime.state === "ready" && controller.activeTurn && !controller.activeTurn.approval &&
    !["completed", "failed", "canceled", "canceling"].includes(controller.activeTurn.status)
    ? controller.activeTurn
    : null;

  async function stopResponse() {
    if (!stopTurn) return;
    setStopping(true);
    try {
      await controller.cancelTurn(stopTurn.turnId, stopTurn.revision);
    } finally {
      setStopping(false);
    }
  }

  function showArchivedThreads() {
    setThreadFilter("archived");
    panels.showThreads();
  }

  function beginConversation() {
    setThreadFilter("all");
    controller.beginConversation();
  }

  return (
    <div className={`app-shell ${leftOpen ? "" : "left-collapsed"} ${inspectorOpen ? "" : "inspector-collapsed"}`} style={{ "--navigation-width": `${panels.navigationWidth}px`, "--inspector-width": `${panels.inspectorWidth}px` } as CSSProperties}>
      <header className="titlebar">
        <div className="brand" aria-label="C-AICLI Desktop">
          <span className="brand-mark" aria-hidden="true">C</span>
          <span className="brand-copy"><span className="brand-name">C-AICLI</span><span className="brand-edition">Desktop workspace</span></span>
        </div>
        <div className="workspace-title" title={workspacePath ?? "No workspace"}>{workspacePath ?? "No workspace"}</div>
        <div className={`runtime-status runtime-${state.runtime.state}`} role="status" aria-live="polite" aria-atomic="true"><span className="status-dot" aria-hidden="true" /><span>{statusMessage}</span></div>
      </header>

      <div className="workspace-layout">
        {!leftOpen && !panels.narrowViewport ? (
          <nav className="thread-rail" aria-label="会话快捷栏">
            <button ref={showThreadsTrigger} className="icon-button" type="button" title="展开会话侧栏" aria-label="展开会话侧栏" aria-controls="threads-panel" aria-expanded={false} onClick={panels.showThreads}>
              <PanelLeft size={18} aria-hidden="true" />
            </button>
            <button className="icon-button" type="button" title="新建对话" aria-label="新建对话" onClick={beginConversation}>
              <SquarePen size={18} aria-hidden="true" />
            </button>
            <button className="icon-button thread-rail-archive" type="button" title="查看已归档对话" aria-label="查看已归档对话" onClick={showArchivedThreads}>
              <Archive size={18} aria-hidden="true" />
            </button>
          </nav>
        ) : null}

        <aside id="threads-panel" className={`thread-sidebar drawer ${leftOpen ? "drawer-open" : ""}`} aria-label="对话面板" aria-hidden={!leftOpen} onKeyDown={(event) => {
          if (event.key === "Escape" && !event.defaultPrevented) { event.preventDefault(); panels.closeThreads(); }
        }}>
          <div
            className="navigation-resize-handle"
            role="separator"
            aria-label="调整会话侧栏宽度"
            aria-orientation="vertical"
            aria-valuemin={240}
            aria-valuemax={360}
            aria-valuenow={panels.navigationWidth}
            tabIndex={0}
            onDoubleClick={panels.resetNavigationWidth}
            onPointerDown={(event) => event.currentTarget.setPointerCapture(event.pointerId)}
            onPointerMove={(event) => { if (event.currentTarget.hasPointerCapture(event.pointerId)) panels.resizeNavigationAt(event.clientX); }}
            onPointerUp={(event) => event.currentTarget.releasePointerCapture(event.pointerId)}
            onKeyDown={(event) => {
              if (event.key === "ArrowLeft") { event.preventDefault(); panels.nudgeNavigationWidth(-16); }
              else if (event.key === "ArrowRight") { event.preventDefault(); panels.nudgeNavigationWidth(16); }
              else if (event.key === "Home") { event.preventDefault(); panels.resetNavigationWidth(); }
            }}
          />
          <ThreadSidebar
            threads={state.threads}
            status={state.threadsStatus}
            error={state.threadsError}
            truncated={state.threadsTruncated}
            selectedThreadId={state.selectedThreadId}
            workspacePath={workspacePath}
            visible={leftOpen}
            filter={threadFilter}
            onFilterChange={setThreadFilter}
            onExpand={panels.showThreads}
            onCollapse={() => panels.closeThreads()}
            onSelect={controller.selectThread}
            onNew={beginConversation}
            onRename={controller.renameThread}
            onArchive={controller.archiveThread}
          />
        </aside>

        <main className="task-surface">
          <div className="task-toolbar">
            <div className="toolbar-group">{!leftOpen && panels.narrowViewport ? <button ref={showThreadsTrigger} className="icon-button" type="button" title="显示会话侧栏" aria-label="显示会话侧栏" aria-controls="threads-panel" aria-expanded={leftOpen} onClick={panels.showThreads}><PanelLeft size={17} aria-hidden="true" /></button> : null}<span className="thread-heading"><span className="thread-eyebrow">对话</span><span className="task-label">{state.detail?.thread.title ?? (state.workspace ? "新建对话" : "工作区概览")}</span></span><span className="refreshing" role="status" aria-live="polite" hidden={!state.refreshing}>正在刷新…</span></div>
            <div className="toolbar-group">
              <button className="command-button" type="button" onClick={() => void openWorkspace()} disabled={opening || state.runtime.state !== "ready"}>{opening ? <RefreshCw className="spin" size={16} aria-hidden="true" /> : <FolderOpen size={16} aria-hidden="true" />}{opening ? "Opening" : "Open workspace"}</button>
              {!inspectorOpen && <button ref={showInspectorTrigger} className="icon-button" type="button" title="Show workspace inspector" aria-label="Show workspace inspector" aria-controls="review-inspector-panel" aria-expanded={inspectorOpen} onClick={panels.showInspector}><PanelRight size={17} aria-hidden="true" /></button>}
            </div>
          </div>

          <section className="timeline" aria-label="Workspace timeline">
            {!workspacePath ? (
              <div className="state-card workspace-empty">
                <div className="state-kicker">Local AI workspace</div>
                <h1>{state.runtime.state === "ready" ? "What should we build?" : state.runtime.message}</h1>
                <p>Open a project to start a focused conversation with auditable tools, approvals, and results.</p>
                {workspaceError && <p role="alert">{workspaceError}</p>}
                {state.runtime.state !== "failed" ? (
                  <button className="primary-action" type="button" onClick={() => void openWorkspace()} disabled={opening || state.runtime.state !== "ready"}><FolderOpen size={17} aria-hidden="true" /> Open workspace</button>
                ) : null}
              </div>
            ) : (
              <TimelineView
                detail={state.detail}
                status={state.detailStatus}
                error={state.detailError}
                onLoadMore={controller.loadMore}
                optimisticExchanges={controller.optimisticExchanges}
                onRestoreOptimistic={controller.restoreOptimistic}
                onApproval={controller.resolveApproval}
                onResume={controller.resumeTurn}
                onRestart={controller.restartTurn}
                runtimeBanner={state.runtime.state === "ready" ? null : (
                  <section className={`runtime-banner runtime-banner-${state.runtime.state}`} role="status" aria-live="polite">
                    <div>
                      <strong>{state.runtime.state === "restarting" || state.runtime.state === "starting" ? "正在重启 AppHost" : "AppHost 不可用"}</strong>
                      <span>已提交的对话历史保持可读，恢复后将通过权威 reload 对账。</span>
                    </div>
                  </section>
                )}
              />
            )}
          </section>
          <Composer
            key={state.selectedThreadId ?? "new-conversation"}
            draft={controller.composerDraft}
            composer={controller.composer}
            disabledReason={controller.composerDisabledReason}
            modelLabel={state.workspace?.configuration.effectiveModel}
            approvalModeLabel={state.workspace?.configuration.approvalMode}
            disabledActionLabel={state.runtime.state === "failed" && state.runtime.canRestart
              ? "Restart AppHost"
              : state.runtime.state === "restarting" || state.runtime.state === "starting"
                ? "Restarting AppHost"
                : !state.workspace
                  ? "Open workspace"
                  : state.detail?.thread.status === "archived"
                    ? "New conversation"
                    : undefined}
            onDisabledAction={state.runtime.state === "failed" && state.runtime.canRestart
              ? () => void controller.restartRuntime()
              : !state.workspace
                ? () => void openWorkspace()
                : state.detail?.thread.status === "archived"
                  ? beginConversation
                  : undefined}
            onText={controller.setComposerText}
            onSearch={(query) => void controller.searchMentions(query)}
            onCloseMentions={controller.closeMentions}
            onContext={controller.addComposerContext}
            onCatalog={controller.addComposerCatalog}
            onRemoveContext={controller.removeComposerContext}
            onRemoveCatalog={controller.removeComposerCatalog}
            onPickFile={() => void controller.pickComposerFile()}
            onPickFolder={() => void controller.pickComposerFolder()}
            onSend={() => void controller.enqueueComposer()}
            stopping={stopping}
            onStop={stopTurn ? () => void stopResponse() : undefined}
            onClear={() => void controller.clearComposer()}
          />
        </main>

        <aside id="review-inspector-panel" className={`inspector drawer ${inspectorOpen ? "drawer-open" : ""}`} aria-label="Workspace inspector" aria-hidden={!inspectorOpen} onKeyDown={(event) => { if (event.key === "Escape") { event.preventDefault(); panels.closeInspector(); } }}>
          <div
            className="inspector-resize-handle"
            role="separator"
            aria-label="Resize workspace inspector"
            aria-orientation="vertical"
            aria-valuemin={320}
            aria-valuemax={480}
            aria-valuenow={panels.inspectorWidth}
            tabIndex={0}
            onPointerDown={(event) => event.currentTarget.setPointerCapture(event.pointerId)}
            onPointerMove={(event) => { if (event.currentTarget.hasPointerCapture(event.pointerId)) panels.resizeInspectorAt(event.clientX); }}
            onPointerUp={(event) => event.currentTarget.releasePointerCapture(event.pointerId)}
            onKeyDown={(event) => {
              if (event.key === "ArrowLeft") { event.preventDefault(); panels.nudgeInspectorWidth(16); }
              else if (event.key === "ArrowRight") { event.preventDefault(); panels.nudgeInspectorWidth(-16); }
            }}
          />
          <WorkspaceInspector
            activePanel={workspacePanel}
            visible={inspectorOpen}
            review={state.review}
            workspaceReady={Boolean(state.workspace)}
            workspaceLabel={workspacePath ?? "No workspace"}
            threadLabel={state.detail?.thread.title ?? "No active thread"}
            turnLabel={state.detail?.turns.at(-1)?.taskSummary ?? "No active turn"}
            commands={controller.reviewCommands}
            terminalCommands={controller.terminalCommands}
            onPanel={selectWorkspacePanel}
            onReport={(id) => void controller.selectReport(id)}
            onArtifact={(id) => void controller.selectArtifact(id)}
            onClose={() => panels.closeInspector()}
          />
        </aside>
      </div>
    </div>
  );
}
