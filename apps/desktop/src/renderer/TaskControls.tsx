import { RefreshCw, RotateCcw, ShieldCheck, ShieldX, Square } from "lucide-react";
import { useMemo, useRef, useState } from "react";
import type { ThreadDetailData, TurnSummaryData } from "../generated/desktop-contracts";

export interface TaskControlsProps {
  readonly detail: ThreadDetailData | null;
  readonly onCancel: (turnId: string, turnRevision: number) => Promise<string | null>;
  readonly onApproval: (turnId: string, requestId: string, approvalRevision: number, turnRevision: number, decision: "approve" | "deny") => Promise<string | null>;
  readonly onResume: (turnId: string, turnRevision: number) => Promise<string | null>;
  readonly onRestart: (turnId: string, turnRevision: number) => Promise<string | null>;
}

const terminal = new Set(["completed", "failed", "canceled"]);

export function TaskControls(props: TaskControlsProps) {
  const [busy, setBusy] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [confirmRestart, setConfirmRestart] = useState(false);
  const restartTrigger = useRef<HTMLButtonElement>(null);
  const turn = useMemo(() => activeTurn(props.detail), [props.detail]);
  const recovery = useMemo(() => recoveryTurn(props.detail), [props.detail]);
  const current = turn ?? recovery;
  const approval = turn?.approval ?? recovery?.approval ?? null;
  const canCancel = Boolean(turn && !terminal.has(turn.status) && turn.status !== "canceling");

  async function run(key: string, action: () => Promise<string | null>) {
    setBusy(key);
    setMessage(null);
    try {
      const result = await action();
      setMessage(result);
      if (!result) window.requestAnimationFrame(() => document.querySelector<HTMLTextAreaElement>('[aria-label="Composer prompt"]')?.focus());
    } finally {
      setBusy(null);
    }
  }

  return (
    <section className="task-controls" aria-label="Task controls" hidden={!current}>
      <div className="task-control-row">
        <div className="task-control-summary">
          <strong>{turn ? `Turn ${turn.ordinal}` : recovery ? "Recovery" : "No active turn"}</strong>
          <span className={`status-chip status-${current?.status.toLowerCase() ?? "idle"}`}>{current?.status ?? "idle"}</span>
        </div>
          <button className="command-button danger-command" type="button" hidden={!canCancel} disabled={busy !== null || !turn} onClick={() => { if (turn) void run("cancel", () => props.onCancel(turn.turnId, turn.revision)); }}>
            <Square size={14} aria-hidden="true" />{busy === "cancel" ? "Canceling" : "Cancel"}
          </button>
      </div>

      {approval && (
        <div className="approval-panel" role="group" aria-label="Approval request">
          <div className="approval-copy">
            <strong>{approval.safeSummary}</strong>
            <span>{approval.operation} · {approval.risk} · {approval.targetClass}</span>
          </div>
          <div className="approval-actions">
            <button className="command-button" type="button" disabled={busy !== null} onClick={() => void run("approve", () => props.onApproval(approval.turnId, approval.requestId, approval.approvalRevision, approval.turnRevision, "approve"))}>
              <ShieldCheck size={15} aria-hidden="true" />Approve
            </button>
            <button className="command-button danger-command" type="button" disabled={busy !== null} onClick={() => void run("deny", () => props.onApproval(approval.turnId, approval.requestId, approval.approvalRevision, approval.turnRevision, "deny"))}>
              <ShieldX size={15} aria-hidden="true" />Deny
            </button>
          </div>
        </div>
      )}

        <div className="recovery-actions" role="alert" hidden={!recovery}>
          <span>This turn needs recovery before it can continue.</span>
          <button className="command-button" type="button" disabled={busy !== null || !recovery} onClick={() => { if (recovery) void run("resume", () => props.onResume(recovery.turnId, recovery.revision)); }}>
            <RefreshCw size={15} aria-hidden="true" />Resume
          </button>
          <button ref={restartTrigger} className="command-button danger-command" type="button" disabled={busy !== null || !recovery} onClick={() => { if (recovery) setConfirmRestart(true); }}>
            <RotateCcw size={15} aria-hidden="true" />Restart
          </button>
        </div>

      {confirmRestart && recovery && <div className="restart-confirm" role="alertdialog" aria-modal="true" aria-labelledby="restart-confirm-title" aria-describedby="restart-confirm-description" onKeyDown={(event) => { if (event.key === "Escape") { event.preventDefault(); setConfirmRestart(false); window.requestAnimationFrame(() => restartTrigger.current?.focus()); } }}>
        <strong id="restart-confirm-title">Restart this turn?</strong>
        <span id="restart-confirm-description">The original queued input starts a new attempt. Completed writes are not replayed.</span>
        <div className="form-actions"><button autoFocus className="command-button danger-command" type="button" onClick={() => { setConfirmRestart(false); void run("restart", () => props.onRestart(recovery.turnId, recovery.revision)); }}>Restart turn</button><button className="command-button" type="button" onClick={() => { setConfirmRestart(false); window.requestAnimationFrame(() => restartTrigger.current?.focus()); }}>Cancel</button></div>
      </div>}

      {message && <div className="inline-error" role="alert">{message}</div>}
    </section>
  );
}

function activeTurn(detail: ThreadDetailData | null): TurnSummaryData | null {
  if (!detail) return null;
  const byThread = detail.thread.activeTurnId ? detail.turns.find(turn => turn.turnId === detail.thread.activeTurnId) : null;
  if (byThread) return byThread;
  return [...detail.turns].reverse().find(turn => !terminal.has(turn.status)) ?? null;
}

function recoveryTurn(detail: ThreadDetailData | null): TurnSummaryData | null {
  if (!detail) return null;
  return [...detail.turns].reverse().find(turn => turn.recoveryRequired) ?? null;
}
