import { Copy, RefreshCw, RotateCcw } from "lucide-react";
import { useRef, useState } from "react";
import type { AssistantMessageBlock, RecoveryBlock } from "./conversation-block-projector";

export function RetryExhaustedActions({
  block,
  onRestart,
}: {
  readonly block: AssistantMessageBlock;
  readonly onRestart?: (turnId: string, turnRevision: number) => Promise<string | null>;
}) {
  const [busy, setBusy] = useState<"retry" | "copy" | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const safeError = block.turn.provider.safeErrorMessage ??
    block.turn.errorCode ??
    "The model connection failed after the retry limit.";

  async function retry() {
    if (!onRestart) return;
    setBusy("retry");
    setMessage(null);
    try {
      setMessage(await onRestart(block.turnId, block.turn.revision));
    } finally {
      setBusy(null);
    }
  }

  async function copy() {
    setBusy("copy");
    setMessage(null);
    try {
      await navigator.clipboard.writeText(safeError);
      setMessage("错误信息已复制");
    } catch {
      setMessage("无法复制错误信息");
    } finally {
      setBusy(null);
    }
  }

  return (
    <div className="assistant-recovery-actions" aria-label="连接失败操作">
      <button className="command-button" type="button" disabled={busy !== null || !onRestart} onClick={() => void retry()}>
        <RefreshCw size={14} aria-hidden="true" />{busy === "retry" ? "正在提交" : "重新尝试"}
      </button>
      <button className="command-button" type="button" disabled={busy !== null} onClick={() => void copy()}>
        <Copy size={14} aria-hidden="true" />复制错误信息
      </button>
      {message ? <span className="inline-action-message" role="status">{message}</span> : null}
    </div>
  );
}

export function RecoveryActions({
  block,
  onResume,
  onRestart,
}: {
  readonly block: RecoveryBlock;
  readonly onResume?: (turnId: string, turnRevision: number) => Promise<string | null>;
  readonly onRestart?: (turnId: string, turnRevision: number) => Promise<string | null>;
}) {
  const [busy, setBusy] = useState<"resume" | "restart" | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);
  const restartTrigger = useRef<HTMLButtonElement>(null);

  async function run(kind: "resume" | "restart") {
    const action = kind === "resume" ? onResume : onRestart;
    if (!action) return;
    setBusy(kind);
    setMessage(null);
    try {
      setMessage(await action(block.turnId, block.turn.revision));
    } finally {
      setBusy(null);
    }
  }

  return (
    <section className="recovery-block" aria-label="执行恢复">
      <strong>执行已中断</strong>
      <p>{block.summary}</p>
      <div className="recovery-action-row">
        {onResume ? <button className="command-button" type="button" disabled={busy !== null} onClick={() => void run("resume")}><RefreshCw size={14} aria-hidden="true" />{busy === "resume" ? "正在继续" : "继续"}</button> : null}
        {onRestart ? <button ref={restartTrigger} className="command-button" type="button" disabled={busy !== null} onClick={() => setConfirming(true)}><RotateCcw size={14} aria-hidden="true" />重新开始</button> : null}
      </div>
      {confirming ? (
        <div className="restart-confirm" role="alertdialog" aria-modal="true" aria-labelledby={`restart-${block.turnId}`} onKeyDown={(event) => {
          if (event.key === "Escape") {
            event.preventDefault();
            setConfirming(false);
            window.requestAnimationFrame(() => restartTrigger.current?.focus());
          }
        }}>
          <strong id={`restart-${block.turnId}`}>重新开始此 Turn？</strong>
          <span>会创建新的 attempt；已完成的写入和工具副作用不会自动重放。</span>
          <div className="form-actions">
            <button autoFocus className="command-button danger-command" type="button" onClick={() => {
              setConfirming(false);
              void run("restart");
            }}>确认重新开始</button>
            <button className="command-button" type="button" onClick={() => {
              setConfirming(false);
              window.requestAnimationFrame(() => restartTrigger.current?.focus());
            }}>取消</button>
          </div>
        </div>
      ) : null}
      {message ? <div className="inline-error" role="alert">{message}</div> : null}
    </section>
  );
}
