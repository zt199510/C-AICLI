import { expect, test, type Page } from "@playwright/test";
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import type { ChangesData, ChangesMutateResult } from "../src/generated/desktop-contracts";
import { cleanupDesktopCase, createDesktopCase, launchDesktop, openWorkspace } from "./desktop-harness";

test("Changes mutations fail closed and preserve Git safety semantics", async ({ browserName }, testInfo) => {
  if (browserName !== "chromium") throw new Error("Electron tests require Chromium.");
  testInfo.setTimeout(90_000);
  const desktop = createDesktopCase("changes-write", false);
  const tracked = path.join(desktop.workspace, "tracked.txt");
  const untracked = path.join(desktop.workspace, "untracked.txt");
  const hookMarker = path.join(desktop.workspace, "hook-ran.txt");
  const remote = path.join(desktop.root, "remote.git");
  initializeRepository(desktop.workspace, tracked, remote);
  fs.appendFileSync(tracked, "unstaged-one\n", "utf8");
  fs.writeFileSync(untracked, "never delete me\n", "utf8");

  try {
    const { page } = await launchDesktop(desktop);
    await openWorkspace(page);
    const initial = await changes(page);
    expect(initial.files?.some((file) => file.path === "tracked.txt" && file.area === "unstaged")).toBe(true);
    expect(initial.files?.some((file) => file.path === "untracked.txt" && file.area === "untracked")).toBe(true);

    const deniedUntracked = await mutate(page, initial, { action: "revert", path: "untracked.txt", area: "untracked", confirmed: true }, "deny-untracked");
    expect(deniedUntracked.succeeded).toBe(false);
    expect(deniedUntracked.error?.code).toBe("changes-untracked-revert-forbidden");
    expect(fs.readFileSync(untracked, "utf8")).toContain("never delete me");

    const staged = await requireMutation(page, initial, { action: "stage", path: "tracked.txt", area: "unstaged" }, "stage-file");
    expect(staged.files?.some((file) => file.path === "tracked.txt" && file.area === "staged")).toBe(true);
    const stale = await mutate(page, initial, { action: "unstage", path: "tracked.txt", area: "staged" }, "stale-unstage");
    expect(stale.succeeded).toBe(false);
    expect(stale.error?.code).toBe("changes-stale");

    const unstaged = await requireMutation(page, staged, { action: "unstage", path: "tracked.txt", area: "staged" }, "unstage-file");
    const deniedRevert = await mutate(page, unstaged, { action: "revert", path: "tracked.txt", area: "unstaged", confirmed: false }, "deny-revert");
    expect(deniedRevert.error?.code).toBe("changes-confirmation-required");
    const reverted = await requireMutation(page, unstaged, { action: "revert", path: "tracked.txt", area: "unstaged", confirmed: true }, "revert-file");
    expect(reverted.files?.some((file) => file.path === "tracked.txt")).toBe(false);

    fs.appendFileSync(tracked, "commit-with-hook\n", "utf8");
    await expect.poll(async () => (await changes(page)).revision, { timeout: 10_000 }).not.toBe(reverted.revision);
    const refreshed = await changes(page);
    expect(refreshed.files?.some((file) => file.path === "tracked.txt" && file.area === "unstaged")).toBe(true);
    const readyToCommit = await requireMutation(page, refreshed, { action: "stage", path: "tracked.txt", area: "unstaged" }, "stage-for-commit");
    const deniedCommit = await mutate(page, readyToCommit, { action: "commit", message: "E2E safe commit", confirmed: false }, "deny-commit");
    expect(deniedCommit.error?.code).toBe("changes-confirmation-required");
    const committed = await requireMutation(page, readyToCommit, { action: "commit", message: "E2E safe commit", confirmed: true }, "commit");
    expect(committed.head).not.toBe(readyToCommit.head);
    expect(fs.readFileSync(hookMarker, "utf8")).toBe("hook-ran");

    const pushed = await requireMutation(page, committed, { action: "push", setUpstream: true, confirmed: true }, "push-upstream");
    expect(pushed.upstream).toBe("origin/main");
    expect(runGit(remote, "rev-parse", "refs/heads/main").trim()).toBe(pushed.head);
    expect(pushed.compareBase).toBe("origin/main");

    const withWorktree = await requireMutation(page, pushed, { action: "create-worktree", targetBranch: "caicli/e2e-worktree", threadId: "thread-e2e", confirmed: true }, "create-worktree");
    const worktree = withWorktree.worktrees?.find((item) => item.branch === "caicli/e2e-worktree");
    expect(worktree?.path).toContain("C-AICLI");
    expect(worktree && fs.existsSync(worktree.path)).toBe(true);
    const withoutWorktree = await requireMutation(page, withWorktree, { action: "remove-worktree", worktreeId: worktree?.worktreeId, baseBranch: "main", confirmed: true }, "remove-worktree");
    expect(withoutWorktree.worktrees).toHaveLength(0);
    expect(worktree && fs.existsSync(worktree.path)).toBe(false);
    const withoutBranch = await requireMutation(page, withoutWorktree, { action: "remove-branch", targetBranch: "caicli/e2e-worktree", baseBranch: "main", confirmed: true }, "remove-branch");
    expect(withoutBranch.branches).not.toContain("caicli/e2e-worktree");
  } finally {
    await cleanupDesktopCase(desktop, testInfo);
  }
});

