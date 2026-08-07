import {
  FileCheck2,
  FolderOpen,
  Globe2,
  MessageCirclePlus,
  MoreHorizontal,
  Plus,
  SquareTerminal,
  X,
} from "lucide-react";
import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { WorkspacePanelHost, type WorkspacePanelHostProps } from "./WorkspacePanelHost";
import {
  buildWorkspaceToolGroups,
  formatToolBadge,
  isSelectableTool,
  type ToolDefinition,
  type WorkspacePanel,
} from "./WorkspaceToolRegistry";

const INLINE_WORKBENCH_MIN_WIDTH = 1200;

export interface WorkspaceToolSidebarProps extends Omit<WorkspacePanelHostProps, "onClose"> {
  readonly openPanels: readonly WorkspacePanel[];
  readonly onPanel: (panel: WorkspacePanel) => void;
  readonly onClosePanel: (panel: WorkspacePanel) => void;
}

interface PrimaryLauncherItem {
  readonly id: WorkspacePanel | null;
  readonly label: string;
  readonly icon: typeof FileCheck2;
  readonly shortcut?: string;
  readonly disabledReason?: string;
}

const primaryLauncherItems: readonly PrimaryLauncherItem[] = [
  { id: "changes", label: "审阅", icon: FileCheck2, shortcut: "Ctrl+Shift+G" },
  { id: "terminal", label: "终端", icon: SquareTerminal },
  { id: null, label: "浏览器", icon: Globe2, shortcut: "Ctrl+T", disabledReason: "浏览器协议尚未接通" },
  { id: null, label: "文件", icon: FolderOpen, shortcut: "Ctrl+P", disabledReason: "工作区文件浏览协议尚未接通" },
  { id: null, label: "侧边聊天", icon: MessageCirclePlus, shortcut: "Ctrl+Alt+S", disabledReason: "侧边聊天协议尚未接通" },
];

