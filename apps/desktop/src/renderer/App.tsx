import { FolderOpen, PanelLeft, PanelLeftClose, PanelRight, RefreshCw, X } from "lucide-react";
import { useEffect, useState } from "react";
import { ReviewInspector } from "./ReviewInspector";
import { ThreadSidebar } from "./ThreadSidebar";
import { TimelineView } from "./TimelineView";
import { useDesktopController } from "./use-desktop-controller";

export function App() {
  const bridge = typeof window !== "undefined" ? window.caicli : undefined;
  const controller = useDesktopController(bridge);
  const { state } = controller;
  const [opening, setOpening] = useState(false);
  const [workspaceError, setWorkspaceError] = useState<string | null>(null);
  const [leftOpen, setLeftOpen] = useState(() => window.innerWidth >= 900);
  const [inspectorOpen, setInspectorOpen] = useState(() => window.innerWidth > 1120);

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
    if (state.workspace) controller.setReviewTab("changes");
  }, [state.workspace?.workspaceId]);

  function showThreads() { setLeftOpen(true); if (window.innerWidth < 900) setInspectorOpen(false); }
  function showInspector() { setInspectorOpen(true); if (window.innerWidth < 900) setLeftOpen(false); }

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
        <div className={`runtime-status runtime-${state.runtime.state}`} aria-live="polite"><span className="status-dot" aria-hidden="true" /><span>{statusMessage}</span></div>
      </header>

      <div className="workspace-layout">
        <aside className={`thread-sidebar drawer ${leftOpen ? "drawer-open" : ""}`} aria-label="Threads panel">
          <div className="panel-heading"><span>Threads</span><button className="icon-button" type="button" title="Collapse threads" aria-label="Collapse threads" onClick={() => setLeftOpen(false)}><PanelLeftClose size={17} aria-hidden="true" /></button></div>
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
            <div className="toolbar-group">{!leftOpen && <button className="icon-button" type="button" title="Show threads" aria-label="Show threads" onClick={showThreads}><PanelLeft size={17} aria-hidden="true" /></button>}<span className="task-label">{state.detail?.thread.title ?? "Workspace review"}</span>{state.refreshing && <span className="refreshing" aria-live="polite">Refreshing…</span>}</div>
            <div className="toolbar-group">
              <button className="command-button" type="button" onClick={() => void openWorkspace()} disabled={opening || state.runtime.state !== "ready"}>{opening ? <RefreshCw className="spin" size={16} aria-hidden="true" /> : <FolderOpen size={16} aria-hidden="true" />}{opening ? "Opening" : "Open workspace"}</button>
              {!inspectorOpen && <button className="icon-button" type="button" title="Show review inspector" aria-label="Show review inspector" onClick={showInspector}><PanelRight size={17} aria-hidden="true" /></button>}
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
        </main>

        <aside className={`inspector drawer ${inspectorOpen ? "drawer-open" : ""}`} aria-label="Review inspector">
          <div className="panel-heading"><span>Review</span><button className="icon-button" type="button" title="Close review inspector" aria-label="Close review inspector" onClick={() => setInspectorOpen(false)}><X size={17} aria-hidden="true" /></button></div>
          <ReviewInspector review={state.review} workspaceReady={Boolean(state.workspace)} onTab={controller.setReviewTab} onReport={(id) => void controller.selectReport(id)} onArtifact={(id) => void controller.selectArtifact(id)} />
        </aside>
      </div>
    </div>
  );
}