type Mutation = {
  action: "stage" | "unstage" | "revert" | "commit" | "push" | "create-branch" | "switch-branch" | "create-worktree" | "remove-worktree" | "remove-branch";
  path?: string; area?: string; hunkId?: string; message?: string; setUpstream?: boolean; confirmed?: boolean;
  targetBranch?: string; baseBranch?: string; threadId?: string; worktreeId?: string;
};

async function changes(page: Page): Promise<ChangesData> {
  return await page.evaluate(async () => {
    const result = await window.caicli.getChanges({});
    if (!result.succeeded || !result.data) throw new Error(result.error?.safeMessage ?? "Changes unavailable.");
    return result.data;
  });
}

async function mutate(page: Page, snapshot: ChangesData, value: Mutation, id: string): Promise<ChangesMutateResult> {
  if (!snapshot.workspaceId || !snapshot.repositoryId || !snapshot.revision) throw new Error("Authoritative Git identity is missing.");
  return await page.evaluate(async ({ command }) => window.caicli.mutateChanges(command), { command: {
    workspaceId: snapshot.workspaceId,
    repositoryId: snapshot.repositoryId,
    expectedRevision: snapshot.revision,
    action: value.action,
    path: value.path ?? null,
    area: value.area ?? null,
    hunkId: value.hunkId ?? null,
    message: value.message ?? null,
    setUpstream: value.setUpstream ?? false,
    confirmed: value.confirmed ?? true,
    clientMutationId: `e2e-${id}`,
    targetBranch: value.targetBranch ?? null,
    baseBranch: value.baseBranch ?? null,
    threadId: value.threadId ?? null,
    worktreeId: value.worktreeId ?? null,
  } });
}

async function requireMutation(page: Page, snapshot: ChangesData, value: Mutation, id: string): Promise<ChangesData> {
  const result = await mutate(page, snapshot, value, id);
  if (!result.succeeded || !result.data) throw new Error(`[${result.error?.code ?? "unknown"}] ${result.error?.safeMessage ?? "Git action failed."}`);
  return result.data.changes;
}

function initializeRepository(workspace: string, tracked: string, remote: string): void {
  runGit(workspace, "init", "-b", "main");
  runGit(workspace, "config", "user.email", "desktop-e2e@example.invalid");
  runGit(workspace, "config", "user.name", "Desktop E2E");
  fs.writeFileSync(tracked, "initial\n", "utf8");
  runGit(workspace, "add", ".");
  runGit(workspace, "commit", "-m", "initial");
  runGit(desktopParent(remote), "init", "--bare", remote);
  runGit(workspace, "remote", "add", "origin", remote);
  const hook = path.join(workspace, ".git", "hooks", "pre-commit");
  fs.writeFileSync(hook, "#!/bin/sh\nprintf hook-ran > hook-ran.txt\n", { encoding: "utf8", mode: 0o755 });
}

function desktopParent(target: string): string { return path.dirname(target); }

function runGit(workingDirectory: string, ...args: string[]): string {
  const result = spawnSync("git", args, { cwd: workingDirectory, encoding: "utf8", windowsHide: true });
  if (result.status !== 0) throw new Error(`git ${args.join(" ")} failed: ${result.stderr}`);
  return result.stdout;
}
