import { FilePlus2, FolderPlus, Send, Trash2, X } from "lucide-react";
import { useEffect, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent } from "react";
import type { CatalogItemData, ContextDescriptorData } from "../generated/desktop-contracts";
import type { ComposerDraft, ComposerCatalogKind, ComposerUiState, SelectedCatalogItem } from "./composer-state";
import { MentionMenu } from "./MentionMenu";

interface ComposerProps {
  readonly draft: ComposerDraft;
  readonly composer: ComposerUiState;
  readonly disabledReason: string | null;
  readonly modelLabel?: string;
  readonly approvalModeLabel?: string;
  readonly disabledActionLabel?: string;
  readonly onDisabledAction?: () => void;
  readonly onText: (text: string) => void;
  readonly onSearch: (query: string) => void;
  readonly onCloseMentions: () => void;
  readonly onContext: (item: ContextDescriptorData) => void;
  readonly onCatalog: (kind: ComposerCatalogKind, item: CatalogItemData, revision: string) => void;
  readonly onRemoveContext: (selectionId: string) => void;
  readonly onRemoveCatalog: (item: SelectedCatalogItem) => void;
  readonly onPickFile: () => void;
  readonly onPickFolder: () => void;
  readonly onSend: () => void;
  readonly onClear: () => void;
}

