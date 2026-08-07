using System.Diagnostics;
using CSharpAiCli.AppHost.Protocol;
using CSharpAiCli.AppHost.Protocol.Generated;

namespace CSharpAiCli.Tests;

public sealed class DesktopGitChangesSupervisorTests
{
    [Fact]
    public void Changes_query_and_file_mutations_are_revision_guarded()
    {
        using GitRepository repository = new();
        File.AppendAllText(Path.Combine(repository.Root, "tracked.txt"), "changed\n");
        File.WriteAllText(Path.Combine(repository.Root, "untracked.txt"), "keep\n");
        DesktopGitChangesSupervisor supervisor = new();
        supervisor.Reset("workspace-test", repository.Root);

        ChangesGetResult queried = supervisor.Query();

        Assert.True(queried.Succeeded, queried.Error?.SafeMessage);
        ChangesData before = Assert.IsType<ChangesData>(queried.Data);
        Assert.Contains(before.Files!, file => file.Path == "tracked.txt" && file.Area == "unstaged");
        Assert.Contains(before.Files!, file => file.Path == "untracked.txt" && file.Area == "untracked");
        ChangesMutateResult stage = supervisor.Mutate(Command(before, "stage", "stage-1", "tracked.txt", "unstaged"));
        Assert.True(stage.Succeeded, stage.Error?.SafeMessage);
        Assert.Contains(stage.Data!.Changes.Files!, file => file.Path == "tracked.txt" && file.Area == "staged");
        ChangesMutateResult stale = supervisor.Mutate(Command(before, "unstage", "stale-1", "tracked.txt", "staged"));
        Assert.False(stale.Succeeded);
        Assert.Equal("changes-stale", stale.Error?.Code);
        ChangesMutateResult untracked = supervisor.Mutate(Command(stage.Data.Changes, "revert", "untracked-1", "untracked.txt", "untracked", confirmed: true));
        Assert.False(untracked.Succeeded);
        Assert.Equal("changes-untracked-revert-forbidden", untracked.Error?.Code);
        Assert.True(File.Exists(Path.Combine(repository.Root, "untracked.txt")));
    }

    private static ChangesMutateParams Command(ChangesData changes, string action, string mutationId, string? path, string? area, bool confirmed = true) => new()
    {
        SchemaVersion = 1,
        WorkspaceId = changes.WorkspaceId!,
        RepositoryId = changes.RepositoryId!,
        ExpectedRevision = changes.Revision!,
        Action = action,
        Path = path,
        Area = area,
        HunkId = null,
        Message = null,
        SetUpstream = false,
        Confirmed = confirmed,
        ClientMutationId = mutationId
    };

    private sealed class GitRepository : IDisposable
    {
        public GitRepository()
        {
            Root = Path.Combine(Path.GetTempPath(), "caicli-git-changes-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Run("init", "-b", "main");
            Run("config", "user.email", "test@example.invalid");
            Run("config", "user.name", "Test User");
            File.WriteAllText(Path.Combine(Root, "tracked.txt"), "initial\n");
            Run("add", ".");
            Run("commit", "-m", "initial");
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
            }
            for (int attempt = 0; attempt < 20 && Directory.Exists(Root); attempt++)
            {
                try { Directory.Delete(Root, recursive: true); }
                catch (UnauthorizedAccessException) when (attempt < 19) { Thread.Sleep(50); }
                catch (IOException) when (attempt < 19) { Thread.Sleep(50); }
            }
        }

        private void Run(params string[] arguments)
        {
            using Process process = new() { StartInfo = new ProcessStartInfo("git") { WorkingDirectory = Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
            foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, stdout + stderr);
        }
    }
}
