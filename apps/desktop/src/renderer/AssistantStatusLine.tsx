import type { AssistantLifecycle, AssistantMessageBlock } from "./conversation-block-projector";

export function AssistantStatusLine({ block }: { readonly block: AssistantMessageBlock }) {
  const text = assistantStatusText(block);
  if (!text || block.lifecycle === "completed" || block.lifecycle === "canceled") return null;

  if (block.lifecycle === "retry-wait") {
    return <div className="assistant-retry-strip">{text}</div>;
  }

  return (
    <div className={`assistant-status-line assistant-status-${block.lifecycle}`}>
      {isAnimated(block.lifecycle) ? <span className="assistant-status-dot" aria-hidden="true" /> : null}
      <span>{text}</span>
    </div>
  );
}

export function assistantStatusText(block: AssistantMessageBlock): string {
  const { lifecycle, turn } = block;
  if (lifecycle === "connecting") return "正在连接";
  if (lifecycle === "thinking") return "正在思考";
  if (lifecycle === "streaming") return "正在生成回复";
  if (lifecycle === "retry-wait") {
    const retry = Math.min(turn.provider.attempt, turn.provider.maxAdditionalRetries);
    return `连接暂时中断，正在重试 ${retry}/${turn.provider.maxAdditionalRetries}`;
  }
  if (lifecycle === "approval") return "需要批准";
  if (lifecycle === "canceling") return "正在停止";
  if (lifecycle === "retry-exhausted") {
    return `连接失败，已重试 ${turn.provider.maxAdditionalRetries} 次`;
  }
  if (lifecycle === "recovery-required") return "执行已中断";
  if (lifecycle === "failed") return "处理失败";
  return "";
}

export function assistantAnnouncement(block: AssistantMessageBlock): string {
  const status = assistantStatusText(block);
  if (status) return status;
  return processingDuration(block) ?? "";
}

export function processingDuration(block: AssistantMessageBlock): string | null {
  const { turn, lifecycle } = block;
  if (!turn.completedAtUtc) return null;
  const elapsed = new Date(turn.completedAtUtc).valueOf() - new Date(turn.createdAtUtc).valueOf();
  if (!Number.isFinite(elapsed) || elapsed < 0) return null;
  const seconds = (elapsed / 1000).toFixed(1);
  if (lifecycle === "canceled") return `已停止 · 处理 ${seconds} 秒`;
  if (lifecycle === "retry-exhausted") return `连接失败 · 处理 ${seconds} 秒`;
  if (lifecycle === "failed") return `处理失败 · 处理 ${seconds} 秒`;
  return `已处理 ${seconds} 秒`;
}

function isAnimated(lifecycle: AssistantLifecycle): boolean {
  return lifecycle === "connecting" ||
    lifecycle === "thinking" ||
    lifecycle === "streaming" ||
    lifecycle === "canceling";
}
