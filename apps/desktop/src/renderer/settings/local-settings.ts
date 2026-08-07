import { useEffect, useMemo, useState } from "react";
import type { ApprovalPreference, DesktopLocalSettings, ThemePreference } from "../../shared/bridge-contract";

export type { ApprovalPreference, ThemePreference };
export type LocalSettings = DesktopLocalSettings;

export const defaultLocalSettings: LocalSettings = {
  language: "zh-CN", theme: "system", defaultShell: "system-default", model: "", approval: "on-request",
  shortcuts: true, summaryDefault: true, bottomDefault: true, toolsDefault: true, gitBase: "main", navigationWidth: 288, inspectorWidth: 640, disabledTools: [],
};

export function useLocalSettings(workspaceId: string | null) {
  const [user, setUserState] = useState<LocalSettings>(defaultLocalSettings);
  const [workspace, setWorkspaceState] = useState<Partial<LocalSettings>>({});

  useEffect(() => {
    let current = true;
    void window.caicli.getSettings({ workspaceId }).then((snapshot) => {
      if (!current) return;
      setUserState(snapshot.user);
      setWorkspaceState(snapshot.workspace);
    }).catch(() => undefined);
    return () => { current = false; };
  }, [workspaceId]);

  const effective = useMemo(() => ({ ...user, ...workspace }), [user, workspace]);
  useEffect(() => {
    document.documentElement.lang = effective.language;
    document.documentElement.dataset.theme = effective.theme;
  }, [effective.language, effective.theme]);

  return {
    user, workspace, effective,
    setUser(value: LocalSettings) {
      setUserState(value);
      void window.caicli.setSettings({ scope: "user", workspaceId, value }).then((snapshot) => setUserState(snapshot.user)).catch(() => undefined);
    },
    setWorkspace(value: Partial<LocalSettings>) {
      setWorkspaceState(value);
      if (workspaceId) void window.caicli.setSettings({ scope: "workspace", workspaceId, value }).then((snapshot) => setWorkspaceState(snapshot.workspace)).catch(() => undefined);
    },
  };
}
