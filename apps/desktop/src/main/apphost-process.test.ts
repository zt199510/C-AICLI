import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import { AppHostClient } from "./apphost-client";
import { resolveAppHostLaunch } from "./apphost-launch";

describe("AppHost process bridge", () => {
  let client: AppHostClient | null = null;
  const tempDirectories: string[] = [];

  afterEach(async () => {
    await client?.stop();
    for (const directory of tempDirectories.splice(0)) {
      fs.rmSync(directory, { force: true, recursive: true });
    }
  });

  it("handshakes, validates a workspace and exits cleanly", async () => {
    const desktopRoot = process.cwd();
    const repoRoot = path.resolve(desktopRoot, "../..");
    const workspaceRoot = fs.mkdtempSync(path.join(os.tmpdir(), "caicli-desktop-apphost-"));
    tempDirectories.push(workspaceRoot);
    const dotnetCandidate = path.join(os.homedir(), ".dotnet", "dotnet.exe");
    const dotnetHost = fs.existsSync(dotnetCandidate) ? dotnetCandidate : "dotnet";
    const appHostDll = path.join(
      repoRoot,
      "src",
      "CSharpAiCli.AppHost",
      "bin",
      "Release",
      "net9.0",
      "CSharpAiCli.AppHost.dll",
    );
    expect(fs.existsSync(appHostDll), "Run the Release .NET build before Desktop verification.").toBe(
      true,
    );
    client = new AppHostClient();

    const initialized = await client.start(
      resolveAppHostLaunch({
        appIsPackaged: false,
        appPath: desktopRoot,
        resourcesPath: path.join(desktopRoot, "resources"),
        environment: {
          CAICLI_DOTNET_HOST: dotnetHost,
          CAICLI_APPHOST_CONFIGURATION: "Release",
        },
      }),
    );
    const workspace = await client.openWorkspace(workspaceRoot);

    expect(initialized.protocolVersion).toBe("desktop-v1");
    expect(initialized.security.rendererNodeAccess).toBe(false);
    expect(workspace.succeeded).toBe(true);
    expect(workspace.data?.rootPath).toBe(workspaceRoot);
    await client.stop();
    expect(client.isRunning()).toBe(false);
  }, 15_000);

  it("reports an AppHost crash and clears the running state", async () => {
    client = new AppHostClient();

    await expect(
      client.start({
        command: process.execPath,
        args: ["-e", "process.exit(7)"],
        cwd: process.cwd(),
      }),
    ).rejects.toThrow("AppHost exited unexpectedly.");

    expect(client.isRunning()).toBe(false);
  });
});
