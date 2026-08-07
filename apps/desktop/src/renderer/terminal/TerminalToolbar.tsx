import { Copy, Plus, RefreshCw, Square, X } from "lucide-react";
import { useEffect, useState } from "react";
import type { TerminalProfileData, TerminalStateData } from "../../generated/desktop-contracts";
import type { TerminalProfileId } from "../../shared/bridge-contract";
import type { XtermAdapter } from "./XtermAdapter";

export function TerminalToolbar({
  session,
  profiles,
  canOpen,
  busy,
  adapter,
  onOpen,
  onInterrupt,
  onReconnect,
  onClose,
  onShareSelection,
  defaultProfile = "system-default",
  showOpenButton = true,
}: {
  readonly session: TerminalStateData | null;
  readonly profiles: readonly TerminalProfileData[];
  readonly canOpen: boolean;
  readonly busy: boolean;
  readonly adapter: XtermAdapter | null;
  readonly onOpen: (profile: TerminalProfileId) => void;
  readonly onInterrupt: () => void;
  readonly onReconnect: () => void;
  readonly onClose: () => void;
  readonly onShareSelection?: (text: string) => void;
  readonly defaultProfile?: TerminalProfileId;
  readonly showOpenButton?: boolean;
}) {
  const [profile, setProfile] = useState<TerminalProfileId>(defaultProfile);
  useEffect(() => setProfile(defaultProfile), [defaultProfile]);
  const [sharePreview, setSharePreview] = useState<string | null>(null);
  const running = session?.status === "running";

  async function copySelection() {
    const value = adapter?.selectedText() ?? "";
    if (value) await navigator.clipboard.writeText(value);
  }

  async function pasteClipboard() {
    const value = await navigator.clipboard.readText();
    if (value) adapter?.paste(value);
  }

  function previewShare() {
    const selected = adapter?.selectedText() ?? "";
    if (!selected) return;
    const bounded = selected.slice(0, 8192);
    setSharePreview(bounded
      .replace(/\b(?:AKIA[0-9A-Z]{16}|gh[pousr]_[A-Za-z0-9_]{20,}|sk-[A-Za-z0-9_-]{16,})\b/g, "[REDACTED]")
      .replace(/\b(api[_-]?key|token|password|secret)\s*[:=]\s*[^\s]+/gi, "$1=[REDACTED]") + (selected.length > 8192 ? "\n[TRUNCATED]" : ""));
  }

  return <div className="terminal-toolbar" aria-label="Terminal toolbar">
    <select aria-label="Shell profile" value={profile} disabled={!canOpen || busy || profiles.length === 0} onChange={(event) => setProfile(event.target.value as TerminalProfileId)}>
      {profiles.map((item) => <option key={item.profileId} value={item.profileId}>{item.displayName}</option>)}
    </select>
    {showOpenButton ? <button type="button" disabled={!canOpen || busy} onClick={() => onOpen(profile)}><Plus size={14} aria-hidden="true" />新建终端</button> : null}
    <span className="terminal-toolbar-spacer" />
    <button type="button" disabled={!session || busy} onClick={() => void copySelection()}><Copy size={14} aria-hidden="true" />复制所选</button>
    <button type="button" disabled={!session || busy || !onShareSelection} onClick={previewShare}>分享所选输出</button>
    <button type="button" disabled={!running || busy} onClick={() => void pasteClipboard()}>粘贴</button>
    <button type="button" disabled={!running || busy} onClick={onInterrupt}><Square size={14} aria-hidden="true" />Ctrl+C</button>
    <button type="button" disabled={!session || running || busy} onClick={onReconnect}><RefreshCw size={14} aria-hidden="true" />重连</button>
    <button type="button" disabled={!session || busy} onClick={onClose}><X size={14} aria-hidden="true" />关闭</button>
    {sharePreview !== null ? <div className="terminal-share-preview" role="alertdialog" aria-label="分享终端所选输出" aria-modal="false">
      <h3>分享预览</h3><p>只会把当前选择加入尚未发送的 Composer；已自动脱敏并限制为 8 KiB。不会分享完整会话。</p>
      <textarea readOnly value={sharePreview} aria-label="Sanitized terminal selection" />
      <div><button type="button" onClick={() => setSharePreview(null)}>取消</button><button type="button" onClick={() => { onShareSelection?.(sharePreview); setSharePreview(null); }}>加入 Composer</button></div>
    </div> : null}
  </div>;
}
