import { FolderOpen, PanelLeft, PanelLeftClose, PanelRight, RefreshCw, X } from "lucide-react";
import { useEffect, useRef, useState } from "react";
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
  const [leftOpen, setLeftOpen] = useState(() => window.innerWidth >= 900);
  const [inspectorOpen, setInspectorOpen] = useState(() => window.innerWidth > 1120);
  const showThreadsTrigger = useRef<HTMLButtonElement>(null);
  const showInspectorTrigger = useRef<HTMLButtonElement>(null);
  const restoreThreadsFocus = useRef(false);
  const restoreInspectorFocus = useRef(false);

  useEffect(() => {
    const updateLayout = () => {
      if (window.innerWidth < 900) { setLeftOpen(false); setInspectorOpen(false); }
      else if (window.innerWidth <= 1120) { setLeftOpen(true); setInspectorOpen(false); }
      else { setLeftOpen(true); setInspectorOpen(true); }
    };
    window.addEventListener("resize", updateLayout);
    return () => window.removeEventListener("resize", updateLayout);
  }, []);

  useEffect(() => {
    if (!leftOpen && restoreThreadsFocus.current) {
      restoreThreadsFocus.current = false;
      showThreadsTrigger.current?.focus();
    }
  }, [leftOpen]);

  useEffect(() => {
    if (!inspectorOpen && restoreInspectorFocus.current) {
      restoreInspectorFocus.current = false;
      showInspectorTrigger.current?.focus();
    }
  }, [inspectorOpen]);

  function showThreads() { setLeftOpen(true); if (window.innerWidth < 900) setInspectorOpen(false); }
  function showInspector() { setInspectorOpen(true); if (window.innerWidth < 900) setLeftOpen(false); }
  function closeThreads(restoreFocus = true) {
    restoreThreadsFocus.current = restoreFocus;
    setLeftOpen(false);
  }
  function closeInspector(restoreFocus = true) {
    restoreInspectorFocus.current = restoreFocus;
    setInspectorOpen(false);
  }

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
        <div className="brand">C-AICLI Desktop</div>
        <div className="workspace-title" title={workspacePath ?? "No workspace"}>{workspacePath ?? "No workspace"}</div>
        <div className={`runtime-status runtime-${state.runtime.state}`} role="status" aria-live="polite" aria-atomic="true"><span className="status-dot" aria-hidden="true" /><span>{statusMessage}</span></div>
      </header>

      <div className="workspace-layout">
        <aside id="threads-panel" className={`thread-sidebar drawer ${leftOpen ? "drawer-open" : ""}`} aria-label="Threads panel" aria-hidden={!leftOpen} onKeyDown={(event) => { if (event.key === "Escape") { event.preventDefault(); closeThreads(); } }}>
          <div className="panel-heading"><span>Threads</span><button className="icon-button" type="button" title="Collapse threads" aria-label="Collapse threads" onClick={() => closeThreads()}><PanelLeftClose size={17} aria-hidden="true" /></button></div>
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
            <div className="toolbar-group">{!leftOpen && <button ref={showThreadsTrigger} className="icon-button" type="button" title="Show threads" aria-label="Show threads" aria-controls="threads-panel" aria-expanded={leftOpen} onClick={showThreads}><PanelLeft size={17} aria-hidden="true" /></button>}<span className="task-label">{state.detail?.thread.title ?? "Workspace review"}</span><span className="refreshing" role="status" aria-live="polite" hidden={!state.refreshing}>Refreshing…</span></div>
            <div className="toolbar-group">
              <button className="command-button" type="button" onClick={() => void openWorkspace()} disabled={opening || state.runtime.state !== "ready"}>{opening ? <RefreshCw className="spin" size={16} aria-hidden="true" /> : <FolderOpen size={16} aria-hidden="true" />}{opening ? "Opening" : "Open workspace"}</button>
              {!inspectorOpen && <button ref={showInspectorTrigger} className="icon-button" type="button" title="Show review inspector" aria-label="Show review inspector" aria-controls="review-inspector-panel" aria-expanded={inspectorOpen} onClick={showInspector}><PanelRight size={17} aria-hidden="true" /></button>}
            </div>
          </div>

          <section className="timeline" aria-label="Workspace timeline">
            {state.runtime.state === "failed" ? (
              <div className="state-card failure-card"><h1>{statusMessage}</h1><p>The desktop runtime is unavailable. Workspace access remains disabled.</p><button className="primary-action" type="button" disabled={!state.runtime.canRestart || bridgeUnavailable} onClick={() => void controller.restartRuntime()}><RefreshCw size={17} aria-hidden="true" /> Restart AppHost</button></div>
            ) : !workspacePath ? (
              <div className="state-card workspace-empty"><h1>{state.runtime.state === "ready" ? "Open a workspace" : state.runtime.message}</h1>{workspaceError && <p role="alert">{workspaceError}</p>}<button className="primary-action" type="button" onClick={() => void openWorkspace()} disabled={opening || state.runtime.state !== "ready"}><FolderOpen size={17} aria-hidden="true" /> Open workspace</button></div>
            ) : (
              <TimelineView detail={state.detail} status={state.detailStatus} error={state.detailError} onLoadMore={controller.loadMore} />
            )}
          </section>
          <TerminalPanel workspaceReady={Boolean(state.workspace)} />
          <TaskControls
            detail={state.detail}
            onCancel={controller.cancelTurn}
            onApproval={controller.resolveApproval}
            onResume={controller.resumeTurn}
            onRestart={controller.restartTurn}
          />
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

        <aside id="review-inspector-panel" className={`inspector drawer ${inspectorOpen ? "drawer-open" : ""}`} aria-label="Review inspector" aria-hidden={!inspectorOpen} onKeyDown={(event) => { if (event.key === "Escape") { event.preventDefault(); closeInspector(); } }}>
          <div className="panel-heading"><span>Review</span><button className="icon-button" type="button" title="Close review inspector" aria-label="Close review inspector" onClick={() => closeInspector()}><X size={17} aria-hidden="true" /></button></div>
          <ReviewInspector review={state.review} workspaceReady={Boolean(state.workspace)} onTab={controller.setReviewTab} onReport={(id) => void controller.selectReport(id)} onArtifact={(id) => void controller.selectArtifact(id)} />
        </aside>
      </div>
    </div>
  );
}
