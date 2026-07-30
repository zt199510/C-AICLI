import { FolderOpen, PanelLeft, PanelLeftClose, PanelRight, RefreshCw, X } from "lucide-react";
import { useEffect, useState } from "react";
import { useShellPanels } from "./app/use-shell-panels";
import { ReviewInspector } from "./ReviewInspector";
import { Composer } from "./Composer";
import { TaskControls } from "./TaskControls";
import { ThreadSidebar } from "./ThreadSidebar";
import { TimelineView } from "./TimelineView";
import { TerminalPanel } from "./TerminalPanel";
import { useDesktopController } from "./use-desktop-controller";

export function App() {
  const bridge = typeof window !== "undefined" ? window.caicli : undefined;
  const controller = useDesktopController(bridge);
  const { state } = controller;
  const [opening, setOpening] = useState(false);
  const [workspaceError, setWorkspaceError] = useState<string | null>(null);
  const [workspaceTool, setWorkspaceTool] = useState<"review" | "terminal">("review");
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

  const workspacePath = state.workspace?.rootPath ?? null;
  const bridgeUnavailable = !bridge;
  const statusMessage = bridgeUnavailable ? "Desktop bridge unavailable" : state.runtime.message;

  return (
    <div className={`app-shell ${leftOpen ? "" : "left-collapsed"} ${inspectorOpen ? "" : "inspector-collapsed"}`}>
      <header className="titlebar">
        <div className="brand" aria-label="C-AICLI Desktop">
          <span className="brand-mark" aria-hidden="true">C</span>
          <span className="brand-copy"><span className="brand-name">C-AICLI</span><span className="brand-edition">Desktop workspace</span></span>
        </div>
        <div className="workspace-title" title={workspacePath ?? "No workspace"}>{workspacePath ?? "No workspace"}</div>
        <div className={`runtime-status runtime-${state.runtime.state}`} role="status" aria-live="polite" aria-atomic="true"><span className="status-dot" aria-hidden="true" /><span>{statusMessage}</span></div>
      </header>

      <div className="workspace-layout">
        <aside id="threads-panel" className={`thread-sidebar drawer ${leftOpen ? "drawer-open" : ""}`} aria-label="Threads panel" aria-hidden={!leftOpen} onKeyDown={(event) => { if (event.key === "Escape") { event.preventDefault(); panels.closeThreads(); } }}>
          <div className="panel-heading"><span>Threads</span><button className="icon-button" type="button" title="Collapse threads" aria-label="Collapse threads" onClick={() => panels.closeThreads()}><PanelLeftClose size={17} aria-hidden="true" /></button></div>
          <ThreadSidebar
            threads={state.threads}
            status={state.threadsStatus}
            error={state.threadsError}
            truncated={state.threadsTruncated}
            selectedThreadId={state.selectedThreadId}
            onSelect={controller.selectThread}
            onCreate={controller.createThread}
            onRename={controller.renameThread}
            onArchive={controller.archiveThread}
          />
        </aside>

        <main className="task-surface">
          <div className="task-toolbar">
            <div className="toolbar-group">{!leftOpen && <button ref={showThreadsTrigger} className="icon-button" type="button" title="Show threads" aria-label="Show threads" aria-controls="threads-panel" aria-expanded={leftOpen} onClick={panels.showThreads}><PanelLeft size={17} aria-hidden="true" /></button>}<span className="thread-heading"><span className="thread-eyebrow">Conversation</span><span className="task-label">{state.detail?.thread.title ?? "Workspace review"}</span></span><span className="refreshing" role="status" aria-live="polite" hidden={!state.refreshing}>Refreshing…</span></div>
            <div className="toolbar-group">
              <button className="command-button" type="button" onClick={() => void openWorkspace()} disabled={opening || state.runtime.state !== "ready"}>{opening ? <RefreshCw className="spin" size={16} aria-hidden="true" /> : <FolderOpen size={16} aria-hidden="true" />}{opening ? "Opening" : "Open workspace"}</button>
              {!inspectorOpen && <button ref={showInspectorTrigger} className="icon-button" type="button" title="Show review inspector" aria-label="Show review inspector" aria-controls="review-inspector-panel" aria-expanded={inspectorOpen} onClick={panels.showInspector}><PanelRight size={17} aria-hidden="true" /></button>}
            </div>
          </div>

          <section className="timeline" aria-label="Workspace timeline">
            {state.runtime.state === "failed" ? (
              <div className="state-card failure-card"><h1>{statusMessage}</h1><p>The desktop runtime is unavailable. Workspace access remains disabled.</p><button className="primary-action" type="button" disabled={!state.runtime.canRestart || bridgeUnavailable} onClick={() => void controller.restartRuntime()}><RefreshCw size={17} aria-hidden="true" /> Restart AppHost</button></div>
            ) : !workspacePath ? (
              <div className="state-card workspace-empty"><div className="state-kicker">Local AI workspace</div><h1>{state.runtime.state === "ready" ? "What should we build?" : state.runtime.message}</h1><p>Open a project to start a focused conversation with auditable tools, approvals, and results.</p>{workspaceError && <p role="alert">{workspaceError}</p>}<button className="primary-action" type="button" onClick={() => void openWorkspace()} disabled={opening || state.runtime.state !== "ready"}><FolderOpen size={17} aria-hidden="true" /> Open workspace</button></div>
            ) : (
              <TimelineView
                detail={state.detail}
                status={state.detailStatus}
                error={state.detailError}
                onLoadMore={controller.loadMore}
                controls={(
                  <TaskControls
                    detail={state.detail}
                    onCancel={controller.cancelTurn}
                    onApproval={controller.resolveApproval}
                    onResume={controller.resumeTurn}
                    onRestart={controller.restartTurn}
                  />
                )}
              />
            )}
          </section>
          <Composer
            draft={controller.composerDraft}
            composer={controller.composer}
            disabledReason={controller.composerDisabledReason}
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
            onClear={() => void controller.clearComposer()}
          />
        </main>

        <aside id="review-inspector-panel" className={`inspector drawer ${inspectorOpen ? "drawer-open" : ""}`} aria-label="Workspace inspector" aria-hidden={!inspectorOpen} onKeyDown={(event) => { if (event.key === "Escape") { event.preventDefault(); panels.closeInspector(); } }}>
          <div className="panel-heading context-heading">
            <div className="context-switcher" role="tablist" aria-label="Workspace tools">
              <button type="button" role="tab" aria-selected={workspaceTool === "review"} onClick={() => setWorkspaceTool("review")}>Review</button>
              <button type="button" role="tab" aria-selected={workspaceTool === "terminal"} onClick={() => setWorkspaceTool("terminal")}>Terminal</button>
            </div>
            <button className="icon-button" type="button" title="Close workspace inspector" aria-label="Close review inspector" onClick={() => panels.closeInspector()}><X size={17} aria-hidden="true" /></button>
          </div>
          <div className="context-panel" role="tabpanel" aria-label={workspaceTool === "review" ? "Review workspace" : "Terminal workspace"}>
            {workspaceTool === "review"
              ? <ReviewInspector review={state.review} workspaceReady={Boolean(state.workspace)} onTab={controller.setReviewTab} onReport={(id) => void controller.selectReport(id)} onArtifact={(id) => void controller.selectArtifact(id)} />
              : <TerminalPanel workspaceReady={Boolean(state.workspace)} />}
          </div>
        </aside>
      </div>
    </div>
  );
}