export function WorkspaceToolSidebar({
  activePanel,
  openPanels,
  visible,
  review,
  workspaceReady,
  terminalCommands,
  terminalController,
  onPanel,
  onClosePanel,
  ...panelProps
}: WorkspaceToolSidebarProps) {
  const [menuOpen, setMenuOpen] = useState(false);
  const [extraMenuOpen, setExtraMenuOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const addButtonRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const extraMenuRef = useRef<HTMLDivElement>(null);
  const tabRefs = useRef(new Map<WorkspacePanel, HTMLButtonElement>());
  const wasVisible = useRef(visible);
  const groups = useMemo(
    () => buildWorkspaceToolGroups(review, workspaceReady, Boolean(terminalController?.canOpen ?? terminalCommands)),
    [review, terminalCommands, terminalController?.canOpen, workspaceReady],
  );
  const tools = useMemo(() => groups.flatMap((group) => group.tools), [groups]);
  const toolsById = useMemo(() => new Map(tools.map((tool) => [tool.id, tool])), [tools]);
  const tabs = openPanels.map((panel) => toolsById.get(panel)).filter((tool): tool is ToolDefinition => Boolean(tool));
  const extraTools = tools.filter((tool) => tool.id !== "changes" && tool.id !== "terminal");

  useEffect(() => {
    const opened = visible && !wasVisible.current;
    wasVisible.current = visible;
    if (!visible) {
      setMenuOpen(false);
      setExtraMenuOpen(false);
    }
    if (opened && window.innerWidth < INLINE_WORKBENCH_MIN_WIDTH) {
      window.requestAnimationFrame(() => (tabRefs.current.get(activePanel) ?? addButtonRef.current)?.focus());
    }
  }, [activePanel, visible]);

  useEffect(() => {
    if (!menuOpen) return;
    window.requestAnimationFrame(() => menuRef.current?.querySelector<HTMLButtonElement>("button:not([disabled])")?.focus());
  }, [menuOpen]);

  useEffect(() => {
    if (!extraMenuOpen) return;
    window.requestAnimationFrame(() => extraMenuRef.current?.querySelector<HTMLButtonElement>("button:not([disabled])")?.focus());
  }, [extraMenuOpen]);

  useEffect(() => {
    if (!menuOpen && !extraMenuOpen) return;
    const dismiss = (event: PointerEvent) => {
      if (event.target instanceof Node && !rootRef.current?.contains(event.target)) {
        setMenuOpen(false);
        setExtraMenuOpen(false);
      }
    };
    document.addEventListener("pointerdown", dismiss);
    return () => document.removeEventListener("pointerdown", dismiss);
  }, [extraMenuOpen, menuOpen]);

  function selectPanel(panel: WorkspacePanel) {
    setMenuOpen(false);
    setExtraMenuOpen(false);
    onPanel(panel);
  }

  function navigateTabs(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    if (event.key === "Delete") {
      event.preventDefault();
      closePanel(tabs[index]?.id, index);
      return;
    }
    if (event.key !== "ArrowLeft" && event.key !== "ArrowRight" && event.key !== "Home" && event.key !== "End") return;
    event.preventDefault();
    const nextIndex = event.key === "Home" ? 0
      : event.key === "End" ? tabs.length - 1
        : (index + (event.key === "ArrowRight" ? 1 : -1) + tabs.length) % tabs.length;
    const next = tabs[nextIndex];
    if (!next) return;
    onPanel(next.id);
    tabRefs.current.get(next.id)?.focus();
  }

  function closePanel(panel: WorkspacePanel | undefined, index: number) {
    if (!panel) return;
    const next = tabs[index + 1] ?? tabs[index - 1];
    onClosePanel(panel);
    window.requestAnimationFrame(() => (next ? tabRefs.current.get(next.id) : addButtonRef.current)?.focus());
  }

  function navigateMenu(event: KeyboardEvent<HTMLDivElement>, menu: HTMLDivElement | null) {
    if (event.key !== "ArrowDown" && event.key !== "ArrowUp" && event.key !== "Home" && event.key !== "End") return;
    const items = [...(menu?.querySelectorAll<HTMLButtonElement>("button:not([disabled])") ?? [])];
    if (items.length === 0) return;
    event.preventDefault();
    const current = Math.max(0, items.indexOf(document.activeElement as HTMLButtonElement));
    const index = event.key === "Home" ? 0
      : event.key === "End" ? items.length - 1
        : (current + (event.key === "ArrowDown" ? 1 : -1) + items.length) % items.length;
    items[index]?.focus();
  }

  function trapDrawerFocus(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === "Escape" && (menuOpen || extraMenuOpen)) {
      event.preventDefault();
      event.stopPropagation();
      setMenuOpen(false);
      setExtraMenuOpen(false);
      addButtonRef.current?.focus();
      return;
    }
    if (event.key !== "Tab" || window.innerWidth >= INLINE_WORKBENCH_MIN_WIDTH || !rootRef.current) return;
    const focusable = [...rootRef.current.querySelectorAll<HTMLElement>(
      'button:not([disabled]), [href], input:not([disabled]), [tabindex]:not([tabindex="-1"])',
    )].filter((element) => !element.hidden && element.getAttribute("aria-hidden") !== "true");
    const first = focusable[0];
    const last = focusable.at(-1);
    if (!first || !last) return;
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  return <div className="workspace-tool-sidebar workspace-workbench" ref={rootRef} onKeyDown={trapDrawerFocus}>
    <header className="workspace-workbench-tabbar">
      <div className="workspace-workbench-tabs" role="tablist" aria-label="工作区标签页">
        {tabs.map((tool, index) => {
          const Icon = tool.icon;
          const selected = activePanel === tool.id;
          return <div className="workspace-workbench-tab" data-active={selected} key={tool.id}>
            <button
              ref={(value) => { if (value) tabRefs.current.set(tool.id, value); else tabRefs.current.delete(tool.id); }}
              type="button"
              id={`workspace-workbench-tab-${tool.id}`}
              role="tab"
              aria-selected={selected}
              aria-controls="workspace-panel-host"
              tabIndex={selected ? 0 : -1}
              onClick={() => onPanel(tool.id)}
              onKeyDown={(event) => navigateTabs(event, index)}
            >
              <Icon size={14} aria-hidden="true" />
              <span>{workbenchLabel(tool)}</span>
            </button>
            <button type="button" tabIndex={-1} className="workspace-workbench-tab-close" aria-label={`关闭 ${workbenchLabel(tool)} 标签页`} onClick={() => closePanel(tool.id, index)}>
              <X size={13} aria-hidden="true" />
            </button>
          </div>;
        })}
      </div>
      <button
        ref={addButtonRef}
        className="workspace-workbench-add icon-button"
        type="button"
        aria-label="添加工作区工具"
        aria-haspopup="menu"
        aria-expanded={menuOpen || extraMenuOpen}
        aria-controls="workspace-workbench-add-menu workspace-workbench-extra-menu"
        onClick={() => { setExtraMenuOpen(false); setMenuOpen((open) => !open); }}
      >
        <Plus size={17} aria-hidden="true" />
      </button>
      {menuOpen ? <div id="workspace-workbench-add-menu" ref={menuRef} className="workspace-workbench-menu" role="menu" aria-label="添加工作区工具" onKeyDown={(event) => navigateMenu(event, menuRef.current)}>
        {primaryLauncherItems.map((item) => {
          const tool = item.id ? toolsById.get(item.id) : undefined;
          const enabled = Boolean(tool && isSelectableTool(tool));
          const Icon = item.icon;
          const reason = item.disabledReason ?? tool?.disabledReason;
          return <button
            type="button"
            role="menuitem"
            aria-label={item.label}
            key={item.label}
            disabled={!enabled}
            aria-keyshortcuts={item.shortcut === "Ctrl+Shift+G" ? "Control+Shift+G" : undefined}
            title={reason ?? item.label}
            onClick={() => { if (item.id && enabled) selectPanel(item.id); }}
          >
            <Icon size={15} aria-hidden="true" />
            <span>{item.label}</span>
            <small>{enabled ? item.shortcut : "未接通"}</small>
          </button>;
        })}
        <div className="workspace-workbench-menu-divider" role="separator" />
        <button type="button" role="menuitem" aria-label="更多 C-AICLI 工具" aria-haspopup="menu" onClick={() => { setMenuOpen(false); setExtraMenuOpen(true); }}>
          <MoreHorizontal size={15} aria-hidden="true" />
          <span>更多 C-AICLI 工具</span>
          <small>›</small>
        </button>
      </div> : null}
      {extraMenuOpen ? <div id="workspace-workbench-extra-menu" ref={extraMenuRef} className="workspace-workbench-menu workspace-workbench-extra-menu" role="menu" aria-label="更多 C-AICLI 工具" onKeyDown={(event) => navigateMenu(event, extraMenuRef.current)}>
        <button type="button" role="menuitem" aria-label="返回工作区工具" onClick={() => { setExtraMenuOpen(false); setMenuOpen(true); }}>
          <span aria-hidden="true">‹</span>
          <span>返回</span>
          <small />
        </button>
        <div className="workspace-workbench-menu-divider" role="separator" />
        <span className="workspace-workbench-menu-label">C-AICLI 工具</span>
        {extraTools.map((tool) => {
          const Icon = tool.icon;
          return <button
            type="button"
            role="menuitem"
            aria-label={tool.label}
            key={tool.id}
            disabled={!isSelectableTool(tool)}
            title={tool.disabledReason ?? tool.label}
            onClick={() => { if (isSelectableTool(tool)) selectPanel(tool.id); }}
          >
            <Icon size={15} aria-hidden="true" />
            <span>{tool.label}</span>
            <small>{tool.badge === undefined ? "" : formatToolBadge(tool.badge)}</small>
          </button>;
        })}
      </div> : null}
    </header>
    <WorkspacePanelHost
      {...panelProps}
      activePanel={activePanel}
      visible={visible}
      review={review}
      workspaceReady={workspaceReady}
      terminalCommands={terminalCommands}
      terminalController={terminalController}
    />
  </div>;
}

function workbenchLabel(tool: ToolDefinition) {
  if (tool.id === "changes") return "审阅";
  if (tool.id === "terminal") return "终端";
  return tool.label;
}