export function Composer(props: ComposerProps) {
  const [composing, setComposing] = useState(false);
  const [activeMentionIndex, setActiveMentionIndex] = useState(0);
  const textarea = useRef<HTMLTextAreaElement>(null);
  const wasDisabled = useRef(true);
  const pending = props.composer.snapshot?.pendingIntent;
  const busy = props.draft.status === "validating" || props.draft.status === "enqueueing";
  const disabled = Boolean(props.disabledReason) || busy || Boolean(pending);
  const statusText = props.draft.error ?? props.disabledReason ?? (busy ? "Sending prompt…" : "");
  const mentionOptions = useMemo(() => [
    ...props.composer.mentions.context.map((item) => ({ id: `mention-context-${item.selectionId}`, select: () => props.onContext(item) })),
    ...props.composer.mentions.skills.map((item) => ({ id: `mention-skill-${item.id}`, select: () => props.onCatalog("skill", item, props.composer.mentions.revisions.skills) })),
    ...props.composer.mentions.experts.map((item) => ({ id: `mention-expert-${item.id}`, select: () => props.onCatalog("expert", item, props.composer.mentions.revisions.experts) })),
    ...props.composer.mentions.automations.map((item) => ({ id: `mention-automation-${item.id}`, select: () => props.onCatalog("automation", item, props.composer.mentions.revisions.automations) })),
  ], [props.composer.mentions, props.onCatalog, props.onContext]);
  const mentionsOpen = props.composer.mentions.loading || Boolean(props.composer.mentions.error) || mentionOptions.length > 0;

  useEffect(() => { setActiveMentionIndex(0); }, [props.composer.mentions]);
  useLayoutEffect(() => {
    if (!textarea.current) return;
    if (textarea.current.value !== props.draft.text) textarea.current.value = props.draft.text;
    resize(textarea.current);
  }, [props.draft.text]);
  useEffect(() => {
    if (wasDisabled.current && !disabled) textarea.current?.focus();
    wasDisabled.current = disabled;
  }, [disabled]);

  function change(text: string, target: HTMLTextAreaElement) {
    props.onText(text);
    resize(target);
    const match = /(?:^|\s)@([^\s@]*)$/u.exec(text);
    if (match) props.onSearch(match[1] ?? ""); else props.onCloseMentions();
  }
  function keyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (mentionsOpen && ["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) {
      event.preventDefault();
      if (mentionOptions.length === 0) return;
      if (event.key === "Home") setActiveMentionIndex(0);
      else if (event.key === "End") setActiveMentionIndex(mentionOptions.length - 1);
      else setActiveMentionIndex((current) => (current + (event.key === "ArrowDown" ? 1 : -1) + mentionOptions.length) % mentionOptions.length);
      return;
    }
    if (event.key === "Escape" && mentionsOpen) {
      event.preventDefault();
      props.onCloseMentions();
      textarea.current?.focus();
      return;
    }
    if (event.key === "Enter" && mentionsOpen && mentionOptions[activeMentionIndex]) {
      event.preventDefault();
      mentionOptions[activeMentionIndex].select();
      props.onCloseMentions();
      textarea.current?.focus();
      return;
    }
    if (event.key === "Enter" && !event.shiftKey && !composing && !event.nativeEvent.isComposing) {
      event.preventDefault();
      if (!disabled && props.draft.text.trim()) props.onSend();
    }
  }

  return (
    <section className="composer" aria-label="Composer">
      <div className="queued-intent" aria-live="polite" hidden={!pending}><div><strong>{pending ? (pending.delivery === "next-turn" ? "Queued for next turn" : "Ready to run") : "No pending input"}</strong><span>{pending ? `${pending.contextCount} context · ${pending.catalogCount} catalog` : "No queued context"}</span></div><button type="button" onClick={props.onClear} aria-label="Clear pending input" disabled={!pending}><Trash2 size={16} aria-hidden="true" /> Clear</button></div>
      {(props.draft.contextSelections.length > 0 || props.draft.catalogSelections.length > 0) && <div className="composer-chips" aria-label="Selected composer context">
        {props.draft.contextSelections.map(item => <span className="composer-chip" key={item.selectionId}>{item.relativePath}<button type="button" aria-label={`Remove ${item.relativePath}`} onClick={() => props.onRemoveContext(item.selectionId)}><X size={13} /></button></span>)}
        {props.draft.catalogSelections.map(item => <span className="composer-chip catalog-chip" key={`${item.kind}:${item.id}`}>{item.label}<button type="button" aria-label={`Remove ${item.label}`} onClick={() => props.onRemoveCatalog(item)}><X size={13} /></button></span>)}
      </div>}
      <div className="composer-input-wrap">
        <textarea ref={textarea} defaultValue="" onChange={(event) => change(event.target.value, event.currentTarget)} onKeyDown={keyDown} onCompositionStart={() => setComposing(true)} onCompositionEnd={() => setComposing(false)} placeholder={props.disabledReason ?? "Message C-AICLI…"} aria-label="Composer prompt" aria-describedby="composer-help composer-status" aria-autocomplete="list" aria-controls={mentionsOpen ? "composer-mentions" : undefined} aria-expanded={mentionsOpen} aria-activedescendant={mentionsOpen ? mentionOptions[activeMentionIndex]?.id : undefined} disabled={disabled} />
        {mentionsOpen ? <MentionMenu id="composer-mentions" value={props.composer.mentions} activeId={mentionOptions[activeMentionIndex]?.id ?? null} onActive={(id) => setActiveMentionIndex(Math.max(0, mentionOptions.findIndex((option) => option.id === id)))} onContext={props.onContext} onCatalog={props.onCatalog} onClose={() => { props.onCloseMentions(); textarea.current?.focus(); }} /> : null}
      </div>
      <div className="composer-footer">
        <div className="composer-tools"><button type="button" title="Attach file" onClick={props.onPickFile} disabled={disabled} aria-label="Attach workspace file"><FilePlus2 size={17} /><span className="sr-only">File</span></button><button type="button" title="Attach folder" onClick={props.onPickFolder} disabled={disabled} aria-label="Attach workspace folder"><FolderPlus size={17} /><span className="sr-only">Folder</span></button></div>
        <div className="composer-summary"><span>{props.composer.snapshot ? `${props.composer.snapshot.effectiveModel} · ${props.composer.snapshot.approvalMode}` : props.modelLabel ? `${props.modelLabel}${props.approvalModeLabel ? ` · ${props.approvalModeLabel}` : ""}` : "Model unavailable"}</span><button className="composer-send" type="button" onClick={props.onSend} disabled={disabled || !props.draft.text.trim()} aria-label={busy ? "Sending prompt" : "Send prompt"}><Send size={17} /><span className="sr-only">{busy ? "Sending" : "Send"}</span></button></div>
      </div>
      <div id="composer-help" className="sr-only">Enter sends. Shift Enter inserts a new line.</div>
      <div id="composer-status" className={props.draft.error ? "composer-error" : "composer-status"} role={props.draft.error ? "alert" : "status"} hidden={!statusText && !props.disabledActionLabel}><span>{statusText}</span>{props.disabledReason && props.disabledActionLabel && props.onDisabledAction ? <button type="button" className="composer-status-action" onClick={props.onDisabledAction}>{props.disabledActionLabel}</button> : null}</div>
    </section>
  );
}

function resize(textarea: HTMLTextAreaElement) {
  textarea.style.height = "auto";
  textarea.style.height = `${Math.min(Math.max(textarea.scrollHeight, 48), 180)}px`;
}
