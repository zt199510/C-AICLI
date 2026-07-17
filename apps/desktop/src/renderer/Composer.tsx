import { FilePlus2, FolderPlus, Send, Trash2, X } from "lucide-react";
import { useRef, useState, type KeyboardEvent } from "react";
import type { CatalogItemData, ContextDescriptorData } from "../generated/desktop-contracts";
import type { ComposerDraft, ComposerCatalogKind, ComposerUiState, SelectedCatalogItem } from "./composer-state";
import { MentionMenu } from "./MentionMenu";

interface ComposerProps {
  readonly draft: ComposerDraft;
  readonly composer: ComposerUiState;
  readonly disabledReason: string | null;
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
  const textarea = useRef<HTMLTextAreaElement>(null);
  const pending = props.composer.snapshot?.pendingIntent;
  const busy = props.draft.status === "validating" || props.draft.status === "enqueueing";
  const disabled = Boolean(props.disabledReason) || busy || Boolean(pending);

  function change(text: string) {
    props.onText(text);
    const match = /(?:^|\s)@([^\s@]*)$/u.exec(text);
    if (match) props.onSearch(match[1] ?? ""); else props.onCloseMentions();
  }
  function keyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === "Escape") { props.onCloseMentions(); return; }
    if (event.key === "Enter" && !event.shiftKey && !composing && !event.nativeEvent.isComposing) {
      event.preventDefault();
      if (!disabled && props.draft.text.trim()) props.onSend();
    }
  }

  return (
    <section className="composer" aria-label="Composer">
      {pending && <div className="queued-intent" aria-live="polite"><div><strong>{pending.delivery === "next-turn" ? "Queued for next turn" : "Ready to run"}</strong><span>{pending.contextCount} context · {pending.catalogCount} catalog</span></div><button type="button" onClick={props.onClear} aria-label="Clear pending input"><Trash2 size={16} aria-hidden="true" /> Clear</button></div>}
      {(props.draft.contextSelections.length > 0 || props.draft.catalogSelections.length > 0) && <div className="composer-chips" aria-label="Selected composer context">
        {props.draft.contextSelections.map(item => <span className="composer-chip" key={item.selectionId}>{item.relativePath}<button type="button" aria-label={`Remove ${item.relativePath}`} onClick={() => props.onRemoveContext(item.selectionId)}><X size={13} /></button></span>)}
        {props.draft.catalogSelections.map(item => <span className="composer-chip catalog-chip" key={`${item.kind}:${item.id}`}>{item.label}<button type="button" aria-label={`Remove ${item.label}`} onClick={() => props.onRemoveCatalog(item)}><X size={13} /></button></span>)}
      </div>}
      <div className="composer-input-wrap">
        <textarea ref={textarea} value={props.draft.text} onChange={(event) => change(event.target.value)} onKeyDown={keyDown} onCompositionStart={() => setComposing(true)} onCompositionEnd={() => setComposing(false)} placeholder="Ask about this workspace… Use @ to add context" aria-label="Composer prompt" aria-describedby="composer-help composer-status" disabled={Boolean(pending) || Boolean(props.disabledReason)} />
        {props.composer.mentions.loading || props.composer.mentions.error || props.composer.mentions.context.length + props.composer.mentions.skills.length + props.composer.mentions.experts.length + props.composer.mentions.automations.length > 0 ? <MentionMenu value={props.composer.mentions} onContext={props.onContext} onCatalog={props.onCatalog} onClose={props.onCloseMentions} /> : null}
      </div>
      <div className="composer-footer">
        <div className="composer-tools"><button type="button" onClick={props.onPickFile} disabled={disabled} aria-label="Attach workspace file"><FilePlus2 size={16} /> File</button><button type="button" onClick={props.onPickFolder} disabled={disabled} aria-label="Attach workspace folder"><FolderPlus size={16} /> Folder</button></div>
        <div className="composer-summary"><span>{props.composer.snapshot ? `${props.composer.snapshot.effectiveModel} · ${props.composer.snapshot.approvalMode}` : "Model and approval policy unavailable"}</span><button className="composer-send" type="button" onClick={props.onSend} disabled={disabled || !props.draft.text.trim()} aria-label="Queue prompt"><Send size={16} />{busy ? "Queuing…" : "Send"}</button></div>
      </div>
      <div id="composer-help" className="sr-only">Enter sends. Shift Enter inserts a new line.</div>
      <div id="composer-status" className={props.draft.error ? "composer-error" : "composer-status"} role={props.draft.error ? "alert" : "status"}>{props.draft.error ?? props.disabledReason ?? (props.draft.status === "queued" ? "Prompt queued." : "")}</div>
    </section>
  );
}
