import {
  Bot,
  FilePlus2,
  FolderPlus,
  Plus,
  RefreshCw,
  Send,
  ShieldCheck,
  Sparkles,
  Square,
  Trash2,
  Wrench,
  X,
} from "lucide-react";
import {
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type KeyboardEvent,
  type ReactNode,
} from "react";
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
  readonly stopping?: boolean;
  readonly onStop?: () => void;
  readonly onClear: () => void;
  readonly onSlashCommand?: (command: string) => void;
}

type OpenMenu = "add" | "tools" | null;

interface MenuItem {
  readonly id: string;
  readonly label: string;
  readonly description?: string;
  readonly icon?: ReactNode;
  readonly group?: string;
  readonly select: () => void;
}

export function Composer(props: ComposerProps) {
  const initialMentionsOpen = hasMentionContent(props.composer);
  const [composing, setComposing] = useState(false);
  const [activeMentionIndex, setActiveMentionIndex] = useState(0);
  const [mentionsRequested, setMentionsRequested] = useState(initialMentionsOpen);
  const [openMenu, setOpenMenu] = useState<OpenMenu>(null);
  const textarea = useRef<HTMLTextAreaElement>(null);
  const addTrigger = useRef<HTMLButtonElement>(null);
  const toolsTrigger = useRef<HTMLButtonElement>(null);
  const wasDisabled = useRef(true);
  const pending = props.composer.snapshot?.pendingIntent;
  const busy = props.draft.status === "validating" || props.draft.status === "enqueueing";
  const disabled = Boolean(props.disabledReason) || busy || Boolean(pending);
  const stopMode = Boolean(props.onStop);
  const statusText = statusMessage(props.draft, props.disabledReason);
  const effectiveModel = props.composer.snapshot?.effectiveModel ?? props.modelLabel ?? "Unavailable";
  const approvalMode = props.composer.snapshot?.approvalMode ?? props.approvalModeLabel ?? "Unavailable";

  const mentionOptions = useMemo(() => [
    ...props.composer.mentions.context.map((item) => ({ id: `mention-context-${item.selectionId}`, select: () => selectContext(item) })),
    ...props.composer.mentions.skills.map((item) => ({ id: `mention-skill-${item.id}`, select: () => selectCatalog("skill", item, props.composer.mentions.revisions.skills) })),
    ...props.composer.mentions.experts.map((item) => ({ id: `mention-expert-${item.id}`, select: () => selectCatalog("expert", item, props.composer.mentions.revisions.experts) })),
    ...props.composer.mentions.automations.map((item) => ({ id: `mention-automation-${item.id}`, select: () => selectCatalog("automation", item, props.composer.mentions.revisions.automations) })),
  ], [props.composer.mentions, props.onCatalog, props.onContext]);
  const mentionsOpen = openMenu === null && mentionsRequested && hasMentionContent(props.composer);
  const slashMatch = /^\/([a-z-]*)$/i.exec(props.draft.text.trim());
  const allSlashCommands: readonly (readonly [string, string])[] = [
    ["new", "新建对话"], ["changes", "打开 Changes"], ["terminal", "打开 Terminal"], ["settings", "打开 Settings"], ["clear", "清空 Composer"],
  ];
  const slashCommands = allSlashCommands.filter(([command]) => !slashMatch?.[1] || command.startsWith(slashMatch[1].toLowerCase()));
  const slashOpen = Boolean(props.onSlashCommand && slashMatch && slashCommands.length);

  const addItems: readonly MenuItem[] = useMemo(() => [
    {
      id: "composer-add-file",
      label: "添加工作区文件",
      description: "从当前工作区选择一个文件",
      icon: <FilePlus2 size={16} aria-hidden="true" />,
      select: () => selectPicker(props.onPickFile),
    },
    {
      id: "composer-add-folder",
      label: "添加工作区目录",
      description: "从当前工作区选择一个目录",
      icon: <FolderPlus size={16} aria-hidden="true" />,
      select: () => selectPicker(props.onPickFolder),
    },
  ], [props.onPickFile, props.onPickFolder]);

  const toolItems: readonly MenuItem[] = useMemo(() => [
    ...props.composer.mentions.skills.map((item) => catalogMenuItem("Skills", "skill", item, props.composer.mentions.revisions.skills)),
    ...props.composer.mentions.experts.map((item) => catalogMenuItem("Experts", "expert", item, props.composer.mentions.revisions.experts)),
    ...props.composer.mentions.automations.map((item) => catalogMenuItem("Automations", "automation", item, props.composer.mentions.revisions.automations)),
  ], [props.composer.mentions, props.onCatalog]);

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
  useEffect(() => {
    if (!openMenu) return;
    const menu = openMenu;
    const onDocumentKeyDown = (event: globalThis.KeyboardEvent) => {
      if (event.key !== "Escape") return;
      event.preventDefault();
      setOpenMenu(null);
      if (menu === "tools") props.onCloseMentions();
      window.requestAnimationFrame(() => (menu === "add" ? addTrigger : toolsTrigger).current?.focus());
    };
    document.addEventListener("keydown", onDocumentKeyDown);
    return () => document.removeEventListener("keydown", onDocumentKeyDown);
  }, [openMenu, props.onCloseMentions]);

  function selectContext(item: ContextDescriptorData) {
    props.onContext(item);
    closeMentionsAndFocus();
  }

  function selectCatalog(kind: ComposerCatalogKind, item: CatalogItemData, revision: string) {
    props.onCatalog(kind, item, revision);
    setOpenMenu(null);
    closeMentionsAndFocus();
  }

  function catalogMenuItem(group: string, kind: ComposerCatalogKind, item: CatalogItemData, revision: string): MenuItem {
    return {
      id: `composer-tool-${kind}-${item.id}`,
      label: item.displayName,
      description: item.description,
      group,
      icon: kind === "skill" ? <Sparkles size={16} aria-hidden="true" /> : kind === "expert" ? <Bot size={16} aria-hidden="true" /> : <Wrench size={16} aria-hidden="true" />,
      select: () => selectCatalog(kind, item, revision),
    };
  }

  function selectPicker(pick: () => void) {
    setOpenMenu(null);
    pick();
    window.requestAnimationFrame(() => textarea.current?.focus());
  }

  function closeMentionsAndFocus() {
    setMentionsRequested(false);
    props.onCloseMentions();
    window.requestAnimationFrame(() => textarea.current?.focus());
  }

  function closeMenu(menu: Exclude<OpenMenu, null>, restoreFocus: boolean) {
    setOpenMenu(null);
    if (menu === "tools") props.onCloseMentions();
    if (restoreFocus) window.requestAnimationFrame(() => (menu === "add" ? addTrigger : toolsTrigger).current?.focus());
  }

  function toggleMenu(menu: Exclude<OpenMenu, null>) {
    if (openMenu === menu) {
      closeMenu(menu, true);
      return;
    }
    setMentionsRequested(false);
    props.onCloseMentions();
    setOpenMenu(menu);
    if (menu === "tools") props.onSearch("");
  }

  function change(text: string, target: HTMLTextAreaElement) {
    props.onText(text);
    resize(target);
    const match = /(?:^|\s)@([^\s@]*)$/u.exec(text);
    if (match) {
      setOpenMenu(null);
      setMentionsRequested(true);
      props.onSearch(match[1] ?? "");
    } else {
      setMentionsRequested(false);
      props.onCloseMentions();
    }
  }

  function keyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === "Escape" && slashOpen) { event.preventDefault(); props.onText(""); return; }
    if (event.key === "Enter" && slashOpen && !event.shiftKey) {
      event.preventDefault();
      const command = slashCommands[0]?.[0];
      if (command) props.onSlashCommand?.(command);
      return;
    }
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
      closeMentionsAndFocus();
      return;
    }
    if (event.key === "Enter" && mentionsOpen) {
      event.preventDefault();
      mentionOptions[activeMentionIndex]?.select();
      return;
    }
    if (event.key === "Enter" && !event.shiftKey && !composing && !event.nativeEvent.isComposing) {
      event.preventDefault();
      if (!stopMode && !disabled && props.draft.text.trim()) props.onSend();
    }
  }

  return (
    <section className="composer" aria-label="Composer">
      <div className="queued-intent" aria-live="polite" hidden={!pending}>
        <div>
          <strong>{pending ? (pending.delivery === "next-turn" ? "Queued for next turn" : "Ready to run") : "No pending input"}</strong>
          <span>{pending ? `${pending.contextCount} context · ${pending.catalogCount} catalog` : "No queued context"}</span>
        </div>
        <button type="button" onClick={props.onClear} aria-label="Clear pending input" disabled={!pending}>
          <Trash2 size={16} aria-hidden="true" /> Clear
        </button>
      </div>

      <div className="composer-chips" aria-label="Selected composer context" hidden={props.draft.contextSelections.length + props.draft.catalogSelections.length === 0}>
        {props.draft.contextSelections.map((item) => (
          <span className="composer-chip" key={item.selectionId}>
            <span>{item.relativePath}</span>
            <button type="button" aria-label={`Remove ${item.relativePath}`} onClick={() => props.onRemoveContext(item.selectionId)}>
              <X size={13} aria-hidden="true" />
            </button>
          </span>
        ))}
        {props.draft.catalogSelections.map((item) => (
          <span className={`composer-chip composer-chip-${item.kind}`} key={`${item.kind}:${item.id}`}>
            <span>{item.label}</span>
            <button type="button" aria-label={`Remove ${item.label}`} onClick={() => props.onRemoveCatalog(item)}>
              <X size={13} aria-hidden="true" />
            </button>
          </span>
        ))}
      </div>

      <div className="composer-input-wrap">
        <textarea
          ref={textarea}
          defaultValue=""
          onChange={(event) => change(event.target.value, event.currentTarget)}
          onKeyDown={keyDown}
          onCompositionStart={() => setComposing(true)}
          onCompositionEnd={() => setComposing(false)}
          placeholder="描述你想完成的任务，输入 @ 添加上下文"
          aria-label="Composer prompt"
          aria-describedby="composer-help composer-status"
          aria-autocomplete="list"
          aria-controls={mentionsOpen ? "composer-mentions" : undefined}
          aria-expanded={mentionsOpen}
          aria-activedescendant={mentionsOpen ? mentionOptions[activeMentionIndex]?.id : undefined}
          disabled={disabled}
        />
        {mentionsOpen ? (
          <MentionMenu
            id="composer-mentions"
            value={props.composer.mentions}
            activeId={mentionOptions[activeMentionIndex]?.id ?? null}
            onActive={(id) => setActiveMentionIndex(Math.max(0, mentionOptions.findIndex((option) => option.id === id)))}
            onContext={selectContext}
            onCatalog={selectCatalog}
            onClose={closeMentionsAndFocus}
          />
        ) : null}
        {slashOpen ? <div className="slash-command-menu" role="listbox" aria-label="Slash commands">
          {slashCommands.map(([command, description]) => <button type="button" role="option" aria-selected="false" key={command} onClick={() => props.onSlashCommand?.(command)}><code>/{command}</code><span>{description}</span></button>)}
        </div> : null}
      </div>

      <div className="composer-footer">
        <div className="composer-toolbar-group composer-toolbar-left">
          <div className="composer-menu-anchor">
            <button
              ref={addTrigger}
              className="composer-tool-button composer-icon-button"
              type="button"
              aria-label="Add context"
              aria-haspopup="menu"
              aria-controls={openMenu === "add" ? "composer-add-menu" : undefined}
              aria-expanded={openMenu === "add"}
              disabled={disabled}
              onClick={() => toggleMenu("add")}
            >
              <Plus size={18} aria-hidden="true" />
            </button>
            {openMenu === "add" ? (
              <ComposerMenu id="composer-add-menu" label="添加上下文" items={addItems} onClose={() => closeMenu("add", true)} />
            ) : null}
          </div>

          <div className="composer-menu-anchor">
            <button
              ref={toolsTrigger}
              className="composer-tool-button composer-tools-trigger"
              type="button"
              aria-label="工具"
              aria-haspopup="menu"
              aria-controls={openMenu === "tools" ? "composer-tools-menu" : undefined}
              aria-expanded={openMenu === "tools"}
              disabled={disabled}
              onClick={() => toggleMenu("tools")}
            >
              <Wrench size={15} aria-hidden="true" /><span>工具</span>
            </button>
            {openMenu === "tools" ? (
              <ComposerMenu
                id="composer-tools-menu"
                label="工具目录"
                items={toolItems}
                loading={props.composer.mentions.loading}
                error={props.composer.mentions.error}
                emptyText="没有可用的 Skills、Experts 或 Automations"
                onClose={() => closeMenu("tools", true)}
              />
            ) : null}
          </div>

          <span className="composer-readonly-state" aria-label={`Approval mode: ${approvalMode}`} title="Approval Mode is controlled by the authoritative desktop state">
            <ShieldCheck size={14} aria-hidden="true" />
            <span className="composer-state-label">Approval</span>
            <strong>{approvalMode}</strong>
          </span>
        </div>

        <div className="composer-toolbar-group composer-toolbar-right">
          <span className="composer-readonly-state composer-model-state" aria-label={`Effective model: ${effectiveModel}`} title="Effective Model is controlled by the authoritative desktop state">
            <Bot size={14} aria-hidden="true" />
            <span className="composer-state-label">Model</span>
            <strong>{effectiveModel}</strong>
          </span>
          {stopMode ? (
            <button className="composer-send composer-stop" type="button" onClick={props.onStop} disabled={props.stopping} aria-label={props.stopping ? "Stopping response" : "Stop response"}>
              <Square size={14} fill="currentColor" aria-hidden="true" />
              <span className="sr-only">{props.stopping ? "Stopping" : "Stop"}</span>
            </button>
          ) : props.disabledActionLabel ? (
            <button className="composer-send composer-alternate-action" type="button" onClick={props.onDisabledAction} disabled={!props.onDisabledAction} aria-label={props.disabledActionLabel}>
              <RefreshCw size={17} aria-hidden="true" />
              <span className="sr-only">{props.disabledActionLabel}</span>
            </button>
          ) : (
            <button className="composer-send" type="button" onClick={props.onSend} disabled={disabled || !props.draft.text.trim()} aria-label={busy ? "Sending prompt" : "Send prompt"}>
              <Send size={17} aria-hidden="true" />
              <span className="sr-only">{busy ? "Sending" : "Send"}</span>
            </button>
          )}
        </div>
      </div>

      <div id="composer-help" className="sr-only">Enter sends. Shift Enter inserts a new line.</div>
      <div id="composer-status" className={props.draft.error ? "composer-error" : "composer-status"} role={props.draft.error ? "alert" : "status"} hidden={!statusText}>
        <span>{statusText}</span>
      </div>
    </section>
  );
}

