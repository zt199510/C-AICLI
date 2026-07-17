import { EventEmitter } from "node:events";
import { describe, expect, it, vi } from "vitest";
import type { InitializeResult, WorkspaceOpenResult } from "../generated/desktop-contracts";
import { AppHostRuntime, type RuntimeClient } from "./apphost-runtime";
import type { DesktopRequestDescriptor } from "./desktop-requests";

describe("AppHost runtime", () => {
  it("starts once and publishes a safe ready snapshot", async () => {
    const client = new FakeClient();
    const runtime = createRuntime([client]);
    const states: string[] = [];
    runtime.subscribe((status) => states.push(status.state));
    await Promise.all([runtime.start(), runtime.start()]);
    expect(client.start).toHaveBeenCalledOnce();
    expect(runtime.getStatus().code).toBe("runtime-ready");
    expect(states).toEqual(["stopped", "starting", "ready"]);
  });

  it("does not auto restart after an unexpected exit", async () => {
    const client = new FakeClient();
    const runtime = createRuntime([client]);
    await runtime.start();
    client.emit("exit");
    expect(runtime.getStatus().code).toBe("apphost-exited");
    expect(runtime.getStatus().canRestart).toBe(true);
    expect(client.start).toHaveBeenCalledOnce();
  });

  it("performs one explicit restart", async () => {
    const first = new FakeClient();
    const second = new FakeClient();
    const runtime = createRuntime([first, second]);
    await runtime.start();
    first.emit("exit");
    await Promise.all([runtime.restart(), runtime.restart()]);
    expect(second.start).toHaveBeenCalledOnce();
    expect(runtime.getStatus().state).toBe("ready");
  });

  it("ignores a late exit from an old generation", async () => {
    const first = new FakeClient();
    const second = new FakeClient();
    const runtime = createRuntime([first, second]);
    await runtime.start();
    first.emit("exit");
    await runtime.restart();
    first.emit("exit");
    expect(runtime.getStatus().state).toBe("ready");
  });

  it("rejects workspace access unless ready and stops idempotently", async () => {
    const client = new FakeClient();
    const runtime = createRuntime([client]);
    await expect(runtime.openWorkspace("sentinel-secret-path")).rejects.toThrow("not ready");
    await runtime.start();
    await runtime.openWorkspace("sentinel-secret-path");
    expect(client.openWorkspace).toHaveBeenCalledWith("sentinel-secret-path");
    await Promise.all([runtime.stop(), runtime.stop()]);
    expect(runtime.getStatus().code).toBe("runtime-stopped");
    expect(client.stop).toHaveBeenCalledOnce();
  });

  it("retains only the last successful canonical workspace snapshot and clears it on crash", async () => {
    const client = new FakeClient();
    client.openWorkspace
      .mockResolvedValueOnce(workspaceResult("workspace-1"))
      .mockResolvedValueOnce({ schemaVersion: 1, succeeded: false, data: null, error: { code: "invalid", category: "workspace", safeMessage: "bad", retryable: false }, diagnostics: [], truncated: false });
    const runtime = createRuntime([client]);
    await runtime.start();
    await runtime.openWorkspace("C:\\first");
    expect(runtime.getWorkspaceSnapshot()?.workspaceId).toBe("workspace-1");
    await runtime.openWorkspace("C:\\failed");
    expect(runtime.getWorkspaceSnapshot()?.workspaceId).toBe("workspace-1");
    client.emit("exit");
    expect(runtime.getWorkspaceSnapshot()).toBeNull();
  });
});

class FakeClient extends EventEmitter implements RuntimeClient {
  start = vi.fn<() => Promise<InitializeResult>>().mockResolvedValue({} as InitializeResult);
  stop = vi.fn<() => Promise<void>>().mockResolvedValue();
  openWorkspace = vi.fn<(path: string) => Promise<WorkspaceOpenResult>>().mockResolvedValue({} as WorkspaceOpenResult);
  forceTerminateForTest = vi.fn();
  request<TParams extends object, TResult>(
    descriptor: DesktopRequestDescriptor<TParams, TResult>, parameters: TParams,
  ): Promise<TResult> { return Promise.reject(new Error(`unused ${descriptor.method} ${Object.keys(parameters).length}`)); }
}

function workspaceResult(workspaceId: string): WorkspaceOpenResult {
  return {
    schemaVersion: 1, succeeded: true,
    data: {
      workspaceId, rootPath: "C:\\workspace", status: "ready",
      capabilities: { readOnlyQueries: true, gitQueries: true, localCatalogs: true, managedArtifacts: true },
      configuration: { hasApiKey: false, apiKeySource: "none", modelSource: "default", agentBackendSource: "default", approvalMode: "ask", approvalModeSource: "default", loadedSourceCount: 0 },
    },
    error: null, diagnostics: [], truncated: false,
  };
}

function createRuntime(clients: FakeClient[]): AppHostRuntime {
  return new AppHostRuntime({
    createClient: () => {
      const next = clients.shift();
      if (!next) throw new Error("No fake client available");
      return next;
    },
    resolveLaunch: () => ({ command: "fake", args: [], cwd: "." }),
  });
}
