using System.Diagnostics;

namespace CSharpAiCli.Core;

public sealed class GitDirtyWorkspaceDetector : IDirtyWorkspaceDetector
{
    private const int TimeoutMilliseconds = 2000;

    public DirtyWorkspaceStatus Detect(WorkspaceContext workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (!workspace.IsUsable)
        {
            return new DirtyWorkspaceStatus(false, "workspace unavailable");
        }

        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo("git", "status --porcelain")
                {
                    WorkingDirectory = workspace.RootPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            process.Start();
            if (!process.WaitForExit(TimeoutMilliseconds))
            {
                TryKill(process);
                return new DirtyWorkspaceStatus(false, "git status unavailable");
            }

            string output = process.StandardOutput.ReadToEnd();
            if (process.ExitCode != 0)
            {
                return new DirtyWorkspaceStatus(false, "not a git workspace");
            }

            int changedLines = output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Length;
            return changedLines == 0
                ? new DirtyWorkspaceStatus(false, "clean")
                : new DirtyWorkspaceStatus(true, $"{changedLines} changed path(s)");
        }
        catch
        {
            return new DirtyWorkspaceStatus(false, "git status unavailable");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }
}
