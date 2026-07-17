import { FolderOpen, PanelLeft, PanelLeftClose, PanelRight, RefreshCw, X } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import type { WorkspaceOpenResult } from "../generated/desktop-contracts";
import { createRuntimeStatus, type RuntimeStatus } from "../shared/bridge-contract";

export function App() {
  const [runtime, setRuntime] = useState<RuntimeStatus>(() => createRuntimeStatus("runtime-starting"));
  const [workspace, setWorkspace] = useState<WorkspaceOpenResult | null>(null);
  const [opening, setOpening] = useState(false);
  const [bridgeUnavailable, setBridgeUnavailable] = useState(false);
  const [leftOpen, setLeftOpen] = useState(() => window.innerWidth >= 900);
  const [inspectorOpen, setInspectorOpen] = useState(() => window.innerWidth > 1120);
  const receivedEvent = useRef(false);

  useEffect(() => {
    if (!window.caicli) {
      setBridgeUnavailable(true);
      setRuntime(createRuntimeStatus("apphost-start-failed"));
      return;
    }
    const unsubscribe = window.caicli.onRuntimeStatus((status) => {
      receivedEvent.current = true;
      setRuntime(status);
      if (status.state !== "ready") setWorkspace(null);
    });
    void window.caicli.getRuntimeStatus()
      .then((status) => { if (!receivedEvent.current) setRuntime(status); })
      .catch(() => {
        setBridgeUnavailable(true);
        setRuntime(createRuntimeStatus("apphost-start-failed"));
      });
    return unsubscribe;
  }, []);

  useEffect(() => {
    const updateLayout = () => {
      if (window.innerWidth < 900) {
        setLeftOpen(false);
        setInspectorOpen(false);
      } else if (window.innerWidth <= 1120) {
        setLeftOpen(true);
        setInspectorOpen(false);
      } else {
        setLeftOpen(true);
        setInspectorOpen(true);
      }
    };
    window.addEventListener("resize", updateLayout);
    return () => window.removeEventListener("resize", updateLayout);
  }, []);

  function showThreads() {
    setLeftOpen(true);
    if (window.innerWidth < 900) setInspectorOpen(false);
  }

  function showInspector() {
    setInspectorOpen(true);
    if (window.innerWidth < 900) setLeftOpen(false);
  }

  async function openWorkspace() {
    if (!window.caicli || runtime.state !== "ready") return;
    setOpening(true);
    try {
      const selected = await window.caicli.openWorkspace();
      if (selected) setWorkspace(selected);
    } catch {
      setWorkspace(null);
    } finally {
      setOpening(false);
    }
  }

  async function restartRuntime() {
    if (!window.caicli || !runtime.canRestart) return;
    setWorkspace(null);
    setRuntime(createRuntimeStatus("runtime-restarting"));
    try { setRuntime(await window.caicli.restartRuntime()); }
    catch { setRuntime(createRuntimeStatus("restart-failed")); }
  }

  const workspacePath = workspace?.succeeded ? workspace.data?.rootPath : null;
  const workspaceError = workspace && !workspace.succeeded ? workspace.error?.safeMessage : null;
  const statusMessage = bridgeUnavailable ? "Desktop bridge unavailable" : runtime.message;

  return (
    <div className={`app-shell ${leftOpen ? "" : "left-collapsed"} ${inspectorOpen ? "" : "inspector-collapsed"}`}>
      <header className="titlebar">
        <div className="brand">C-AICLI Desktop</div>
        <div className="workspace-title" title={workspacePath ?? "No workspace"}>{workspacePath ?? "No workspace"}</div>
        <div className={`runtime-status runtime-${runtime.state}`} aria-live="polite">
          <span className="status-dot" aria-hidden="true" />
          <span>{statusMessage}</span>
        </div>
      </header>

      <div className="workspace-layout">
        <aside className={`thread-sidebar drawer ${leftOpen ? "drawer-open" : ""}`} aria-label="Threads panel">
          <div className="panel-heading">
            <span>Threads</span>
            <button className="icon-button" type="button" title="Collapse threads" aria-label="Collapse threads" onClick={() => setLeftOpen(false)}>
              <PanelLeftClose size={17} aria-hidden="true" />
            </button>
          </div>
          <div className="empty-list">No threads</div>
        </aside>

        <main className="task-surface">
          <div className="task-toolbar">
            <div className="toolbar-group">
              {!leftOpen && <button className="icon-button" type="button" title="Show threads" aria-label="Show threads" onClick={showThreads}><PanelLeft size={17} aria-hidden="true" /></button>}
              <span className="task-label">Workspace</span>
            </div>
            <div className="toolbar-group">
              <button className="command-button" type="button" onClick={openWorkspace} disabled={opening || runtime.state !== "ready"}>
                {opening ? <RefreshCw className="spin" size={16} aria-hidden="true" /> : <FolderOpen size={16} aria-hidden="true" />}
                {opening ? "Opening" : "Open workspace"}
              </button>
              {!inspectorOpen && <button className="icon-button" type="button" title="Show runtime inspector" aria-label="Show runtime inspector" onClick={showInspector}><PanelRight size={17} aria-hidden="true" /></button>}
            </div>
          </div>

          <section className="timeline" aria-label="Workspace">
            {runtime.state === "failed" ? (
              <div className="state-card failure-card">
                <h1>{statusMessage}</h1>
                <p>The desktop runtime is unavailable. Workspace access remains disabled.</p>
                <button className="primary-action" type="button" disabled={!runtime.canRestart || bridgeUnavailable} onClick={restartRuntime}>
                  <RefreshCw size={17} aria-hidden="true" /> Restart AppHost
                </button>
              </div>
            ) : workspacePath ? (
              <div className="state-card workspace-ready">
                <FolderOpen size={28} aria-hidden="true" />
                <h1>{workspacePath}</h1>
                <p>{workspace?.data?.workspaceId}</p>
              </div>
            ) : (
              <div className="state-card workspace-empty">
                <h1>{runtime.state === "ready" ? "Open a workspace" : runtime.message}</h1>
                {workspaceError && <p role="alert">{workspaceError}</p>}
                <button className="primary-action" type="button" onClick={openWorkspace} disabled={opening || runtime.state !== "ready"}>
                  <FolderOpen size={17} aria-hidden="true" /> Open workspace
                </button>
              </div>
            )}
          </section>
        </main>

        <aside className={`inspector drawer ${inspectorOpen ? "drawer-open" : ""}`} aria-label="Runtime inspector">
          <div className="panel-heading">
            <span>Runtime</span>
            <button className="icon-button" type="button" title="Close runtime inspector" aria-label="Close runtime inspector" onClick={() => setInspectorOpen(false)}><X size={17} aria-hidden="true" /></button>
          </div>
          <dl className="runtime-details">
            <dt>State</dt><dd>{runtime.state}</dd>
            <dt>Code</dt><dd>{runtime.code}</dd>
            <dt>Protocol</dt><dd>{runtime.protocolVersion ?? "-"}</dd>
            <dt>Restart</dt><dd>{runtime.canRestart ? "available" : "unavailable"}</dd>
          </dl>
        </aside>
      </div>
    </div>
  );
}
