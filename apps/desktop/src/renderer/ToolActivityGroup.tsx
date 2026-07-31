import { Activity, ChevronDown } from "lucide-react";
import type { ToolActivityBlock } from "./conversation-block-projector";

export function ToolActivityGroup({ block }: { readonly block: ToolActivityBlock }) {
  return (
    <details className={`tool-activity-group activity-${block.status}`} open={block.defaultExpanded}>
      <summary>
        <span><Activity size={14} aria-hidden="true" />{block.summary}</span>
        <span className="activity-disclosure">展开 <ChevronDown size={13} aria-hidden="true" /></span>
      </summary>
      <ol>
        {block.auditItems.map((item) => (
          <li key={item.itemId}>
            <span aria-hidden="true">{activityGlyph(item.status, item.payload.succeeded)}</span>
            <span>{item.summary || item.payload.name || item.type}</span>
          </li>
        ))}
      </ol>
    </details>
  );
}

function activityGlyph(status: string, succeeded: boolean | null): string {
  if (status === "failed" || succeeded === false) return "!";
  if (status === "running" || status === "started") return "•";
  return "✓";
}