function ComposerMenu({ id, label, items, loading = false, error = null, emptyText = "No items", onClose }: {
  readonly id: string;
  readonly label: string;
  readonly items: readonly MenuItem[];
  readonly loading?: boolean;
  readonly error?: string | null;
  readonly emptyText?: string;
  readonly onClose: () => void;
}) {
  const [activeIndex, setActiveIndex] = useState(0);
  const menu = useRef<HTMLDivElement>(null);
  const itemRefs = useRef<Array<HTMLButtonElement | null>>([]);

  useEffect(() => {
    setActiveIndex(0);
    const frame = window.requestAnimationFrame(() => (itemRefs.current[0] ?? menu.current)?.focus());
    return () => window.cancelAnimationFrame(frame);
  }, [items.length]);

  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === "Escape") {
      event.preventDefault();
      event.stopPropagation();
      onClose();
      return;
    }
    if (!items.length || !["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) return;
    event.preventDefault();
    const next = event.key === "Home"
      ? 0
      : event.key === "End"
        ? items.length - 1
        : (activeIndex + (event.key === "ArrowDown" ? 1 : -1) + items.length) % items.length;
    setActiveIndex(next);
    itemRefs.current[next]?.focus();
  }

  let currentGroup = "";
  return (
    <div ref={menu} id={id} className="composer-menu" role="menu" aria-label={label} tabIndex={-1} onKeyDown={onKeyDown}>
      <div className="composer-menu-heading">
        <span>{label}</span>
        <button type="button" onClick={onClose} aria-label={`关闭${label}`}>Esc</button>
      </div>
      {loading ? <div className="composer-menu-state" role="status">正在加载…</div> : null}
      {error ? <div className="composer-menu-state composer-menu-error" role="alert">{error}</div> : null}
      {!loading && !error && items.length === 0 ? <div className="composer-menu-state">{emptyText}</div> : null}
      {!loading && !error ? items.map((item, index) => {
        const showGroup = Boolean(item.group && item.group !== currentGroup);
        if (item.group) currentGroup = item.group;
        return (
          <div className="composer-menu-entry" key={item.id}>
            {showGroup ? <div className="composer-menu-group-label">{item.group}</div> : null}
            <button
              ref={(node) => { itemRefs.current[index] = node; }}
              id={item.id}
              type="button"
              role="menuitem"
              tabIndex={index === activeIndex ? 0 : -1}
              onFocus={() => setActiveIndex(index)}
              onMouseMove={() => setActiveIndex(index)}
              onClick={item.select}
            >
              {item.icon}
              <span><strong>{item.label}</strong>{item.description ? <small>{item.description}</small> : null}</span>
            </button>
          </div>
        );
      }) : null}
    </div>
  );
}

function resize(textarea: HTMLTextAreaElement) {
  textarea.style.height = "auto";
  const height = Math.min(Math.max(textarea.scrollHeight, 48), 180);
  textarea.style.height = `${height}px`;
  textarea.style.overflowY = textarea.scrollHeight > 180 ? "auto" : "hidden";
}

function hasMentionContent(composer: ComposerUiState) {
  const mentions = composer.mentions;
  return mentions.loading || Boolean(mentions.error) || mentions.context.length + mentions.skills.length + mentions.experts.length + mentions.automations.length > 0;
}

function statusMessage(draft: ComposerDraft, disabledReason: string | null) {
  if (draft.error) return draft.error;
  if (disabledReason) return disabledReason;
  if (draft.status === "validating" || draft.status === "enqueueing") return "Sending prompt…";
  if (draft.status === "queued") return "Prompt queued.";
  return "";
}
