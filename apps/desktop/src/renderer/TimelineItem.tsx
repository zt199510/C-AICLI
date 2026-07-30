import {
  AlertTriangle,
  Bot,
  CheckCircle2,
  CircleDot,
  FileClock,
  GitCompare,
  Hammer,
  MessageSquare,
  ShieldQuestion,
  TerminalSquare,
  UserRound,
} from "lucide-react";
import type { ComponentType } from "react";
import type { TimelineItemData } from "../generated/desktop-contracts";

const presentations: Record<string, { label: string; icon: ComponentType<{ size?: number; "aria-hidden"?: boolean }> }> = {
  "user.message": { label: "User message", icon: UserRound },
  "assistant.message": { label: "Assistant message", icon: Bot },
  "assistant.final": { label: "Assistant result", icon: Bot },
  "plan.updated": { label: "Plan updated", icon: FileClock },
  "tool.started": { label: "Tool started", icon: Hammer },
  "tool.completed": { label: "Tool completed", icon: Hammer },
  "command.started": { label: "Command started", icon: TerminalSquare },
  "command.completed": { label: "Command completed", icon: TerminalSquare },
  "verification.completed": { label: "Verification completed", icon: CheckCircle2 },
  "approval.requested": { label: "Approval requested", icon: ShieldQuestion },
  "approval.resolved": { label: "Approval resolved", icon: CheckCircle2 },
  "changes.updated": { label: "Changes updated", icon: GitCompare },
  "report.available": { label: "Report available", icon: FileClock },
  "artifact.available": { label: "Artifact available", icon: CircleDot },
  "warning.raised": { label: "Warning", icon: AlertTriangle },
  "turn.completed": { label: "Turn completed", icon: CheckCircle2 },
};

export function TimelineItem({
  item,
  projectionKind,
}: {
  item: TimelineItemData;
  projectionKind?: string;
}) {
  const presentation = presentations[item.type] ?? { label: "Unrecognized audit event", icon: MessageSquare };
  const Icon = presentation.icon;
  const message = isMessage(item.type);
  const visibleText = message && item.payload.text ? item.payload.text : item.summary;
  if (message) {
    const user = item.type === "user.message";
    return (
      <article
        className={`conversation-message ${messageRole(item.type)} ${item.redacted ? "redacted" : ""}`}
        data-projection-kind={projectionKind}
        data-sequence={item.sequence}
      >
        <div className="conversation-message-identity" aria-hidden="true">{user ? "Y" : "C"}</div>
        <div className="conversation-message-copy">
          <header className="conversation-message-meta">
            <span className="sr-only">{presentation.label}</span>
            <strong>{user ? "You" : "C-AICLI"}</strong>
            <time dateTime={item.timestampUtc}>{formatTime(item.timestampUtc)}</time>
          </header>
          <div className="conversation-message-content">{item.redacted ? "Content redacted" : visibleText}</div>
          {item.source && <div className="source-pointer">Source: {item.source.kind} · {item.source.sourceId} · {item.source.availability}</div>}
        </div>
      </article>
    );
  }
  return (
    <article
      className={`timeline-card timeline-${category(item.type)} ${messageRole(item.type)} ${item.redacted ? "redacted" : ""}`}
      data-projection-kind={projectionKind}
      data-sequence={item.sequence}
    >
      <header>
        <span className="timeline-icon"><Icon size={16} aria-hidden={true} /></span>
        <span className="timeline-type">{presentation.label}</span>
        <span className={`status-chip status-${safeToken(item.status)}`}>{item.status}</span>
        <time dateTime={item.timestampUtc}>{formatTime(item.timestampUtc)}</time>
      </header>
      <p className="timeline-summary">{item.redacted ? "Content redacted" : visibleText}</p>
      {!presentations[item.type] && <p className="audit-fallback-type">Type: {item.type || "(empty)"}</p>}
      {!item.redacted && !message && hasPayload(item) && (
        <details className="timeline-payload">
          <summary>Details</summary>
          {item.payload.name && <div><strong>Name:</strong> {item.payload.name}</div>}
          {item.payload.text && <pre>{item.payload.text}</pre>}
          {item.payload.errorCode && <div><strong>Error:</strong> {item.payload.errorCode}</div>}
          {item.payload.referenceId && <div><strong>Reference:</strong> {item.payload.referenceId}</div>}
          {item.payload.stopReason && <div><strong>Stop reason:</strong> {item.payload.stopReason}</div>}
          {item.payload.count !== null && <div><strong>Count:</strong> {item.payload.count}</div>}
        </details>
      )}
      {item.source && <div className="source-pointer">Source: {item.source.kind} · {item.source.sourceId} · {item.source.availability}</div>}
    </article>
  );
}

function category(type: string): string {
  if (isMessage(type)) return "message";
  if (type.startsWith("warning") || type.startsWith("approval")) return "attention";
  if (type.startsWith("command") || type.startsWith("tool")) return "execution";
  return "event";
}

function isMessage(type: string): boolean {
  return type === "user.message" || type === "assistant.message" || type === "assistant.final";
}

function messageRole(type: string): string {
  if (type === "user.message") return "message-user";
  if (type === "assistant.message" || type === "assistant.final") return "message-assistant";
  return "";
}

function safeToken(value: string): string {
  const token = value.toLowerCase();
  return /^[a-z0-9-]+$/.test(token) ? token : "unknown";
}

function formatTime(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.valueOf()) ? value : date.toLocaleString();
}

function hasPayload(item: TimelineItemData): boolean {
  return Object.values(item.payload).some((value) => value !== null && value !== "" && value !== item.payload.kind);
}
