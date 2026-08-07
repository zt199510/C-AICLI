using CSharpAiCli.AppHost.Protocol;
using CSharpAiCli.AppHost.Protocol.Generated;
using CSharpAiCli.Application;
using System.Diagnostics;

namespace CSharpAiCli.Tests;

public sealed class DesktopSubagentSupervisorTests
{
    [Fact]
    public void Read_only_agents_are_explicit_bounded_non_nesting_and_cancelable()
    {
        string root = Path.Combine(Path.GetTempPath(), "caicli-subagent-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            DesktopApplicationSessionOpenResult opened = new DesktopApplicationSessionFactory().Open(root);
            using DesktopApplicationSession session = Assert.IsType<DesktopApplicationSession>(opened.Session);
            ThreadSummaryProjection parent = Assert.IsType<ThreadSummaryProjection>(session.CreateThread("parent").Data);
            DesktopGitChangesSupervisor changes = new();
            changes.Reset(session.Workspace.WorkspaceId, session.Workspace.RootPath);
            using DesktopSubagentSupervisor supervisor = new(new DesktopApplicationSessionFactory(), new DeterministicFakeTurnExecutionRuntime(), changes);

            SubagentResult first = supervisor.Start(session, Start(parent.ThreadId, "first [pause]", "start-1"));
            SubagentResult second = supervisor.Start(session, Start(parent.ThreadId, "second [pause]", "start-2"));
            SubagentResult third = supervisor.Start(session, Start(parent.ThreadId, "third [pause]", "start-3"));
            Assert.True(first.Succeeded && second.Succeeded && third.Succeeded);
            Assert.Equal(3, third.Data!.Agents.Count);

            SubagentResult excess = supervisor.Start(session, Start(parent.ThreadId, "fourth [pause]", "start-4"));
            Assert.False(excess.Succeeded);
            Assert.Equal("subagent-limit", excess.Error!.Code);

            SubagentData child = first.Data!.Agents[0];
            SubagentResult nested = supervisor.Start(session, Start(child.ThreadId, "nested", "start-nested"));
            Assert.False(nested.Succeeded);
            Assert.Equal("subagent-nesting-denied", nested.Error!.Code);

            SubagentResult canceled = supervisor.Cancel(new SubagentMutationParams
            {
                SchemaVersion = 1,
                AgentId = child.AgentId,
                Confirmed = true,
                ClientMutationId = "cancel-1"
            }, takeover: false);
            Assert.True(canceled.Succeeded);
            Assert.Equal("canceled", canceled.Data!.Agents.Single(value => value.AgentId == child.AgentId).Status);
            Assert.Null(child.WorktreeId);
        }
        finally
        {
            DeleteTree(root);
        }
    }

    [Fact]
    public void Write_agent_owns_an_external_managed_worktree_that_takeover_preserves()
    {
        string root = Path.Combine(Path.GetTempPath(), "caicli-subagent-git-" + Guid.NewGuid().ToString("N"));
        string managedRoot = Path.Combine(Path.GetTempPath(), "caicli-subagent-worktree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string? worktreePath = null;
        try
        {
            Git(root, "init");
            Git(root, "config", "user.email", "desktop-tests@example.invalid");
            Git(root, "config", "user.name", "Desktop Tests");
            File.WriteAllText(Path.Combine(root, "README.md"), "fixture");
            Git(root, "add", "README.md");
            Git(root, "commit", "-m", "fixture");

            DesktopApplicationSessionOpenResult opened = new DesktopApplicationSessionFactory().Open(root);
            using DesktopApplicationSession session = Assert.IsType<DesktopApplicationSession>(opened.Session);
            ThreadSummaryProjection parent = Assert.IsType<ThreadSummaryProjection>(session.CreateThread("parent").Data);
            DesktopGitChangesSupervisor changes = new(managedRoot);
            changes.Reset(session.Workspace.WorkspaceId, session.Workspace.RootPath);
            using DesktopSubagentSupervisor supervisor = new(new DesktopApplicationSessionFactory(), new DeterministicFakeTurnExecutionRuntime(), changes);

            SubagentResult started = supervisor.Start(session, new SubagentStartParams
            {
                SchemaVersion = 1,
                ParentThreadId = parent.ThreadId,
                Prompt = "write fixture [pause]",
                Mode = "write",
                Confirmed = true,
                ClientMutationId = "write-start"
            });
            Assert.True(started.Succeeded, started.Error?.SafeMessage);
            SubagentData agent = Assert.Single(started.Data!.Agents);
            worktreePath = agent.WorkspacePath;
            Assert.NotNull(agent.WorktreeId);
            Assert.StartsWith("caicli/subagent-", agent.Branch, StringComparison.Ordinal);
            Assert.True(Directory.Exists(worktreePath));
            Assert.NotEqual(Path.GetFullPath(root), Path.GetFullPath(worktreePath));

            SubagentResult takeover = supervisor.Cancel(new SubagentMutationParams
            {
                SchemaVersion = 1,
                AgentId = agent.AgentId,
                Confirmed = true,
                ClientMutationId = "takeover"
            }, takeover: true);
            Assert.True(takeover.Succeeded);
            Assert.Equal("taken-over", Assert.Single(takeover.Data!.Agents).Status);
            Assert.True(Directory.Exists(worktreePath));
        }
        finally
        {
            if (worktreePath is not null && Directory.Exists(worktreePath))
                Git(root, "worktree", "remove", "--force", worktreePath);
            DeleteTree(root);
            DeleteTree(managedRoot);
        }
    }

    private static SubagentStartParams Start(string parentThreadId, string prompt, string mutationId) => new()
    {
        SchemaVersion = 1,
        ParentThreadId = parentThreadId,
        Prompt = prompt,
        Mode = "read-only",
        Confirmed = true,
        ClientMutationId = mutationId
    };

    private static void Git(string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo start = new("git") { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }

    private static void DeleteTree(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
