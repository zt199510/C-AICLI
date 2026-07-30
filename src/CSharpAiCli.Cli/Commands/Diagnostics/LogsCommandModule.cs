using System.CommandLine;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class LogsCommandModule : ICliCommandModule
{
    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("logs", "Inspect CLI log files.");
        Command pathCommand = new("path", "Print the resolved CLI log directory.");
        pathCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
            if (Directory.Exists(logDirectory))
            {
                context.TryWriteCommandLog("logs path", snapshot);
            }

            context.WriteVerboseDiagnostics(parseResult, "logs path", snapshot);
            context.Output.WriteLine(logDirectory);
            return 0;
        });
        command.Subcommands.Add(pathCommand);

        Command showCommand = new("show", "Print CLI log lines.");
        Option<int?> tailOption = new("--tail")
        {
            Description = "Print the last number of log lines.",
        };
        tailOption.DefaultValueFactory = _ => 20;
        CliCommandContext.AddPositiveIntegerValidator(tailOption, "--tail");
        showCommand.Options.Add(tailOption);
        showCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            int tailCount = parseResult.GetValue(tailOption) ?? 20;
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.WriteVerboseDiagnostics(parseResult, "logs show", snapshot);

            string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
            if (!Directory.Exists(logDirectory))
            {
                return 0;
            }

            Queue<string> tailLines = new();
            foreach (string logPath in EnumerateLogFilesBestEffort(logDirectory))
            {
                AddLogFileTailLinesBestEffort(tailLines, logPath, tailCount);
            }

            foreach (string line in tailLines)
            {
                context.Output.WriteLine(line);
            }

            return 0;
        });
        command.Subcommands.Add(showCommand);

        Command clearCommand = new("clear", "Delete CLI log files.");
        clearCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.WriteVerboseDiagnostics(parseResult, "logs clear", snapshot);

            string logDirectory = LogPathResolver.ResolveLogDirectory(snapshot);
            if (!IsClearableLogDirectory(logDirectory))
            {
                return 0;
            }

            int clearedCount = 0;
            foreach (string logPath in EnumerateLogFilesBestEffort(logDirectory))
            {
                if (DeleteLogFileBestEffort(logPath))
                {
                    clearedCount++;
                }
            }

            context.Output.WriteLine($"Cleared {clearedCount} log file(s).");
            return 0;
        });
        command.Subcommands.Add(clearCommand);
        return command;
    }

    private static IReadOnlyList<string> EnumerateLogFilesBestEffort(string logDirectory)
    {
        List<string> logPaths = [];
        IEnumerator<string>? enumerator = null;
        try
        {
            enumerator = Directory.EnumerateFiles(logDirectory, "*.log").GetEnumerator();
            while (true)
            {
                try
                {
                    if (!enumerator.MoveNext())
                    {
                        break;
                    }
                }
                catch (Exception exception) when (IsBestEffortLogFileException(exception))
                {
                    break;
                }

                logPaths.Add(enumerator.Current);
            }
        }
        catch (Exception exception) when (IsBestEffortLogFileException(exception))
        {
        }
        finally
        {
            enumerator?.Dispose();
        }

        logPaths.Sort(StringComparer.Ordinal);
        return logPaths;
    }

    private static void AddLogFileTailLinesBestEffort(
        Queue<string> tailLines,
        string logPath,
        int tailCount)
    {
        try
        {
            foreach (string line in File.ReadLines(logPath))
            {
                tailLines.Enqueue(line);
                while (tailLines.Count > tailCount)
                {
                    tailLines.Dequeue();
                }
            }
        }
        catch (Exception exception) when (IsBestEffortLogFileException(exception))
        {
        }
    }

    private static bool DeleteLogFileBestEffort(string logPath)
    {
        try
        {
            if (!File.Exists(logPath))
            {
                return false;
            }

            File.Delete(logPath);
            return !File.Exists(logPath);
        }
        catch (Exception exception) when (IsBestEffortLogFileException(exception))
        {
            return false;
        }
    }

    private static bool IsClearableLogDirectory(string logDirectory)
    {
        try
        {
            string fullPath = Path.GetFullPath(logDirectory);
            if (!Directory.Exists(fullPath))
            {
                return false;
            }

            string? root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root) || IsSymlinkOrReparsePoint(root))
            {
                return false;
            }

            string relativePath = Path.GetRelativePath(root, fullPath);
            if (string.Equals(relativePath, ".", StringComparison.Ordinal))
            {
                return true;
            }

            string currentPath = root;
            foreach (string segment in relativePath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries))
            {
                currentPath = Path.Combine(currentPath, segment);
                if (IsSymlinkOrReparsePoint(currentPath))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception) when (IsBestEffortLogFileException(exception)
            || exception is ArgumentException
            || exception is NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsSymlinkOrReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (IsBestEffortLogFileException(exception))
        {
            return true;
        }
    }

    private static bool IsBestEffortLogFileException(Exception exception)
    {
        if (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }

        return exception is IOException or UnauthorizedAccessException;
    }
}
