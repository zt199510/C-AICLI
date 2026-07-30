import { Activity, ListTree } from "lucide-react";
import type { TimelineProjectionBlock as ProjectionBlock } from "./timeline-projection";
import { TimelineItem } from "./TimelineItem";

export function TimelineProjectionBlock({ block }: { block: ProjectionBlock }) {
  if (block.kind !== "execution" || block.items.length === 1) {
    return <TimelineItem item={block.items[0]!} projectionKind={block.kind} />;
  }

  const latest = block.items.at(-1)!;
  const failed = block.items.some((item) => item.status === "failed" || item.payload.succeeded === false);
  return (
    <article
      className={`timeline-card timeline-execution projection-execution ${failed ? "projection-failed" : ""}`}
      data-projection-kind="execution"
      data-sequence={latest.sequence}
    >
      <header>
        <span className="timeline-icon"><Activity size={16} aria-hidden="true" /></span>
        <span className="timeline-type">Tool activity</span>
        <span className={`status-chip status-${failed ? "failed" : "completed"}`}>
          {failed ? "failed" : latest.status}
        </span>
        <span className="projection-count">{block.items.length} steps</span>
      </header>
      <p className="timeline-summary">{latest.redacted ? "Content redacted" : latest.summary}</p>
      <details className="timeline-payload projection-audit">
        <summary><ListTree size={14} aria-hidden="true" /> View audit events</summary>
        <ol>
          {block.items.map((item) => (
            <li key={item.itemId}>
              <span>{item.type}</span>
              <span>{item.redacted ? "Content redacted" : item.summary}</span>
              <span>{item.status}</span>
            </li>
          ))}
        </ol>
      </details>
    </article>
  );
}
