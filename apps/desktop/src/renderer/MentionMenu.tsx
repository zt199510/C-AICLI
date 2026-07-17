import type { CatalogItemData, ContextDescriptorData } from "../generated/desktop-contracts";
import type { ComposerCatalogKind, MentionResults } from "./composer-state";

interface MentionMenuProps {
  readonly value: MentionResults;
  readonly onContext: (item: ContextDescriptorData) => void;
  readonly onCatalog: (kind: ComposerCatalogKind, item: CatalogItemData, revision: string) => void;
  readonly onClose: () => void;
}

export function MentionMenu({ value, onContext, onCatalog, onClose }: MentionMenuProps) {
  return (
    <div className="mention-menu" role="listbox" aria-label="Composer mentions">
      <div className="mention-heading"><span>Add context or capability</span><button type="button" onClick={onClose} aria-label="Close mentions">Esc</button></div>
      {value.loading && <div className="mention-state" aria-live="polite">Searching…</div>}
      {value.error && <div className="mention-state" role="alert">{value.error}</div>}
      {!value.loading && !value.error && <>
        <MentionGroup title="Files and folders" items={value.context} render={(item) => (
          <button role="option" key={item.selectionId} type="button" onClick={() => onContext(item)}>
            <span>{item.relativePath}</span><small>{item.kind} · {formatBytes(item.byteCount)}</small>
          </button>
        )} />
        <CatalogGroup title="Skills" kind="skill" items={value.skills} revision={value.revisions.skills} onSelect={onCatalog} />
        <CatalogGroup title="Experts" kind="expert" items={value.experts} revision={value.revisions.experts} onSelect={onCatalog} />
        <CatalogGroup title="Automations" kind="automation" items={value.automations} revision={value.revisions.automations} onSelect={onCatalog} />
        {value.context.length + value.skills.length + value.experts.length + value.automations.length === 0 && <div className="mention-state">No matches</div>}
        {value.truncated && <div className="mention-state">Results truncated — refine your query.</div>}
      </>}
    </div>
  );
}

function CatalogGroup({ title, kind, items, revision, onSelect }: { title: string; kind: ComposerCatalogKind; items: readonly CatalogItemData[]; revision: string; onSelect: MentionMenuProps["onCatalog"] }) {
  return <MentionGroup title={title} items={items} render={(item) => <button role="option" key={item.id} type="button" onClick={() => onSelect(kind, item, revision)}><span>{item.displayName}</span><small>{item.description}</small></button>} />;
}

function MentionGroup<T>({ title, items, render }: { title: string; items: readonly T[]; render: (item: T) => React.ReactNode }) {
  if (items.length === 0) return null;
  return <div className="mention-group" role="group" aria-label={title}><div>{title}</div>{items.map(render)}</div>;
}

function formatBytes(value: number): string { return value < 1024 ? `${value} B` : value < 1024 * 1024 ? `${Math.ceil(value / 1024)} KiB` : `${(value / 1024 / 1024).toFixed(1)} MiB`; }
