import { FolderOpen, PanelLeftClose, RefreshCw } from "lucide-react";
import { useEffect, useState } from "react";
import type { InitializeResult, WorkspaceOpenResult } from "../generated/desktop-contracts";
import type { RuntimeStatus } from "../shared/bridge-contract";

const initialStatus: RuntimeStatus = { state: "starting", detail: "Starting AppHost" };

export function App() {
  const [runtime, setRuntime] = useState<RuntimeStatus>(initialStatus);
  const [initialization, setInitialization] = useState<InitializeResult | null>(null);
  const [workspace, setWorkspace] = useState<WorkspaceOpenResult | null>(null);
  const [opening, setOpening] = useState(false);

  useEffect(() => {
    if (!window.caicli) {
      setRuntime({ state: "failed", detail: "Desktop bridge unavailable" });
      return;
    }

    const unsubscribe = window.caicli.onRuntimeStatus(setRuntime);
    window.caicli
      .initialize()
      .then((result) => {
        setInitialization(result);
        setRuntime({ state: "ready", detail: result.protocolVersion });
      })
      .catch(() => setRuntime({ state: "failed", detail: "AppHost unavailable" }));
    return unsubscribe;
  }, []);

  async function openWorkspace() {
    if (!window.caicli) return;
    setOpening(true);
    try {
      const selected = await window.caicli.openWorkspace();
      if (selected) setWorkspace(selected);
    } finally {
      setOpening(false);
    }
  }

  return (
    <div className="app-shell">
      <header className="titlebar">
        <div className="brand">C-AICLI Desktop</div>
        <div className="workspace-title">{workspace?.data?.rootPath ?? "No workspace"}</div>
        <div className={`runtime-status runtime-${runtime.state}`}>
          <span className="status-dot" aria-hidden="true" />
          {runtime.detail}
        </div>
      </header>

      <div className="workspace-layout">
        <aside className="thread-sidebar">
          <div className="panel-heading">
            <span>Threads</span>
            <button className="icon-button" type="button" title="Collapse threads" disabled>
              <PanelLeftClose size={16} aria-hidden="true" />
              <span className="sr-only">Collapse threads</span>
            </button>
          </div>
          <div className="empty-list">No threads</div>
        </aside>

        <main className="task-surface">
          <div className="task-toolbar">
            <span className="task-label">Workspace</span>
            <button
              className="command-button"
              type="button"
              onClick={openWorkspace}
              disabled={opening || runtime.state !== "ready"}
            >
              {opening ? <RefreshCw className="spin" size={16} aria-hidden="true" /> : <FolderOpen size={16} aria-hidden="true" />}
              {opening ? "Opening" : "Open workspace"}
            </button>
          </div>

          <section className="timeline" aria-label="Task timeline">
            {workspace?.succeeded && workspace.data ? (
              <div className="workspace-ready">
                <FolderOpen size={28} aria-hidden="true" />
                <h1>{workspace.data.rootPath}</h1>
                <p>{workspace.data.workspaceId}</p>
              </div>
            ) : (
              <div className="workspace-empty">
                <h1>Open a workspace</h1>
                <button
                  className="primary-action"
                  type="button"
                  onClick={openWorkspace}
                  disabled={opening || runtime.state !== "ready"}
                >
                  <FolderOpen size={17} aria-hidden="true" />
                  Open workspace
                </button>
              </div>
            )}
          </section>
        </main>

        <aside className="inspector">
          <div className="panel-heading">Runtime</div>
          <dl className="runtime-details">
            <dt>State</dt>
            <dd>{runtime.state}</dd>
            <dt>Protocol</dt>
            <dd>{initialization?.protocolVersion ?? "-"}</dd>
            <dt>Transport</dt>
            <dd>{initialization?.security.transport ?? "-"}</dd>
            <dt>Node access</dt>
            <dd>{initialization?.security.rendererNodeAccess ? "enabled" : "disabled"}</dd>
          </dl>
        </aside>
      </div>
    </div>
  );
}
