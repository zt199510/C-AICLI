import { RefreshCw, RotateCcw, ShieldCheck, ShieldX, Square } from "lucide-react";
import { useMemo, useState } from "react";
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
  const turn = useMemo(() => activeTurn(props.detail), [props.detail]);
  const recovery = useMemo(() => recoveryTurn(props.detail), [props.detail]);
  const approval = turn?.approval ?? recovery?.approval ?? null;
  const canCancel = turn && !terminal.has(turn.status) && turn.status !== "canceling";

  if (!turn && !recovery) return null;

  async function run(key: string, action: () => Promise<string | null>) {
    setBusy(key);
    setMessage(null);
    try {
      const result = await action();
      setMessage(result);
    } finally {
      setBusy(null);
    }
  }

  return (
    <section className="task-controls" aria-label="Task controls">
      <div className="task-control-row">
        <div className="task-control-summary">
          <strong>{turn ? `Turn ${turn.ordinal}` : "Recovery"}</strong>
          <span className={`status-chip status-${(turn ?? recovery)?.status.toLowerCase()}`}>{(turn ?? recovery)?.status}</span>
        </div>
        {canCancel && (
          <button className="command-button danger-command" type="button" disabled={busy !== null} onClick={() => void run("cancel", () => props.onCancel(turn.turnId, turn.revision))}>
            <Square size={14} aria-hidden="true" />{busy === "cancel" ? "Canceling" : "Cancel"}
          </button>
        )}
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

      {recovery && (
        <div className="recovery-actions" role="alert">
          <span>This turn needs recovery before it can continue.</span>
          <button className="command-button" type="button" disabled={busy !== null} onClick={() => void run("resume", () => props.onResume(recovery.turnId, recovery.revision))}>
            <RefreshCw size={15} aria-hidden="true" />Resume
          </button>
          <button className="command-button danger-command" type="button" disabled={busy !== null} onClick={() => {
            if (window.confirm("Restart this turn from its original queued input?")) void run("restart", () => props.onRestart(recovery.turnId, recovery.revision));
          }}>
            <RotateCcw size={15} aria-hidden="true" />Restart
          </button>
        </div>
      )}

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
