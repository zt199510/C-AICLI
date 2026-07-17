import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import { AppHostClient } from "./apphost-client";
import { resolveAppHostLaunch } from "./apphost-launch";
import { SCHEMA_VERSION } from "../generated/desktop-contracts";
import { THREAD_ARCHIVE_REQUEST, THREAD_CREATE_REQUEST, THREAD_RENAME_REQUEST } from "./desktop-requests";

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

  it("delivers successful thread mutation responses before ordered notifications", async () => {
    const desktopRoot = process.cwd();
    const workspaceRoot = fs.mkdtempSync(path.join(os.tmpdir(), "caicli-desktop-thread-"));
    tempDirectories.push(workspaceRoot);
    const dotnetCandidate = path.join(os.homedir(), ".dotnet", "dotnet.exe");
    const dotnetHost = fs.existsSync(dotnetCandidate) ? dotnetCandidate : "dotnet";
    client = new AppHostClient();
    const order: string[] = [];
    client.on("response", (method: string) => {
      if (method.startsWith("thread.")) order.push(`response:${method}`);
    });
    client.on("thread-changed", (event) => order.push(`notification:${event.changeKind}:${event.eventSequence}`));
    await client.start(resolveAppHostLaunch({
      appIsPackaged: false,
      appPath: desktopRoot,
      resourcesPath: path.join(desktopRoot, "resources"),
      environment: { CAICLI_DOTNET_HOST: dotnetHost, CAICLI_APPHOST_CONFIGURATION: "Release" },
    }));
    await client.openWorkspace(workspaceRoot);
    const created = await client.request(THREAD_CREATE_REQUEST, { schemaVersion: SCHEMA_VERSION, title: "Read-only review" });
    expect(created.succeeded).toBe(true);
    const thread = created.data;
    expect(thread).not.toBeNull();
    if (!thread) throw new Error("Thread was not created.");
    const renamed = await client.request(THREAD_RENAME_REQUEST, { schemaVersion: SCHEMA_VERSION, threadId: thread.threadId, expectedRevision: thread.revision, title: "Renamed review" });
    expect(renamed.succeeded).toBe(true);
    if (!renamed.data) throw new Error("Thread was not renamed.");
    const archived = await client.request(THREAD_ARCHIVE_REQUEST, { schemaVersion: SCHEMA_VERSION, threadId: thread.threadId, expectedRevision: renamed.data.revision });
    expect(archived.succeeded).toBe(true);
    const conflict = await client.request(THREAD_RENAME_REQUEST, { schemaVersion: SCHEMA_VERSION, threadId: thread.threadId, expectedRevision: thread.revision, title: "Stale rename" });
    expect(conflict.succeeded).toBe(false);
    await client.stop();
    expect(order).toEqual([
      `response:${THREAD_CREATE_REQUEST.method}`, "notification:created:1",
      `response:${THREAD_RENAME_REQUEST.method}`, "notification:renamed:2",
      `response:${THREAD_ARCHIVE_REQUEST.method}`, "notification:archived:3",
      `response:${THREAD_RENAME_REQUEST.method}`,
    ]);
  }, 20_000);
});
