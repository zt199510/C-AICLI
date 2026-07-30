using System.CommandLine;
using System.Globalization;
using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks;
using CSharpAiCli.ProjectPacks.Runtime;

namespace CSharpAiCli.Cli;

internal sealed class ArtifactsCommandModule : ICliCommandModule
{
    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("artifacts", "Inspect and safely manage project pack artifact lifecycle records.");
        command.Subcommands.Add(CreateListCommand(context));
        command.Subcommands.Add(CreateShowCommand(context));
        command.Subcommands.Add(CreateVerifyCommand(context));
        command.Subcommands.Add(CreateExportCommand(context));
        command.Subcommands.Add(CreatePruneCommand(context));
        return command;
    }

    private static Command CreateListCommand(CliCommandContext context)
    {
        Command command = new("list", "List declared artifacts without modifying them.");
        Option<string> runOption = new("--run") { Description = "Filter by one project pack run id." };
        Option<string> statusOption = new("--status") { Description = "Filter by one run state." };
        Option<bool> jsonOption = new("--json") { Description = "Write one stable JSON artifact list." };
        Option<string> outputOption = CreateOutputOption();
        command.Options.Add(runOption);
        command.Options.Add(statusOption);
        command.Options.Add(jsonOption);
        command.Options.Add(outputOption);
        command.SetAction(parseResult =>
        {
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(
                parseResult.GetValue(context.GlobalOptions.Workspace));
            bool jsonOutput = CliCommandContext.IsJsonOutputRequested(
                parseResult.GetValue(jsonOption),
                parseResult.GetValue(outputOption) ?? "text");
            context.WriteVerboseDiagnostics(parseResult, "artifacts list", snapshot, humanReadableOutput: !jsonOutput);
            ManagedArtifactListResult result = ManagedArtifactStore.Create(snapshot).List(
                parseResult.GetValue(runOption),
                parseResult.GetValue(statusOption));
            context.Output.WriteLine(jsonOutput
                ? ManagedArtifactRenderer.RenderListJson(result)
                : ManagedArtifactRenderer.RenderListText(result));
            return result.Diagnostics.Count == 0 ? 0 : 1;
        });
        return command;
    }

    private static Command CreateShowCommand(CliCommandContext context)
    {
        Command command = new("show", "Show one declared artifact without reading its content.");
        Argument<string> idArgument = new("artifact-id") { Description = "Managed artifact id." };
        Option<bool> jsonOption = new("--json") { Description = "Write one stable JSON artifact object." };
        Option<string> outputOption = CreateOutputOption();
        command.Arguments.Add(idArgument);
        command.Options.Add(jsonOption);
        command.Options.Add(outputOption);
        command.SetAction(parseResult =>
        {
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(
                parseResult.GetValue(context.GlobalOptions.Workspace));
            bool jsonOutput = CliCommandContext.IsJsonOutputRequested(
                parseResult.GetValue(jsonOption),
                parseResult.GetValue(outputOption) ?? "text");
            ManagedArtifactReadResult result = ManagedArtifactStore.Create(snapshot).Read(
                parseResult.GetValue(idArgument) ?? string.Empty);
            if (!result.Succeeded || result.Manifest is null || result.Artifact is null)
            {
                context.Output.WriteLine(ManagedArtifactRenderer.RenderFailure(
                    "artifacts.show",
                    result.Diagnostic ?? new ManagedArtifactDiagnostic(
                        ManagedArtifactErrorCode.NotFound,
                        "Managed artifact was not found."),
                    jsonOutput));
                return 1;
            }

            context.Output.WriteLine(jsonOutput
                ? ManagedArtifactRenderer.RenderShowJson(result.Manifest, result.Artifact)
                : ManagedArtifactRenderer.RenderShowText(result.Manifest, result.Artifact));
            return 0;
        });
        return command;
    }

    private static Command CreateVerifyCommand(CliCommandContext context)
    {
        Command command = new("verify", "Recompute one artifact size and SHA256 through its ownership boundary.");
        Argument<string> idArgument = new("artifact-id") { Description = "Managed artifact id." };
        Option<bool> jsonOption = new("--json") { Description = "Write one stable JSON verification object." };
        Option<string> outputOption = CreateOutputOption();
        command.Arguments.Add(idArgument);
        command.Options.Add(jsonOption);
        command.Options.Add(outputOption);
        command.SetAction(parseResult =>
        {
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(
                parseResult.GetValue(context.GlobalOptions.Workspace));
            bool jsonOutput = CliCommandContext.IsJsonOutputRequested(
                parseResult.GetValue(jsonOption),
                parseResult.GetValue(outputOption) ?? "text");
            ManagedArtifactVerificationResult result = ManagedArtifactStore.Create(snapshot).Verify(
                parseResult.GetValue(idArgument) ?? string.Empty,
                snapshot.Workspace);
            context.Output.WriteLine(jsonOutput
                ? ManagedArtifactRenderer.RenderVerifyJson(result)
                : ManagedArtifactRenderer.RenderVerifyText(result));
            return result.Succeeded ? 0 : 1;
        });
        return command;
    }

    private static Command CreateExportCommand(CliCommandContext context)
    {
        Command command = new("export", "Export one artifact lifecycle record to stdout.");
        Argument<string> idArgument = new("artifact-id") { Description = "Managed artifact id." };
        Option<string> formatOption = new("--format") { Description = "Select text, json, or markdown." };
        formatOption.DefaultValueFactory = _ => "text";
        formatOption.Validators.Add(result =>
        {
            string format = result.GetValueOrDefault<string>() ?? "text";
            if (format is not "text" and not "json" and not "markdown")
            {
                result.AddError("Invalid value for --format. Allowed values are text, json, and markdown.");
            }
        });
        command.Arguments.Add(idArgument);
        command.Options.Add(formatOption);
        command.SetAction(parseResult =>
        {
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(
                parseResult.GetValue(context.GlobalOptions.Workspace));
            string format = parseResult.GetValue(formatOption) ?? "text";
            ManagedArtifactReadResult result = ManagedArtifactStore.Create(snapshot).Read(
                parseResult.GetValue(idArgument) ?? string.Empty);
            if (!result.Succeeded || result.Manifest is null || result.Artifact is null)
            {
                context.Output.WriteLine(ManagedArtifactRenderer.RenderFailure(
                    "artifacts.export",
                    result.Diagnostic ?? new ManagedArtifactDiagnostic(
                        ManagedArtifactErrorCode.NotFound,
                        "Managed artifact was not found."),
                    format == "json"));
                return 1;
            }

            context.Output.WriteLine(ManagedArtifactRenderer.RenderExport(result.Manifest, result.Artifact, format));
            return 0;
        });
        return command;
    }

    private static Command CreatePruneCommand(CliCommandContext context)
    {
        Command command = new("prune", "Dry-run or explicitly apply bounded managed artifact pruning.");
        Option<string> olderOption = new("--older-than") { Description = "Minimum age such as 30d, 12h, or 90m." };
        olderOption.Validators.Add(result =>
        {
            if (!result.Implicit &&
                !ManagedArtifactRetentionParser.TryParseAge(result.GetValueOrDefault<string>(), out _))
            {
                result.AddError("Invalid --older-than value. Use a positive d, h, or m duration such as 30d.");
            }
        });
        Option<string[]> statusOption = new("--status")
        {
            Description = "Filter accepted, rejected, failed, or canceled terminal runs.",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true
        };
        statusOption.Validators.Add(result =>
        {
            string[] statuses = result.GetValueOrDefault<string[]>() ?? [];
            if (statuses.Any(status => status is not ProjectPackRunState.Accepted
                and not ProjectPackRunState.Rejected
                and not ProjectPackRunState.Failed
                and not ProjectPackRunState.Canceled))
            {
                result.AddError("--status accepts only accepted, rejected, failed, or canceled.");
            }
        });
        Option<long?> minimumSizeOption = new("--min-size") { Description = "Minimum declared artifact bytes." };
        Option<long?> maximumSizeOption = new("--max-size") { Description = "Maximum declared artifact bytes." };
        Option<bool> dryRunOption = new("--dry-run") { Description = "Render candidates without deletion; this is the default." };
        Option<bool> applyOption = new("--apply") { Description = "Explicitly apply deletion to eligible owned managed artifacts." };
        Option<bool> jsonOption = new("--json") { Description = "Write one stable JSON prune result." };
        Option<string> outputOption = CreateOutputOption();
        command.Options.Add(olderOption);
        command.Options.Add(statusOption);
        command.Options.Add(minimumSizeOption);
        command.Options.Add(maximumSizeOption);
        command.Options.Add(dryRunOption);
        command.Options.Add(applyOption);
        command.Options.Add(jsonOption);
        command.Options.Add(outputOption);
        command.SetAction(parseResult =>
        {
            bool apply = parseResult.GetValue(applyOption);
            bool dryRun = parseResult.GetValue(dryRunOption);
            bool jsonOutput = CliCommandContext.IsJsonOutputRequested(
                parseResult.GetValue(jsonOption),
                parseResult.GetValue(outputOption) ?? "text");
            if (apply && dryRun)
            {
                context.Output.WriteLine(ManagedArtifactRenderer.RenderFailure(
                    "artifacts.prune",
                    new ManagedArtifactDiagnostic(
                        ManagedArtifactErrorCode.PruneIneligible,
                        "--dry-run and --apply cannot be used together."),
                    jsonOutput));
                return 2;
            }

            long? minimumSize = parseResult.GetValue(minimumSizeOption);
            long? maximumSize = parseResult.GetValue(maximumSizeOption);
            if (minimumSize < 0 || maximumSize < 0 ||
                minimumSize is not null && maximumSize is not null && minimumSize > maximumSize)
            {
                context.Output.WriteLine(ManagedArtifactRenderer.RenderFailure(
                    "artifacts.prune",
                    new ManagedArtifactDiagnostic(
                        ManagedArtifactErrorCode.PruneIneligible,
                        "Prune size filters must be non-negative and min-size cannot exceed max-size."),
                    jsonOutput));
                return 2;
            }

            DateTimeOffset nowUtc = context.Dependencies.UtcNowProvider();
            string[] statuses = parseResult.GetValue(statusOption) ?? [];
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(
                parseResult.GetValue(context.GlobalOptions.Workspace));
            string configuredAge = parseResult.GetValue(olderOption) ??
                snapshot.Configuration.ArtifactRetention.DefaultMinimumAgeDays
                    .ToString(CultureInfo.InvariantCulture) + "d";
            ManagedArtifactRetentionParser.TryParseAge(configuredAge, out TimeSpan age);
            ManagedArtifactPruneResult result = ManagedArtifactStore.Create(snapshot).Prune(
                new ManagedArtifactPruneFilter(
                    nowUtc - age,
                    statuses.Length == 0 ? null : new HashSet<string>(statuses, StringComparer.Ordinal),
                    minimumSize,
                    maximumSize),
                apply,
                snapshot.Workspace,
                nowUtc);
            if (apply && result.DeletedCount > 0)
            {
                IReadOnlyList<ManagedArtifactDiagnostic> correlationDiagnostics =
                    UpdatePrunedProjectPackJobCorrelations(snapshot, result, nowUtc);
                if (correlationDiagnostics.Count > 0)
                {
                    result = result with
                    {
                        Diagnostics = result.Diagnostics.Concat(correlationDiagnostics).ToArray()
                    };
                }
            }

            context.Output.WriteLine(jsonOutput
                ? ManagedArtifactRenderer.RenderPruneJson(result)
                : ManagedArtifactRenderer.RenderPruneText(result));
            return result.Diagnostics.Any(diagnostic => diagnostic.ErrorCode is
                ManagedArtifactErrorCode.PruneFailed or
                ManagedArtifactErrorCode.PruneRace or
                ManagedArtifactErrorCode.ReparsePoint ||
                diagnostic.ErrorCode.StartsWith("artifact-job-correlation-", StringComparison.Ordinal)) ? 1 : 0;
        });
        return command;
    }

    private static Option<string> CreateOutputOption()
    {
        Option<string> option = new("--output") { Description = "Select text or json output." };
        option.DefaultValueFactory = _ => "text";
        CliCommandContext.AddTextJsonOutputValidator(option);
        return option;
    }

    private static IReadOnlyList<ManagedArtifactDiagnostic> UpdatePrunedProjectPackJobCorrelations(
        CliEnvironmentSnapshot snapshot,
        ManagedArtifactPruneResult result,
        DateTimeOffset nowUtc)
    {
        List<ManagedArtifactDiagnostic> diagnostics = [];
        ManagedArtifactStore artifactStore = ManagedArtifactStore.Create(snapshot);
        ManagedProjectPackRunStore runStore = ManagedProjectPackRunStore.Create(snapshot);
        JobRecordStore jobStore = JobRecordStore.Create(snapshot);
        foreach (IGrouping<string, ManagedArtifactPruneItem> runGroup in result.Items
            .Where(item => item.Deleted)
            .GroupBy(item => item.RunId, StringComparer.Ordinal))
        {
            ManagedArtifactReadResult manifestRead = artifactStore.ReadManifest(runGroup.Key);
            if (!manifestRead.Succeeded || manifestRead.Manifest?.Owner.JobId is null)
            {
                diagnostics.Add(new ManagedArtifactDiagnostic(
                    "artifact-job-correlation-missing",
                    "Pruned artifact tombstone was retained, but no correlated job could be updated.",
                    RunId: runGroup.Key));
                continue;
            }

            JobRecordReadResult jobRead = jobStore.Read(manifestRead.Manifest.Owner.JobId);
            if (!jobRead.Succeeded || jobRead.Record is null)
            {
                diagnostics.Add(new ManagedArtifactDiagnostic(
                    "artifact-job-correlation-missing",
                    "Pruned artifact tombstone was retained, but the correlated job was missing or corrupt.",
                    RunId: runGroup.Key));
                continue;
            }

            try
            {
                ManagedProjectPackRunLayout layout = runStore.GetLayout(runGroup.Key);
                Dictionary<string, ManagedArtifactPruneItem> prunedPaths = runGroup.ToDictionary(
                    item => Path.GetFullPath(Path.Combine(
                        layout.RunRoot,
                        item.Path.Replace('/', Path.DirectorySeparatorChar))),
                    PathComparer);
                List<JobArtifact> artifacts = jobRead.Record.Artifacts.Select(artifact =>
                {
                    string fullPath;
                    try
                    {
                        fullPath = Path.GetFullPath(artifact.Path);
                    }
                    catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
                    {
                        return artifact;
                    }

                    return prunedPaths.TryGetValue(fullPath, out ManagedArtifactPruneItem? pruned)
                        ? new JobArtifact(
                            artifact.Kind,
                            artifact.Path,
                            Exists: false,
                            Summary: $"Pruned managed artifact; tombstone={ManagedArtifactId.Create(runGroup.Key, manifestRead.Manifest.Artifacts.Single(entry => entry.Path == pruned.Path).PointerId)}",
                            artifact.Sha256,
                            artifact.CreatedAtUtc)
                        : artifact;
                }).ToList();
                foreach (ManagedArtifactPruneItem item in runGroup)
                {
                    ManagedArtifactEntry tombstone =
                        manifestRead.Manifest.Artifacts.Single(entry => entry.ArtifactId == item.ArtifactId);
                    artifacts.Add(new JobArtifact(
                        JobArtifactKind.ProjectPackArtifactTombstone,
                        $"inline:artifact-tombstone/{item.ArtifactId}",
                        Exists: true,
                        Summary: $"runId={item.RunId}; path={tombstone.Path}; removedAtUtc={tombstone.Tombstone?.RemovedAtUtc:O}; reason={tombstone.Tombstone?.Reason}",
                        tombstone.Sha256,
                        tombstone.Tombstone?.RemovedAtUtc));
                }

                JobRecord updated = jobRead.Record.WithStatus(
                    jobRead.Record.Status,
                    nowUtc,
                    jobRead.Record.ExitCode,
                    jobRead.Record.StopReason,
                    jobRead.Record.ErrorCode,
                    jobRead.Record.Summary,
                    taskReport: null,
                    artifacts: artifacts
                        .GroupBy(artifact => artifact.Kind + "\n" + artifact.Path, StringComparer.Ordinal)
                        .Select(group => group.Last())
                        .ToArray(),
                    warnings: jobRead.Record.Warnings.Concat(
                    [
                        $"Managed artifact prune retained {runGroup.Count()} tombstone(s); source, workspace output, run, and job metadata were preserved."
                    ]).Distinct(StringComparer.Ordinal).ToArray());
                jobStore.Update(updated);
            }
            catch (Exception exception) when (IsStoreException(exception) ||
                exception is ProjectPackContractException)
            {
                diagnostics.Add(new ManagedArtifactDiagnostic(
                    "artifact-job-correlation-failed",
                    "Pruned artifact tombstone was retained, but the correlated job could not be updated safely.",
                    RunId: runGroup.Key));
            }
        }

        return diagnostics;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static bool IsStoreException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or InvalidOperationException;
    }
}
