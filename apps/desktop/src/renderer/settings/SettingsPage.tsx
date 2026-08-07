import type { LocalSettings } from "./local-settings";

export function SettingsPage({ user, workspace, onUser, onWorkspace, onClose }: {
  readonly user: LocalSettings;
  readonly workspace: Partial<LocalSettings>;
  readonly onUser: (value: LocalSettings) => void;
  readonly onWorkspace: (value: Partial<LocalSettings>) => void;
  readonly onClose: () => void;
}) {
  const userField = <K extends keyof LocalSettings>(key: K, value: LocalSettings[K]) => onUser({ ...user, [key]: value });
  const workspaceField = <K extends keyof LocalSettings>(key: K, value: LocalSettings[K] | undefined) => onWorkspace({ ...workspace, [key]: value });
  return <section className="settings-page" aria-label="设置">
    <header><div><span>本地偏好</span><h1>设置</h1></div><button onClick={onClose}>返回任务</button></header>
    <div className="settings-columns"><section><h2>用户默认值</h2>
      <label>语言<select value={user.language} onChange={(event) => userField("language", event.target.value as LocalSettings["language"])}><option value="zh-CN">简体中文</option><option value="en-US">English</option></select></label>
      <label>主题<select value={user.theme} onChange={(event) => userField("theme", event.target.value as LocalSettings["theme"])}><option value="system">跟随系统</option><option value="light">浅色</option><option value="dark">深色</option></select></label>
      <label>默认 Shell<select value={user.defaultShell} onChange={(event) => userField("defaultShell", event.target.value as LocalSettings["defaultShell"])}><option value="system-default">PowerShell（默认）</option><option value="powershell">PowerShell</option><option value="cmd">命令提示符</option><option value="wsl">WSL</option><option value="git-bash">Git Bash</option></select></label>
      <label>默认模型<input value={user.model} onChange={(event) => userField("model", event.target.value)} placeholder="使用运行时默认值" /></label>
      <label>审批<select value={user.approval} onChange={(event) => userField("approval", event.target.value as LocalSettings["approval"])}><option value="read-only">只读</option><option value="on-request">按需审批</option><option value="trusted-local">受信任本地</option></select></label>
      <label><input type="checkbox" checked={user.shortcuts} onChange={(event) => userField("shortcuts", event.target.checked)} /> 启用键盘快捷键</label>
    </section><section><h2>工作区覆盖</h2><p>留空会继承用户默认值。Model、Approval 与禁用工具只影响下一次发送，随 Composer 一起交给 AppHost 权威执行。</p>
      <label>模型覆盖<input value={workspace.model ?? ""} onChange={(event) => workspaceField("model", event.target.value || undefined)} /></label>
      <label>审批覆盖<select value={workspace.approval ?? ""} onChange={(event) => workspaceField("approval", (event.target.value || undefined) as LocalSettings["approval"] | undefined)}><option value="">继承</option><option value="read-only">只读</option><option value="on-request">按需审批</option><option value="trusted-local">受信任本地</option></select></label>
      <label>禁用工具<textarea value={(workspace.disabledTools ?? []).join("\n")} onChange={(event) => workspaceField("disabledTools", event.target.value.split(/\r?\n/).map((value) => value.trim()).filter(Boolean))} placeholder="每行一个工具 ID" /></label>
      <label>Git / PR 基线<input value={workspace.gitBase ?? ""} onChange={(event) => workspaceField("gitBase", event.target.value || undefined)} /></label>
    </section></div>
    <footer>凭据不会进入此页面或设置 JSON；Git 与 GitHub 认证由 Windows Credential Manager / gh 管理。</footer>
  </section>;
}
