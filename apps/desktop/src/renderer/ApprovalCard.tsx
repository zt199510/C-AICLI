import { ShieldCheck, ShieldX } from "lucide-react";
import { useState } from "react";
import type { ApprovalBlock } from "./conversation-block-projector";

export function ApprovalCard({
  block,
  onApproval,
}: {
  readonly block: ApprovalBlock;
  readonly onApproval?: (
    turnId: string,
    requestId: string,
    approvalRevision: number,
    turnRevision: number,
    decision: "approve" | "deny",
  ) => Promise<string | null>;
}) {
  const [busy, setBusy] = useState<"approve" | "deny" | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  if (block.state === "resolved" || !block.approval) {
    return (
      <section className="approval-card approval-resolved" aria-label="审批结果">
        <strong>{resolvedLabel(block.summary)}</strong>
        <span>{block.summary}</span>
      </section>
    );
  }

  const approval = block.approval;
  async function decide(decision: "approve" | "deny") {
    if (!onApproval) return;
    setBusy(decision);
    setMessage(null);
    try {
      setMessage(await onApproval(
        approval.turnId,
        approval.requestId,
        approval.approvalRevision,
        approval.turnRevision,
        decision,
      ));
    } finally {
      setBusy(null);
    }
  }

  return (
    <section className="approval-card" aria-label="需要批准">
      <div className="approval-card-copy">
        <strong>需要批准</strong>
        <p>{approval.safeSummary}</p>
        <span>{approval.operation} · {approval.risk} · {approval.targetClass}</span>
      </div>
      <div className="approval-actions" role="group" aria-label="审批操作">
        <button className="command-button danger-command" type="button" disabled={busy !== null || !onApproval} onClick={() => void decide("deny")}><ShieldX size={15} aria-hidden="true" />{busy === "deny" ? "正在拒绝" : "拒绝"}</button>
        <button className="command-button approval-primary" type="button" disabled={busy !== null || !onApproval} onClick={() => void decide("approve")}><ShieldCheck size={15} aria-hidden="true" />{busy === "approve" ? "正在批准" : "批准"}</button>
      </div>
      {message ? <div className="inline-error" role="alert">{message}</div> : null}
    </section>
  );
}

function resolvedLabel(summary: string): string {
  const normalized = summary.toLowerCase();
  if (normalized.includes("deny") || normalized.includes("拒绝")) return "已拒绝";
  if (normalized.includes("stale") || normalized.includes("失效")) return "请求已失效";
  if (normalized.includes("expired") || normalized.includes("过期")) return "请求已过期";
  return "已批准";
}
