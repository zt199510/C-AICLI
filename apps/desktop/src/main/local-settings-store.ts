import fs from "node:fs";
import path from "node:path";
import {
  isLocalSettings,
  type DesktopLocalSettings,
  type DesktopSettingsSnapshot,
  type SetSettingsCommand,
} from "../shared/bridge-contract";

export const defaultDesktopSettings: DesktopLocalSettings = Object.freeze({
  language: "zh-CN", theme: "system", defaultShell: "system-default", model: "", approval: "on-request",
  shortcuts: true, summaryDefault: true, bottomDefault: true, toolsDefault: true, gitBase: "main", navigationWidth: 288, inspectorWidth: 640, disabledTools: [],
});

interface SettingsFile {
  readonly schemaVersion: 1;
  readonly user: DesktopLocalSettings;
  readonly workspaces: Readonly<Record<string, Partial<DesktopLocalSettings>>>;
}

export class LocalSettingsStore {
  constructor(private readonly filePath: string | null) {}

  get(workspaceId: string | null): DesktopSettingsSnapshot {
    const current = this.read();
    return { schemaVersion: 1, user: current.user, workspace: workspaceId ? current.workspaces[workspaceId] ?? {} : {} };
  }

  set(command: SetSettingsCommand): DesktopSettingsSnapshot {
    const current = this.read();
    const next: SettingsFile = command.scope === "user"
      ? { ...current, user: { ...defaultDesktopSettings, ...command.value } }
      : { ...current, workspaces: { ...current.workspaces, [command.workspaceId!]: { ...current.workspaces[command.workspaceId!], ...command.value } } };
    if (this.filePath) {
      fs.mkdirSync(path.dirname(this.filePath), { recursive: true });
      const temporary = `${this.filePath}.${process.pid}.tmp`;
      fs.writeFileSync(temporary, `${JSON.stringify(next, null, 2)}\n`, { encoding: "utf8", mode: 0o600 });
      fs.renameSync(temporary, this.filePath);
    }
    return { schemaVersion: 1, user: next.user, workspace: command.workspaceId ? next.workspaces[command.workspaceId] ?? {} : {} };
  }

  private read(): SettingsFile {
    if (!this.filePath || !fs.existsSync(this.filePath)) return { schemaVersion: 1, user: defaultDesktopSettings, workspaces: {} };
    try {
      const value = JSON.parse(fs.readFileSync(this.filePath, "utf8")) as Partial<SettingsFile>;
      const user = isLocalSettings(value.user, false) ? value.user as DesktopLocalSettings : defaultDesktopSettings;
      const workspaces: Record<string, Partial<DesktopLocalSettings>> = {};
      if (value.workspaces && typeof value.workspaces === "object" && !Array.isArray(value.workspaces)) {
        for (const [key, settings] of Object.entries(value.workspaces).slice(0, 100)) {
          if (key.length <= 128 && isLocalSettings(settings, true)) workspaces[key] = settings;
        }
      }
      return { schemaVersion: 1, user, workspaces };
    } catch {
      return { schemaVersion: 1, user: defaultDesktopSettings, workspaces: {} };
    }
  }
}
