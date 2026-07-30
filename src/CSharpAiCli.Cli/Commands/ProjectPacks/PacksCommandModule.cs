using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using CSharpAiCli.Application;
using CSharpAiCli.Core;
using CSharpAiCli.ProjectPacks;
using CSharpAiCli.ProjectPacks.GerberTiff;
using CSharpAiCli.ProjectPacks.Runtime;

namespace CSharpAiCli.Cli;

public static partial class CliCommandFactory
{
    private sealed class PacksCommandModule : ICliCommandModule
    {
        public Command Create(CliCommandContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            TextWriter output = context.Output;
            Func<string?, CliEnvironmentSnapshot> workspaceSnapshotProvider = context.WorkspaceSnapshotProvider;
            Action<string, CliEnvironmentSnapshot> commandLogger = context.Dependencies.CommandLogger;
            Func<DateTimeOffset> utcNowProvider = context.Dependencies.UtcNowProvider;
            Option<string> workspaceOption = context.GlobalOptions.Workspace;
            ProjectPackRegistry projectPackRegistry = new([new GerberTiffWorkflowPack()]);

            void WriteVerboseDiagnostics(ParseResult parseResult, string commandName, CliEnvironmentSnapshot snapshot, bool humanReadableOutput = true) =>
                context.WriteVerboseDiagnostics(parseResult, commandName, snapshot, humanReadableOutput);

            DiagnosticContext? CreateTraceContext(ParseResult parseResult, CliEnvironmentSnapshot snapshot) =>
                context.CreateTraceContext(parseResult, snapshot);

            void TryWriteTraceCommandEvent(
                string commandName,
                CliEnvironmentSnapshot snapshot,
                DiagnosticContext? diagnosticContext,
                string type,
                long sequence,
                string status,
                string? summary = null,
                string? errorCode = null,
                DateTimeOffset? timestampUtc = null) =>
                context.TryWriteTraceCommandEvent(commandName, snapshot, diagnosticContext, type, sequence, status, summary, errorCode, timestampUtc);
            Command packsCommand = new("packs", "Inspect deterministic project pack contracts and tool dependencies.");
            Command packsListCommand = new("list", "List built-in project pack contracts without running tools.");
            Option<bool> packsListJsonOption = new("--json")
            {
                Description = "Write a single JSON project pack list object.",
            };
            Option<string> packsListOutputOption = new("--output")
            {
                Description = "Select text or json output.",
            };
            packsListOutputOption.DefaultValueFactory = _ => "text";
            AddTextJsonOutputValidator(packsListOutputOption);
            packsListCommand.Options.Add(packsListJsonOption);
            packsListCommand.Options.Add(packsListOutputOption);
            packsListCommand.SetAction(parseResult =>
            {
                string? workspacePath = parseResult.GetValue(workspaceOption);
                bool jsonRequested = parseResult.GetValue(packsListJsonOption);
                string outputMode = parseResult.GetValue(packsListOutputOption) ?? "text";
                bool jsonOutput = IsJsonOutputRequested(jsonRequested, outputMode);
                CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
                WriteVerboseDiagnostics(parseResult, "packs list", snapshot, humanReadableOutput: !jsonOutput);
                output.WriteLine(jsonOutput
                    ? ProjectPackReportRenderer.RenderListJson(projectPackRegistry)
                    : ProjectPackReportRenderer.RenderListText(projectPackRegistry));
                return 0;
            });

            Command packsDoctorCommand = new("doctor", "Statically inspect project pack tools; --probe requires approval before execution.");
            Argument<string> packsDoctorPackArgument = new("pack")
            {
                Description = "Registered project pack id.",
            };
            packsDoctorPackArgument.Validators.Add(result =>
            {
                string packId = result.GetValueOrDefault<string>() ?? string.Empty;
                if (!projectPackRegistry.TryGet(packId, out _))
                {
                    result.AddError($"Unknown project pack '{packId}'.");
                }
            });
            Option<string[]> packsDoctorToolPathOption = new("--tool-path")
            {
                Description = "Configure [dependency=]absolute-path. A single bare path selects the primary dependency.",
                Arity = ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = true,
            };
            Option<bool> packsDoctorProbeOption = new("--probe")
            {
                Description = "Run fixed version probes after static identity checks and explicit approval.",
            };
            Option<bool> packsDoctorApproveOption = new("--approve")
            {
                Description = "Approve this invocation's fixed external tool probes.",
            };
            Option<string> packsDoctorApprovalOption = new("--approval")
            {
                Description = "Override approval mode for this invocation only.",
            };
            Option<bool> packsDoctorJsonOption = new("--json")
            {
                Description = "Write a single JSON project pack doctor object.",
            };
            Option<string> packsDoctorOutputOption = new("--output")
            {
                Description = "Select text or json output.",
            };
            packsDoctorOutputOption.DefaultValueFactory = _ => "text";
            AddApprovalModeValidator(packsDoctorApprovalOption);
            AddTextJsonOutputValidator(packsDoctorOutputOption);
            packsDoctorCommand.Arguments.Add(packsDoctorPackArgument);
            packsDoctorCommand.Options.Add(packsDoctorToolPathOption);
            packsDoctorCommand.Options.Add(packsDoctorProbeOption);
            packsDoctorCommand.Options.Add(packsDoctorApproveOption);
            packsDoctorCommand.Options.Add(packsDoctorApprovalOption);
            packsDoctorCommand.Options.Add(packsDoctorJsonOption);
            packsDoctorCommand.Options.Add(packsDoctorOutputOption);
            packsDoctorCommand.SetAction(parseResult =>
            {
                string? workspacePath = parseResult.GetValue(workspaceOption);
                string packId = parseResult.GetValue(packsDoctorPackArgument) ?? string.Empty;
                string[] toolPathValues = parseResult.GetValue(packsDoctorToolPathOption) ?? [];
                bool probe = parseResult.GetValue(packsDoctorProbeOption);
                bool approve = parseResult.GetValue(packsDoctorApproveOption);
                string? approvalModeValue = parseResult.GetValue(packsDoctorApprovalOption);
                bool jsonRequested = parseResult.GetValue(packsDoctorJsonOption);
                string outputMode = parseResult.GetValue(packsDoctorOutputOption) ?? "text";
                bool jsonOutput = IsJsonOutputRequested(jsonRequested, outputMode);
                CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
                WriteVerboseDiagnostics(parseResult, "packs doctor", snapshot, humanReadableOutput: !jsonOutput);

                if (!projectPackRegistry.TryGet(packId, out IProjectPack? pack) || pack is null)
                {
                    output.WriteLine(jsonOutput
                        ? JsonSerializer.Serialize(new
                        {
                            type = "packs.doctor",
                            schemaVersion = ProjectPackSchema.CurrentVersion,
                            pack = packId,
                            status = "failed",
                            errorCode = "pack-not-found",
                            summary = "Project pack is not registered."
                        }, JsonOptions)
                        : "Project pack is not registered.");
                    return 1;
                }

                if (!TryParseProjectPackToolPaths(pack.Manifest, toolPathValues, out Dictionary<string, string> toolPaths, out string? bindingError))
                {
                    output.WriteLine(jsonOutput
                        ? JsonSerializer.Serialize(new
                        {
                            type = "packs.doctor",
                            schemaVersion = ProjectPackSchema.CurrentVersion,
                            pack = packId,
                            status = "failed",
                            errorCode = "pack-tool-binding-invalid",
                            summary = bindingError
                        }, JsonOptions)
                        : bindingError);
                    return 2;
                }

                ApprovalMode? cliApprovalMode = GetApprovalOverride(
                    approvalModeValue,
                    parseResult.GetResult(packsDoctorApprovalOption),
                    approve);
                IApprovalPolicy approvalPolicy = ApprovalPolicyResolver.Resolve(
                    snapshot.Configuration.ApprovalMode,
                    cliApprovalMode);
                DiagnosticContext? traceContext = CreateTraceContext(parseResult, snapshot);
                TryWriteTraceCommandEvent(
                    "packs doctor",
                    snapshot,
                    traceContext,
                    "command.start",
                    sequence: 1,
                    "started",
                    summary: probe ? "Project pack doctor probe requested." : "Project pack static doctor requested.");

                ProjectPackDoctorReport report = new ProjectPackDoctorService().Diagnose(
                    pack,
                    toolPaths,
                    trustedHashes: null,
                    probe,
                    approvalPolicy,
                    CancellationToken.None);
                output.WriteLine(jsonOutput
                    ? ProjectPackReportRenderer.RenderDoctorJson(report)
                    : ProjectPackReportRenderer.RenderDoctorText(report));
                TryWriteTraceCommandEvent(
                    "packs doctor",
                    snapshot,
                    traceContext,
                    "command.complete",
                    sequence: 2,
                    report.Succeeded ? "success" : "failure",
                    summary: $"Project pack doctor completed with status {report.Status}.",
                    errorCode: report.Succeeded ? null : "pack-doctor-failed");
                return report.Succeeded ? 0 : 1;
            });
            packsCommand.Subcommands.Add(packsListCommand);
            packsCommand.Subcommands.Add(packsDoctorCommand);

            Command packsPlanCommand = new("plan", "Build a bounded deterministic project pack plan without running conversion.");
            Argument<string> packsPlanPackArgument = new("pack")
            {
                Description = "Registered project pack id.",
            };
            packsPlanPackArgument.Validators.Add(result =>
            {
                string packId = result.GetValueOrDefault<string>() ?? string.Empty;
                if (!projectPackRegistry.TryGet(packId, out _))
                {
                    result.AddError($"Unknown project pack '{packId}'.");
                }
            });
            Option<string> packsPlanInputOption = new("--input")
            {
                Description = "Use one explicit input directory inside the workspace.",
            };
            packsPlanInputOption.Validators.Add(result =>
            {
                if (result.Implicit || string.IsNullOrWhiteSpace(result.GetValueOrDefault<string>()))
                {
                    result.AddError("--input is required.");
                }
            });
            Option<string> packsPlanOutputDirectoryOption = new("--output-dir")
            {
                Description = "Validate one new output directory inside the workspace without creating it.",
            };
            packsPlanOutputDirectoryOption.Validators.Add(result =>
            {
                if (result.Implicit || string.IsNullOrWhiteSpace(result.GetValueOrDefault<string>()))
                {
                    result.AddError("--output-dir is required.");
                }
            });
            Option<string[]> packsPlanToolPathOption = new("--tool-path")
            {
                Description = "Statically inspect [dependency=]absolute-path without starting the tool.",
                Arity = ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = true,
            };
            Option<bool> packsPlanJsonOption = new("--json")
            {
                Description = "Write a single JSON project pack plan object.",
            };
            Option<string> packsPlanOutputOption = new("--output")
            {
                Description = "Select text or json output.",
            };
            packsPlanOutputOption.DefaultValueFactory = _ => "text";
            AddTextJsonOutputValidator(packsPlanOutputOption);
            packsPlanCommand.Arguments.Add(packsPlanPackArgument);
            packsPlanCommand.Options.Add(packsPlanInputOption);
            packsPlanCommand.Options.Add(packsPlanOutputDirectoryOption);
            packsPlanCommand.Options.Add(packsPlanToolPathOption);
            packsPlanCommand.Options.Add(packsPlanJsonOption);
            packsPlanCommand.Options.Add(packsPlanOutputOption);
            packsPlanCommand.SetAction(parseResult =>
            {
                string? workspacePath = parseResult.GetValue(workspaceOption);
                string packId = parseResult.GetValue(packsPlanPackArgument) ?? string.Empty;
                string? inputDirectory = parseResult.GetValue(packsPlanInputOption);
                string? outputDirectory = parseResult.GetValue(packsPlanOutputDirectoryOption);
                string[] toolPathValues = parseResult.GetValue(packsPlanToolPathOption) ?? [];
                bool jsonRequested = parseResult.GetValue(packsPlanJsonOption);
                string outputMode = parseResult.GetValue(packsPlanOutputOption) ?? "text";
                bool jsonOutput = IsJsonOutputRequested(jsonRequested, outputMode);
                CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
                WriteVerboseDiagnostics(parseResult, "packs plan", snapshot, humanReadableOutput: !jsonOutput);

                if (!projectPackRegistry.TryGet(packId, out IProjectPack? registeredPack) ||
                    registeredPack is not GerberTiffWorkflowPack gerberTiffPack)
                {
                    output.WriteLine(jsonOutput
                        ? JsonSerializer.Serialize(new
                        {
                            type = "packs.plan",
                            schemaVersion = ProjectPackSchema.CurrentVersion,
                            pack = packId,
                            status = "blocked",
                            errorCode = "pack-plan-not-supported",
                            summary = "Project pack does not provide a v1 deterministic plan builder."
                        }, JsonOptions)
                        : "Project pack does not provide a v1 deterministic plan builder.");
                    return 1;
                }

                if (!TryParseProjectPackToolPaths(
                    gerberTiffPack.Manifest,
                    toolPathValues,
                    out Dictionary<string, string> toolPaths,
                    out string? bindingError))
                {
                    output.WriteLine(jsonOutput
                        ? JsonSerializer.Serialize(new
                        {
                            type = "packs.plan",
                            schemaVersion = ProjectPackSchema.CurrentVersion,
                            pack = packId,
                            status = "blocked",
                            errorCode = "pack-tool-binding-invalid",
                            summary = bindingError
                        }, JsonOptions)
                        : bindingError);
                    return 2;
                }

                DiagnosticContext? traceContext = CreateTraceContext(parseResult, snapshot);
                TryWriteTraceCommandEvent(
                    "packs plan",
                    snapshot,
                    traceContext,
                    "command.start",
                    sequence: 1,
                    "started",
                    summary: "Project pack static plan requested.");
                GerberTiffConversionPlan plan = new GerberTiffConversionPlanBuilder(gerberTiffPack).Build(
                    snapshot.Workspace,
                    inputDirectory,
                    outputDirectory,
                    toolPaths,
                    trustedHashes: null,
                    CancellationToken.None);
                string workspaceSource = string.IsNullOrWhiteSpace(workspacePath) ? "current-directory" : "--workspace";
                output.WriteLine(jsonOutput
                    ? GerberTiffPlanRenderer.RenderJson(plan, workspaceSource)
                    : GerberTiffPlanRenderer.RenderText(plan, workspaceSource));
                TryWriteTraceCommandEvent(
                    "packs plan",
                    snapshot,
                    traceContext,
                    "command.complete",
                    sequence: 2,
                    plan.Runnable ? "success" : "failure",
                    summary: plan.Runnable
                        ? "Project pack runnable plan generated."
                        : "Project pack plan completed without runnable conversion authorization.",
                    errorCode: plan.Runnable ? null : "pack-plan-blocked");
                return plan.Runnable ? 0 : 1;
            });
            packsCommand.Subcommands.Add(packsPlanCommand);

            Command packsRunCommand = new("run", "Create an isolated run and optionally execute the controlled Gerber/TIFF conversion.");
            Argument<string> packsRunPackArgument = new("pack")
            {
                Description = "Registered project pack id."
            };
            packsRunPackArgument.Validators.Add(result =>
            {
                string packId = result.GetValueOrDefault<string>() ?? string.Empty;
                if (!projectPackRegistry.TryGet(packId, out _))
                {
                    result.AddError($"Unknown project pack '{packId}'.");
                }
            });
            Option<string> packsRunPlanOption = new("--plan")
            {
                Description = "Read one packs.plan JSON file from inside the workspace."
            };
            packsRunPlanOption.Validators.Add(result =>
            {
                if (result.Implicit || string.IsNullOrWhiteSpace(result.GetValueOrDefault<string>()))
                {
                    result.AddError("--plan is required.");
                }
            });
            Option<bool> packsRunDryRunOption = new("--dry-run")
            {
                Description = "Persist isolated staging evidence without executing fake or real conversion tools."
            };
            Option<bool> packsRunApproveOption = new("--approve")
            {
                Description = "Approve this invocation's fixed tool probes and conversion stages."
            };
            Option<string> packsRunApprovalOption = new("--approval")
            {
                Description = "Override approval mode for this invocation only."
            };
            Option<string[]> packsRunToolPathOption = new("--tool-path")
            {
                Description = "Revalidate [dependency=]absolute-path without starting the tool.",
                Arity = ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = true
            };
            Option<bool> packsRunJsonOption = new("--json")
            {
                Description = "Write a single JSON project pack run object."
            };
            Option<string> packsRunOutputOption = new("--output")
            {
                Description = "Select text or json output."
            };
            packsRunOutputOption.DefaultValueFactory = _ => "text";
            AddApprovalModeValidator(packsRunApprovalOption);
            AddTextJsonOutputValidator(packsRunOutputOption);
            packsRunCommand.Arguments.Add(packsRunPackArgument);
            packsRunCommand.Options.Add(packsRunPlanOption);
            packsRunCommand.Options.Add(packsRunDryRunOption);
            packsRunCommand.Options.Add(packsRunApproveOption);
            packsRunCommand.Options.Add(packsRunApprovalOption);
            packsRunCommand.Options.Add(packsRunToolPathOption);
            packsRunCommand.Options.Add(packsRunJsonOption);
            packsRunCommand.Options.Add(packsRunOutputOption);
            packsRunCommand.SetAction(parseResult =>
            {
                string? workspacePath = parseResult.GetValue(workspaceOption);
                string packId = parseResult.GetValue(packsRunPackArgument) ?? string.Empty;
                string? planPath = parseResult.GetValue(packsRunPlanOption);
                bool dryRun = parseResult.GetValue(packsRunDryRunOption);
                bool approve = parseResult.GetValue(packsRunApproveOption);
                string? approvalModeValue = parseResult.GetValue(packsRunApprovalOption);
                string[] toolPathValues = parseResult.GetValue(packsRunToolPathOption) ?? [];
                bool jsonOutput = IsJsonOutputRequested(
                    parseResult.GetValue(packsRunJsonOption),
                    parseResult.GetValue(packsRunOutputOption) ?? "text");
                CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
                WriteVerboseDiagnostics(parseResult, "packs run", snapshot, humanReadableOutput: !jsonOutput);

                if (!projectPackRegistry.TryGet(packId, out IProjectPack? registeredPack) ||
                    registeredPack is not GerberTiffWorkflowPack gerberTiffPack)
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.run", "pack-run-not-supported", "Project pack does not support controlled v1 conversion.", jsonOutput));
                    return 1;
                }

                if (!TryParseProjectPackToolPaths(
                    gerberTiffPack.Manifest,
                    toolPathValues,
                    out Dictionary<string, string> toolPaths,
                    out string? bindingError))
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.run", "pack-tool-binding-invalid", bindingError ?? "Project pack tool binding is invalid.", jsonOutput));
                    return 2;
                }

                if (!TryReadProjectPackPlan(snapshot.Workspace, planPath, out string? planJson, out string? planErrorCode, out string? planError))
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.run", planErrorCode ?? ProjectPackRunErrorCode.PlanInvalid,
                        planError ?? "Project pack plan could not be read.", jsonOutput));
                    return 1;
                }

                GerberTiffRunPlanSnapshot plan;
                try
                {
                    plan = GerberTiffRunPlanLoader.Load(planJson!);
                }
                catch (ProjectPackContractException exception)
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.run", exception.ErrorCode, exception.Message, jsonOutput));
                    return 1;
                }

                if (!string.Equals(plan.PackId, packId, StringComparison.Ordinal))
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.run", ProjectPackRunErrorCode.PlanInvalid,
                        "Project pack plan does not match the selected pack.", jsonOutput));
                    return 1;
                }

                ApprovalMode? cliApprovalMode = GetApprovalOverride(
                    approvalModeValue,
                    parseResult.GetResult(packsRunApprovalOption),
                    approve);
                IApprovalPolicy approvalPolicy = ApprovalPolicyResolver.Resolve(
                    snapshot.Configuration.ApprovalMode,
                    cliApprovalMode);
                IReadOnlyDictionary<string, ExternalToolIdentity> probedIdentities =
                    new Dictionary<string, ExternalToolIdentity>(StringComparer.Ordinal);
                if (!dryRun)
                {
                    ProjectPackDoctorReport probeReport = new ProjectPackDoctorService().Diagnose(
                        gerberTiffPack,
                        toolPaths,
                        trustedHashes: null,
                        probe: true,
                        approvalPolicy,
                        CancellationToken.None);
                    if (!probeReport.Succeeded || probeReport.Tools.Any(tool => tool.Required && tool.Identity?.Version is null))
                    {
                        string errorCode = MapProjectPackExecutionProbeError(probeReport);
                        string probeCodes = string.Join(", ", probeReport.Diagnostics
                            .Where(diagnostic => diagnostic.Severity == ProjectPackDiagnosticSeverity.Error)
                            .Select(diagnostic => diagnostic.Code)
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(code => code, StringComparer.Ordinal));
                        output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                            "packs.run",
                            errorCode,
                            "Controlled conversion requires current approved probes for every mandatory external tool." +
                                (probeCodes.Length == 0 ? string.Empty : $" Probe diagnostics: {probeCodes}."),
                            jsonOutput));
                        return 1;
                    }

                    probedIdentities = probeReport.Tools
                        .Where(tool => tool.Identity is not null)
                        .ToDictionary(tool => tool.DependencyId, tool => tool.Identity!, StringComparer.Ordinal);
                }

                DateTimeOffset nowUtc = utcNowProvider();
                string runId = ProjectPackRunId.Create(nowUtc);
                string jobId = JobIdGenerator.Create(nowUtc);
                JobRecordStore jobStore = JobRecordStore.Create(snapshot);
                JobRecord job = JobRecord.CreateRunning(
                    jobId,
                    nowUtc,
                    new JobCommandSummary(
                        "packs run",
                        Task: plan.PlanId,
                        WorkspaceRoot: snapshot.Workspace.RootPath,
                        OutputMode: jsonOutput ? "json" : "text",
                        DryRun: dryRun),
                    runId);
                try
                {
                    jobStore.Create(job);
                }
                catch (Exception exception) when (IsJobStoreException(exception))
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.run", "job-record-write-failed", "Project pack run job record could not be created.", jsonOutput));
                    return 1;
                }

                DiagnosticContext? traceContext = CreateTraceContext(parseResult, snapshot);
                TryWriteTraceCommandEvent(
                    "packs run",
                    snapshot,
                    traceContext,
                    "command.start",
                    sequence: 1,
                    "started",
                    summary: dryRun
                        ? "Project pack isolated dry-run staging requested."
                        : "Project pack controlled real conversion requested after current tool probes.",
                    timestampUtc: nowUtc);
                ManagedProjectPackRunStore runStore = ManagedProjectPackRunStore.Create(snapshot);
                ProjectPackRunService runService = new(runStore);
                ProjectPackRunMutationResult result = runService.CreateAndStage(
                    plan,
                    snapshot.Workspace,
                    toolPaths,
                    GetProjectPackPolicyFingerprint(snapshot, packId),
                    nowUtc,
                    new ProjectPackRunCorrelation(jobId: jobId),
                    runId,
                    CancellationToken.None);
                if (!dryRun && result.Record?.State == ProjectPackRunState.Ready)
                {
                    GerberTiffControlledConversionDriver driver = new(
                        plan,
                        snapshot.Workspace,
                        toolPaths,
                        probedIdentities,
                        approvalPolicy);
                    result = runService.ExecuteControlled(
                        runId,
                        plan,
                        snapshot.Workspace,
                        toolPaths,
                        GetProjectPackPolicyFingerprint(snapshot, packId),
                        driver,
                        utcNowProvider(),
                        CancellationToken.None);
                }

                List<JobArtifact> artifacts = [];
                ManagedProjectPackRunLayout layout = runStore.GetLayout(runId);
                if (File.Exists(layout.RunRecordPath))
                {
                    artifacts.Add(JobArtifact.FromPath(
                        JobArtifactKind.ProjectPackRun,
                        layout.RunRecordPath,
                        $"packRunId={runId}; operational checkpoint pointer only"));
                }

                if (File.Exists(layout.InputManifestPath))
                {
                    artifacts.Add(JobArtifact.FromPath(
                        JobArtifactKind.ProjectPackInputManifest,
                        layout.InputManifestPath,
                        "Immutable staged-input identity manifest."));
                }

                if (result.Record is not null)
                {
                    foreach (ProjectPackRunArtifactPointer pointer in result.Record.Artifacts)
                    {
                        if (pointer.Kind is not "conversion-execution-log" and
                            not "tiff-output" and
                            not "render-intermediate")
                        {
                            continue;
                        }

                        string? artifactPath = ResolveProjectPackArtifactPath(
                            pointer,
                            layout,
                            snapshot.Workspace);
                        if (artifactPath is null || !File.Exists(artifactPath))
                        {
                            continue;
                        }

                        string kind = pointer.Kind == "conversion-execution-log"
                            ? JobArtifactKind.ProjectPackExecutionLog
                            : pointer.Kind == "tiff-output"
                                ? JobArtifactKind.ProjectPackConversionOutput
                                : pointer.Kind;
                        artifacts.Add(JobArtifact.FromPath(
                            kind,
                            artifactPath,
                            pointer.Kind == "tiff-output"
                                ? "Controlled conversion output; TIFF engineering verification is pending."
                                : "Project pack managed artifact pointer."));
                    }
                }

                bool commandSucceeded = result.Record?.State == (dryRun
                    ? ProjectPackRunState.Ready
                    : ProjectPackRunState.Verifying);
                string jobStatus = commandSucceeded
                    ? dryRun ? JobStatus.DryRun : JobStatus.Succeeded
                    : result.Record?.State == ProjectPackRunState.Canceled
                        ? JobStatus.Canceled
                        : result.Record?.ErrorCode == ProjectPackRunErrorCode.ApprovalRequired
                            ? JobStatus.ApprovalRequired
                            : JobStatus.Failed;

                try
                {
                    JobRecord completedJob = job.WithStatus(
                        jobStatus,
                        utcNowProvider(),
                        exitCode: commandSucceeded ? 0 : 1,
                        stopReason: commandSucceeded
                            ? dryRun ? "dry-run-staged" : "conversion-executed-verification-pending"
                            : result.Record?.State ?? "project-pack-run-failed",
                        errorCode: commandSucceeded ? null : result.Record?.ErrorCode ?? result.Diagnostic?.ErrorCode,
                        summary: commandSucceeded
                            ? dryRun
                                ? "Project pack inputs were staged; no conversion or business verification was executed."
                                : "Gerber to TIFF conversion executed; declared hashes were recorded and TIFF engineering verification remains pending."
                            : result.Record?.Summary ?? result.Diagnostic?.Summary,
                        taskReport: null,
                        artifacts: artifacts,
                        warnings:
                        [
                            $"packRunId={runId}",
                            "Project pack run state remains the operational checkpoint; taskReport was not duplicated.",
                            dryRun
                                ? "No real conversion was executed."
                                : "Conversion execution does not establish TIFF engineering verification or business correctness."
                        ]);
                    jobStore.Update(completedJob);
                }
                catch (Exception exception) when (IsJobStoreException(exception))
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.run", "job-record-write-failed", "Project pack run job record could not be completed.", jsonOutput, runId));
                    return 1;
                }

                if (!commandSucceeded || result.Record is null || result.Checkpoint is null)
                {
                    TryWriteTraceCommandEvent(
                        "packs run",
                        snapshot,
                        traceContext,
                        "command.complete",
                        sequence: 2,
                        "failure",
                        summary: result.Record?.Summary ?? result.Diagnostic?.Summary ?? "Project pack run failed.",
                        errorCode: result.Record?.ErrorCode ?? result.Diagnostic?.ErrorCode,
                        timestampUtc: utcNowProvider());
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.run",
                        result.Record?.ErrorCode ?? result.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.ExecutionFailed,
                        result.Record?.Summary ?? result.Diagnostic?.Summary ?? "Project pack run did not reach its declared checkpoint.",
                        jsonOutput,
                        runId));
                    return 1;
                }

                output.WriteLine(jsonOutput
                    ? ProjectPackRunRenderer.RenderJson(result.Record, result.Checkpoint)
                    : ProjectPackRunRenderer.RenderText(result.Record, result.Checkpoint));
                TryWriteTraceCommandEvent(
                    "packs run",
                    snapshot,
                    traceContext,
                    "command.complete",
                    sequence: 2,
                    "success",
                    summary: dryRun
                        ? "Project pack isolated dry-run staging completed without tool execution."
                        : "Gerber to TIFF conversion executed; TIFF engineering verification remains pending.",
                    timestampUtc: utcNowProvider());
                return 0;
            });
            packsCommand.Subcommands.Add(packsRunCommand);

            Command packsRunsCommand = new("runs", "Inspect managed project pack run checkpoints.");
            Command packsRunsShowCommand = new("show", "Show one managed project pack run without executing it.");
            Argument<string> packsRunsShowIdArgument = new("run-id") { Description = "Project pack run id." };
            Option<bool> packsRunsShowJsonOption = new("--json") { Description = "Write a single JSON project pack run object." };
            Option<string> packsRunsShowOutputOption = new("--output") { Description = "Select text or json output." };
            packsRunsShowOutputOption.DefaultValueFactory = _ => "text";
            AddTextJsonOutputValidator(packsRunsShowOutputOption);
            packsRunsShowCommand.Arguments.Add(packsRunsShowIdArgument);
            packsRunsShowCommand.Options.Add(packsRunsShowJsonOption);
            packsRunsShowCommand.Options.Add(packsRunsShowOutputOption);
            packsRunsShowCommand.SetAction(parseResult =>
            {
                string? workspacePath = parseResult.GetValue(workspaceOption);
                string runId = parseResult.GetValue(packsRunsShowIdArgument) ?? string.Empty;
                bool jsonOutput = IsJsonOutputRequested(
                    parseResult.GetValue(packsRunsShowJsonOption),
                    parseResult.GetValue(packsRunsShowOutputOption) ?? "text");
                CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
                ProjectPackRunReadResult read = ManagedProjectPackRunStore.Create(snapshot).Read(runId);
                if (!read.Succeeded || read.Record is null || read.Checkpoint is null)
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.run", read.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.NotFound,
                        read.Diagnostic?.Summary ?? "Project pack run was not found.", jsonOutput, runId));
                    return 1;
                }

                output.WriteLine(jsonOutput
                    ? ProjectPackRunRenderer.RenderJson(read.Record, read.Checkpoint)
                    : ProjectPackRunRenderer.RenderText(read.Record, read.Checkpoint));
                return 0;
            });
            packsRunsCommand.Subcommands.Add(packsRunsShowCommand);
            packsCommand.Subcommands.Add(packsRunsCommand);

            Command packsCancelCommand = new("cancel", "Cancel one non-terminal managed project pack run.");
            Argument<string> packsCancelIdArgument = new("run-id") { Description = "Project pack run id." };
            Option<bool> packsCancelJsonOption = new("--json") { Description = "Write a single JSON project pack run object." };
            Option<string> packsCancelOutputOption = new("--output") { Description = "Select text or json output." };
            packsCancelOutputOption.DefaultValueFactory = _ => "text";
            AddTextJsonOutputValidator(packsCancelOutputOption);
            packsCancelCommand.Arguments.Add(packsCancelIdArgument);
            packsCancelCommand.Options.Add(packsCancelJsonOption);
            packsCancelCommand.Options.Add(packsCancelOutputOption);
            packsCancelCommand.SetAction(parseResult =>
            {
                string? workspacePath = parseResult.GetValue(workspaceOption);
                string runId = parseResult.GetValue(packsCancelIdArgument) ?? string.Empty;
                bool jsonOutput = IsJsonOutputRequested(
                    parseResult.GetValue(packsCancelJsonOption),
                    parseResult.GetValue(packsCancelOutputOption) ?? "text");
                CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
                ProjectPackRunMutationResult result = new ProjectPackRunService(ManagedProjectPackRunStore.Create(snapshot))
                    .Cancel(runId, utcNowProvider());
                if (!result.Succeeded || result.Record is null || result.Checkpoint is null)
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.cancel", result.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.TransitionInvalid,
                        result.Diagnostic?.Summary ?? "Project pack run could not be canceled.", jsonOutput, runId));
                    return 1;
                }

                output.WriteLine(jsonOutput
                    ? ProjectPackRunRenderer.RenderJson(result.Record, result.Checkpoint)
                    : ProjectPackRunRenderer.RenderText(result.Record, result.Checkpoint));
                return 0;
            });
            packsCommand.Subcommands.Add(packsCancelCommand);

            Command packsRecoverCommand = new("recover", "Explicitly mark a stale running local run as interrupted without replaying it.");
            Argument<string> packsRecoverIdArgument = new("run-id") { Description = "Stale running project pack run id." };
            Option<bool> packsRecoverInterruptedOption = new("--mark-interrupted")
            {
                Description = "Confirm the execute process is no longer active and record interrupted evidence."
            };
            packsRecoverInterruptedOption.Validators.Add(result =>
            {
                if (!result.GetValueOrDefault<bool>())
                {
                    result.AddError("--mark-interrupted is required for manual recovery.");
                }
            });
            Option<bool> packsRecoverJsonOption = new("--json") { Description = "Write one JSON project pack run object." };
            Option<string> packsRecoverOutputOption = new("--output") { Description = "Select text or json output." };
            packsRecoverOutputOption.DefaultValueFactory = _ => "text";
            AddTextJsonOutputValidator(packsRecoverOutputOption);
            packsRecoverCommand.Arguments.Add(packsRecoverIdArgument);
            packsRecoverCommand.Options.Add(packsRecoverInterruptedOption);
            packsRecoverCommand.Options.Add(packsRecoverJsonOption);
            packsRecoverCommand.Options.Add(packsRecoverOutputOption);
            packsRecoverCommand.SetAction(parseResult =>
            {
                string runId = parseResult.GetValue(packsRecoverIdArgument) ?? string.Empty;
                bool jsonOutput = IsJsonOutputRequested(
                    parseResult.GetValue(packsRecoverJsonOption),
                    parseResult.GetValue(packsRecoverOutputOption) ?? "text");
                CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(parseResult.GetValue(workspaceOption));
                DateTimeOffset nowUtc = utcNowProvider();
                ProjectPackRunMutationResult result = new ProjectPackRunService(
                    ManagedProjectPackRunStore.Create(snapshot)).MarkInterrupted(runId, nowUtc);
                if (!result.Succeeded || result.Record is null || result.Checkpoint is null)
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.recover",
                        result.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.TransitionInvalid,
                        result.Diagnostic?.Summary ?? "Stale running project pack run could not be marked interrupted.",
                        jsonOutput,
                        runId));
                    return 1;
                }

                if (!TryUpdateRecoveredProjectPackCorrelations(snapshot, result.Record, nowUtc, out string? correlationError))
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.recover",
                        "pack-recovery-correlation-failed",
                        correlationError ?? "Run was marked interrupted but correlated local metadata could not be updated.",
                        jsonOutput,
                        runId));
                    return 1;
                }

                output.WriteLine(jsonOutput
                    ? ProjectPackRunRenderer.RenderJson(result.Record, result.Checkpoint)
                    : ProjectPackRunRenderer.RenderText(result.Record, result.Checkpoint));
                return 0;
            });
            packsCommand.Subcommands.Add(packsRecoverCommand);

            Command packsResumeCommand = new("resume", "Revalidate and render a safe resume plan without replaying execute.");
            Argument<string> packsResumeIdArgument = new("run-id") { Description = "Project pack run id." };
            Option<bool> packsResumeDryRunOption = new("--dry-run") { Description = "Evaluate eligibility without executing any stage." };
            Option<string[]> packsResumeToolPathOption = new("--tool-path")
            {
                Description = "Revalidate [dependency=]absolute-path without starting the tool.",
                Arity = ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = true
            };
            Option<bool> packsResumeJsonOption = new("--json") { Description = "Write a single JSON resume eligibility object." };
            Option<string> packsResumeOutputOption = new("--output") { Description = "Select text or json output." };
            packsResumeOutputOption.DefaultValueFactory = _ => "text";
            AddTextJsonOutputValidator(packsResumeOutputOption);
            packsResumeCommand.Arguments.Add(packsResumeIdArgument);
            packsResumeCommand.Options.Add(packsResumeDryRunOption);
            packsResumeCommand.Options.Add(packsResumeToolPathOption);
            packsResumeCommand.Options.Add(packsResumeJsonOption);
            packsResumeCommand.Options.Add(packsResumeOutputOption);
            packsResumeCommand.SetAction(parseResult =>
            {
                string? workspacePath = parseResult.GetValue(workspaceOption);
                string runId = parseResult.GetValue(packsResumeIdArgument) ?? string.Empty;
                _ = parseResult.GetValue(packsResumeDryRunOption);
                bool jsonOutput = IsJsonOutputRequested(
                    parseResult.GetValue(packsResumeJsonOption),
                    parseResult.GetValue(packsResumeOutputOption) ?? "text");
                CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
                string[] toolPathValues = parseResult.GetValue(packsResumeToolPathOption) ?? [];
                GerberTiffWorkflowPack pack = new();
                if (!TryParseProjectPackToolPaths(pack.Manifest, toolPathValues,
                    out Dictionary<string, string> toolPaths, out string? bindingError))
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.resume", "pack-tool-binding-invalid", bindingError ?? "Project pack tool binding is invalid.", jsonOutput, runId));
                    return 2;
                }

                ProjectPackResumeEligibility eligibility = new ProjectPackRunService(ManagedProjectPackRunStore.Create(snapshot))
                    .EvaluateResume(
                        runId,
                        snapshot.Workspace,
                        toolPaths,
                        GetProjectPackPolicyFingerprint(snapshot, GerberTiffWorkflowPack.ProfileName),
                        CancellationToken.None);
                output.WriteLine(jsonOutput
                    ? ProjectPackRunRenderer.RenderResumeJson(runId, eligibility)
                    : ProjectPackRunRenderer.RenderResumeText(runId, eligibility));
                return eligibility.Eligible ? 0 : 1;
            });
            packsCommand.Subcommands.Add(packsResumeCommand);

            Command packsRestartCommand = new("restart", "Explicitly restart an interrupted execute stage as a new approved attempt.");
            Argument<string> packsRestartIdArgument = new("run-id") { Description = "Interrupted project pack run id." };
            Option<string> packsRestartFromOption = new("--from") { Description = "Restart boundary; v1 requires execute." };
            packsRestartFromOption.Validators.Add(result =>
            {
                if (result.Implicit || result.GetValueOrDefault<string>() != "execute")
                {
                    result.AddError("--from execute is required.");
                }
            });
            Option<string[]> packsRestartToolPathOption = new("--tool-path")
            {
                Description = "Revalidate [dependency=]absolute-path before reserving the new attempt.",
                Arity = ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = true
            };
            Option<bool> packsRestartApproveOption = new("--approve")
            {
                Description = "Approve this invocation's fixed probes and new-attempt conversion stages."
            };
            Option<string> packsRestartApprovalOption = new("--approval")
            {
                Description = "Override approval mode for this invocation only."
            };
            AddApprovalModeValidator(packsRestartApprovalOption);
            Option<bool> packsRestartJsonOption = new("--json") { Description = "Write one JSON restart result." };
            Option<string> packsRestartOutputOption = new("--output") { Description = "Select text or json output." };
            packsRestartOutputOption.DefaultValueFactory = _ => "text";
            AddTextJsonOutputValidator(packsRestartOutputOption);
            packsRestartCommand.Arguments.Add(packsRestartIdArgument);
            packsRestartCommand.Options.Add(packsRestartFromOption);
            packsRestartCommand.Options.Add(packsRestartToolPathOption);
            packsRestartCommand.Options.Add(packsRestartApproveOption);
            packsRestartCommand.Options.Add(packsRestartApprovalOption);
            packsRestartCommand.Options.Add(packsRestartJsonOption);
            packsRestartCommand.Options.Add(packsRestartOutputOption);
            packsRestartCommand.SetAction(parseResult =>
            {
                string parentRunId = parseResult.GetValue(packsRestartIdArgument) ?? string.Empty;
                bool jsonOutput = IsJsonOutputRequested(
                    parseResult.GetValue(packsRestartJsonOption),
                    parseResult.GetValue(packsRestartOutputOption) ?? "text");
                CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(parseResult.GetValue(workspaceOption));
                GerberTiffWorkflowPack pack = new();
                if (!TryParseProjectPackToolPaths(
                    pack.Manifest,
                    parseResult.GetValue(packsRestartToolPathOption) ?? [],
                    out Dictionary<string, string> toolPaths,
                    out string? bindingError))
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.restart",
                        "pack-tool-binding-invalid",
                        bindingError ?? "Project pack tool binding is invalid.",
                        jsonOutput,
                        parentRunId));
                    return 2;
                }

                IApprovalPolicy approvalPolicy = ApprovalPolicyResolver.Resolve(
                    snapshot.Configuration.ApprovalMode,
                    GetApprovalOverride(
                        parseResult.GetValue(packsRestartApprovalOption),
                        parseResult.GetResult(packsRestartApprovalOption),
                        parseResult.GetValue(packsRestartApproveOption)));
                ProjectPackDoctorReport probe = new ProjectPackDoctorService().Diagnose(
                    pack,
                    toolPaths,
                    trustedHashes: null,
                    probe: true,
                    approvalPolicy,
                    CancellationToken.None);
                if (!probe.Succeeded || probe.Tools.Any(tool => tool.Required && tool.Identity?.Version is null))
                {
                    string errorCode = MapProjectPackExecutionProbeError(probe);
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.restart",
                        errorCode,
                        "Restart requires current approved probes for every mandatory external tool.",
                        jsonOutput,
                        parentRunId));
                    return 1;
                }

                IReadOnlyDictionary<string, ExternalToolIdentity> identities = probe.Tools
                    .Where(tool => tool.Identity is not null)
                    .ToDictionary(tool => tool.DependencyId, tool => tool.Identity!, StringComparer.Ordinal);
                ManagedProjectPackRunStore runStore = ManagedProjectPackRunStore.Create(snapshot);
                ProjectPackRestartPreparation preparation = new ProjectPackRestartService(runStore).PrepareExecuteRestart(
                    parentRunId,
                    snapshot.Workspace,
                    toolPaths,
                    GetProjectPackPolicyFingerprint(snapshot, GerberTiffWorkflowPack.ProfileName),
                    utcNowProvider(),
                    CancellationToken.None);
                if (!preparation.Succeeded || preparation.Plan is null || preparation.NewRunId is null ||
                    preparation.ParentRecord is null || preparation.Attempt is null)
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.restart",
                        preparation.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.RestartRequired,
                        preparation.Diagnostic?.Summary ?? "New-attempt restart plan could not be reserved.",
                        jsonOutput,
                        parentRunId));
                    return 1;
                }

                DateTimeOffset nowUtc = utcNowProvider();
                string jobId = JobIdGenerator.Create(nowUtc);
                JobRecord job = JobRecord.CreateRunning(
                    jobId,
                    nowUtc,
                    new JobCommandSummary(
                        "packs restart",
                        Task: preparation.Plan.PlanId,
                        WorkspaceRoot: snapshot.Workspace.RootPath,
                        OutputMode: jsonOutput ? "json" : "text",
                        DryRun: false),
                    preparation.NewRunId);
                JobRecordStore jobStore = JobRecordStore.Create(snapshot);
                try
                {
                    jobStore.Create(job);
                }
                catch (Exception exception) when (IsJobStoreException(exception))
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.restart",
                        "job-record-write-failed",
                        "Reserved restart job record could not be created.",
                        jsonOutput,
                        preparation.NewRunId));
                    return 1;
                }

                ProjectPackRunCorrelation correlation = new(
                    jobId: jobId,
                    rootRunId: preparation.ParentRecord.Correlation.RootRunId ?? parentRunId,
                    parentRunId: parentRunId,
                    attempt: preparation.Attempt.Value);
                ProjectPackRunService runService = new(runStore);
                ProjectPackRunMutationResult result = runService.CreateAndStage(
                    preparation.Plan,
                    snapshot.Workspace,
                    toolPaths,
                    GetProjectPackPolicyFingerprint(snapshot, GerberTiffWorkflowPack.ProfileName),
                    nowUtc,
                    correlation,
                    preparation.NewRunId,
                    CancellationToken.None);
                if (result.Record?.State == ProjectPackRunState.Ready)
                {
                    result = runService.ExecuteControlled(
                        preparation.NewRunId,
                        preparation.Plan,
                        snapshot.Workspace,
                        toolPaths,
                        GetProjectPackPolicyFingerprint(snapshot, GerberTiffWorkflowPack.ProfileName),
                        new GerberTiffControlledConversionDriver(
                            preparation.Plan,
                            snapshot.Workspace,
                            toolPaths,
                            identities,
                            approvalPolicy),
                        utcNowProvider(),
                        CancellationToken.None);
                }

                List<JobArtifact> jobArtifacts = [];
                ManagedProjectPackRunLayout childLayout = runStore.GetLayout(preparation.NewRunId);
                if (File.Exists(childLayout.RunRecordPath))
                {
                    jobArtifacts.Add(JobArtifact.FromPath(
                        JobArtifactKind.ProjectPackRun,
                        childLayout.RunRecordPath,
                        $"packRunId={preparation.NewRunId}; parentRunId={parentRunId}; attempt={preparation.Attempt}"));
                }

                if (File.Exists(childLayout.InputManifestPath))
                {
                    jobArtifacts.Add(JobArtifact.FromPath(
                        JobArtifactKind.ProjectPackInputManifest,
                        childLayout.InputManifestPath,
                        "New-attempt immutable staged-input identity manifest."));
                }

                foreach (ProjectPackRunArtifactPointer pointer in result.Record?.Artifacts ?? [])
                {
                    if (pointer.Kind is not "conversion-execution-log" and not "tiff-output" and not "render-intermediate")
                    {
                        continue;
                    }

                    string? path = ResolveProjectPackArtifactPath(pointer, childLayout, snapshot.Workspace);
                    if (path is null || !File.Exists(path))
                    {
                        continue;
                    }

                    jobArtifacts.Add(JobArtifact.FromPath(
                        pointer.Kind == "conversion-execution-log"
                            ? JobArtifactKind.ProjectPackExecutionLog
                            : pointer.Kind == "tiff-output"
                                ? JobArtifactKind.ProjectPackConversionOutput
                                : pointer.Kind,
                        path,
                        "New-attempt controlled conversion evidence."));
                }

                bool succeeded = result.Record?.State == ProjectPackRunState.Verifying;
                try
                {
                    jobStore.Update(job.WithStatus(
                        succeeded ? JobStatus.Succeeded : JobStatus.Failed,
                        utcNowProvider(),
                        exitCode: succeeded ? 0 : 1,
                        stopReason: succeeded ? "restart-conversion-executed-verification-pending" : "restart-attempt-failed",
                        errorCode: succeeded ? null : result.Record?.ErrorCode ?? result.Diagnostic?.ErrorCode,
                        summary: succeeded
                            ? "New-attempt controlled conversion completed; TIFF verification remains pending."
                            : result.Record?.Summary ?? result.Diagnostic?.Summary,
                        taskReport: null,
                        artifacts: jobArtifacts,
                        warnings:
                        [
                            $"parentRunId={parentRunId}; newRunId={preparation.NewRunId}; attempt={preparation.Attempt}",
                            "Parent partial evidence was preserved and the new output directory used no-overwrite semantics.",
                            "Approval was invocation-local and was not persisted for resume or another restart."
                        ]));
                }
                catch (Exception exception) when (IsJobStoreException(exception))
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.restart",
                        "job-record-write-failed",
                        "Restart attempt completed without a safely updated job index.",
                        jsonOutput,
                        preparation.NewRunId));
                    return 1;
                }

                if (!succeeded || result.Record is null || result.Checkpoint is null)
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.restart",
                        result.Record?.ErrorCode ?? result.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.ExecutionFailed,
                        result.Record?.Summary ?? result.Diagnostic?.Summary ?? "Restart attempt failed.",
                        jsonOutput,
                        preparation.NewRunId));
                    return 1;
                }

                output.WriteLine(jsonOutput
                    ? ProjectPackRunRenderer.RenderJson(result.Record, result.Checkpoint)
                    : ProjectPackRunRenderer.RenderText(result.Record, result.Checkpoint));
                return 0;
            });
            packsCommand.Subcommands.Add(packsRestartCommand);

            Command packsVerifyCommand = new("verify", "Run bounded TIFF verification for one controlled conversion run.");
            Argument<string> packsVerifyIdArgument = new("run-id") { Description = "Project pack run id." };
            Option<string> packsVerifyBaselineOption = new("--baseline")
            {
                Description = "Use one strict schema-v1 baseline manifest from inside the workspace."
            };
            Option<bool> packsVerifyJsonOption = new("--json") { Description = "Write one JSON TIFF verification result." };
            Option<string> packsVerifyOutputOption = new("--output") { Description = "Select text or json output." };
            packsVerifyOutputOption.DefaultValueFactory = _ => "text";
            AddTextJsonOutputValidator(packsVerifyOutputOption);
            packsVerifyCommand.Arguments.Add(packsVerifyIdArgument);
            packsVerifyCommand.Options.Add(packsVerifyBaselineOption);
            packsVerifyCommand.Options.Add(packsVerifyJsonOption);
            packsVerifyCommand.Options.Add(packsVerifyOutputOption);
            packsVerifyCommand.SetAction(parseResult =>
            {
                string? workspacePath = parseResult.GetValue(workspaceOption);
                string runId = parseResult.GetValue(packsVerifyIdArgument) ?? string.Empty;
                string? baselinePath = parseResult.GetValue(packsVerifyBaselineOption);
                bool jsonOutput = IsJsonOutputRequested(
                    parseResult.GetValue(packsVerifyJsonOption),
                    parseResult.GetValue(packsVerifyOutputOption) ?? "text");
                CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
                WriteVerboseDiagnostics(parseResult, "packs verify", snapshot, humanReadableOutput: !jsonOutput);
                DiagnosticContext? traceContext = CreateTraceContext(parseResult, snapshot);
                TryWriteTraceCommandEvent(
                    "packs verify",
                    snapshot,
                    traceContext,
                    "command.start",
                    sequence: 1,
                    "started",
                    summary: "Bounded TIFF verification requested for an existing managed run.",
                    timestampUtc: utcNowProvider());

                TiffVerificationRunResult verification = new TiffVerificationService(
                    ManagedProjectPackRunStore.Create(snapshot)).Verify(
                        runId,
                        snapshot.Workspace,
                        baselinePath,
                        utcNowProvider(),
                        CancellationToken.None);
                if (verification.PersistenceDiagnostic is not null)
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.verify",
                        verification.PersistenceDiagnostic.Code,
                        verification.PersistenceDiagnostic.Summary,
                        jsonOutput,
                        runId));
                    return 1;
                }

                if (verification.Mutation?.Record is not null &&
                    !TryUpdateProjectPackVerificationJob(
                        snapshot,
                        verification.Mutation.Record,
                        previewCommand: false,
                        verification.Result.HardVerificationPassed,
                        utcNowProvider(),
                        out string? jobError))
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.verify",
                        "job-record-write-failed",
                        jobError ?? "TIFF verification job evidence could not be updated.",
                        jsonOutput,
                        runId));
                    return 1;
                }

                output.WriteLine(jsonOutput
                    ? TiffVerificationRenderer.RenderJson(verification.Result)
                    : TiffVerificationRenderer.RenderText(verification.Result));
                TryWriteTraceCommandEvent(
                    "packs verify",
                    snapshot,
                    traceContext,
                    "command.complete",
                    sequence: 2,
                    verification.Succeeded ? "success" : "failure",
                    summary: verification.Result.Summary,
                    errorCode: verification.Result.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == "error")?.Code,
                    timestampUtc: utcNowProvider());
                return verification.Succeeded ? 0 : 1;
            });
            packsCommand.Subcommands.Add(packsVerifyCommand);

            Command packsPreviewCommand = new("preview", "Generate managed PNG preview/contact sheet evidence after hard verification.");
            Argument<string> packsPreviewIdArgument = new("run-id") { Description = "Project pack run id." };
            Option<bool> packsPreviewJsonOption = new("--json") { Description = "Write one JSON TIFF preview result." };
            Option<string> packsPreviewOutputOption = new("--output") { Description = "Select text or json output." };
            packsPreviewOutputOption.DefaultValueFactory = _ => "text";
            AddTextJsonOutputValidator(packsPreviewOutputOption);
            packsPreviewCommand.Arguments.Add(packsPreviewIdArgument);
            packsPreviewCommand.Options.Add(packsPreviewJsonOption);
            packsPreviewCommand.Options.Add(packsPreviewOutputOption);
            packsPreviewCommand.SetAction(parseResult =>
            {
                string? workspacePath = parseResult.GetValue(workspaceOption);
                string runId = parseResult.GetValue(packsPreviewIdArgument) ?? string.Empty;
                bool jsonOutput = IsJsonOutputRequested(
                    parseResult.GetValue(packsPreviewJsonOption),
                    parseResult.GetValue(packsPreviewOutputOption) ?? "text");
                CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(workspacePath);
                WriteVerboseDiagnostics(parseResult, "packs preview", snapshot, humanReadableOutput: !jsonOutput);
                DiagnosticContext? traceContext = CreateTraceContext(parseResult, snapshot);
                TryWriteTraceCommandEvent(
                    "packs preview",
                    snapshot,
                    traceContext,
                    "command.start",
                    sequence: 1,
                    "started",
                    summary: "Managed PNG preview/contact sheet generation requested.",
                    timestampUtc: utcNowProvider());

                TiffPreviewRunResult preview = new TiffVerificationService(
                    ManagedProjectPackRunStore.Create(snapshot)).Preview(
                        runId,
                        snapshot.Workspace,
                        utcNowProvider(),
                        CancellationToken.None);
                if (preview.PersistenceDiagnostic is not null)
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.preview",
                        preview.PersistenceDiagnostic.Code,
                        preview.PersistenceDiagnostic.Summary,
                        jsonOutput,
                        runId));
                    return 1;
                }

                if (preview.Mutation?.Record is not null &&
                    !TryUpdateProjectPackVerificationJob(
                        snapshot,
                        preview.Mutation.Record,
                        previewCommand: true,
                        preview.Result.Succeeded,
                        utcNowProvider(),
                        out string? jobError))
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.preview",
                        "job-record-write-failed",
                        jobError ?? "TIFF preview job evidence could not be updated.",
                        jsonOutput,
                        runId));
                    return 1;
                }

                output.WriteLine(jsonOutput
                    ? TiffVerificationRenderer.RenderPreviewJson(preview.Result)
                    : TiffVerificationRenderer.RenderPreviewText(preview.Result));
                TryWriteTraceCommandEvent(
                    "packs preview",
                    snapshot,
                    traceContext,
                    "command.complete",
                    sequence: 2,
                    preview.Succeeded ? "success" : "failure",
                    summary: preview.Result.Summary,
                    errorCode: preview.Result.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == "error")?.Code,
                    timestampUtc: utcNowProvider());
                return preview.Succeeded ? 0 : 1;
            });
            packsCommand.Subcommands.Add(packsPreviewCommand);

            Command packsAcceptCommand = new("accept", "Record an explicit human acceptance after current hard verification.");
            Argument<string> packsAcceptIdArgument = new("run-id") { Description = "Project pack run id." };
            Option<string> packsAcceptActorOption = new("--actor") { Description = "Human reviewer identity for audit evidence." };
            packsAcceptActorOption.DefaultValueFactory = _ => Environment.UserName;
            Option<string> packsAcceptNoteOption = new("--note") { Description = "Optional redacted human review note." };
            Option<bool> packsAcceptJsonOption = new("--json") { Description = "Write one JSON project pack run object." };
            Option<string> packsAcceptOutputOption = new("--output") { Description = "Select text or json output." };
            packsAcceptOutputOption.DefaultValueFactory = _ => "text";
            AddTextJsonOutputValidator(packsAcceptOutputOption);
            packsAcceptCommand.Arguments.Add(packsAcceptIdArgument);
            packsAcceptCommand.Options.Add(packsAcceptActorOption);
            packsAcceptCommand.Options.Add(packsAcceptNoteOption);
            packsAcceptCommand.Options.Add(packsAcceptJsonOption);
            packsAcceptCommand.Options.Add(packsAcceptOutputOption);
            packsAcceptCommand.SetAction(parseResult =>
            {
                string runId = parseResult.GetValue(packsAcceptIdArgument) ?? string.Empty;
                bool jsonOutput = IsJsonOutputRequested(
                    parseResult.GetValue(packsAcceptJsonOption),
                    parseResult.GetValue(packsAcceptOutputOption) ?? "text");
                CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(parseResult.GetValue(workspaceOption));
                ProjectPackRunMutationResult result = new ProjectPackAcceptanceService(
                    ManagedProjectPackRunStore.Create(snapshot)).Accept(
                        runId,
                        parseResult.GetValue(packsAcceptActorOption) ?? Environment.UserName,
                        parseResult.GetValue(packsAcceptNoteOption),
                        utcNowProvider());
                if (!result.Succeeded || result.Record is null || result.Checkpoint is null)
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.accept",
                        result.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.AcceptanceNotEligible,
                        result.Diagnostic?.Summary ?? "Project pack run could not be accepted.",
                        jsonOutput,
                        runId));
                    return 1;
                }

                if (!TryUpdateProjectPackAcceptanceJob(snapshot, result.Record, utcNowProvider(), out string? jobError))
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.accept",
                        "job-record-write-failed",
                        jobError ?? "Human acceptance job evidence could not be updated.",
                        jsonOutput,
                        runId));
                    return 1;
                }

                output.WriteLine(jsonOutput
                    ? ProjectPackRunRenderer.RenderJson(result.Record, result.Checkpoint)
                    : ProjectPackRunRenderer.RenderText(result.Record, result.Checkpoint));
                return 0;
            });
            packsCommand.Subcommands.Add(packsAcceptCommand);

            Command packsRejectCommand = new("reject", "Record an explicit human rejection after current hard verification.");
            Argument<string> packsRejectIdArgument = new("run-id") { Description = "Project pack run id." };
            Option<string> packsRejectReasonOption = new("--reason") { Description = "Required redacted human rejection reason." };
            packsRejectReasonOption.Validators.Add(result =>
            {
                if (result.Implicit || string.IsNullOrWhiteSpace(result.GetValueOrDefault<string>()))
                {
                    result.AddError("--reason is required.");
                }
            });
            Option<string> packsRejectActorOption = new("--actor") { Description = "Human reviewer identity for audit evidence." };
            packsRejectActorOption.DefaultValueFactory = _ => Environment.UserName;
            Option<bool> packsRejectJsonOption = new("--json") { Description = "Write one JSON project pack run object." };
            Option<string> packsRejectOutputOption = new("--output") { Description = "Select text or json output." };
            packsRejectOutputOption.DefaultValueFactory = _ => "text";
            AddTextJsonOutputValidator(packsRejectOutputOption);
            packsRejectCommand.Arguments.Add(packsRejectIdArgument);
            packsRejectCommand.Options.Add(packsRejectReasonOption);
            packsRejectCommand.Options.Add(packsRejectActorOption);
            packsRejectCommand.Options.Add(packsRejectJsonOption);
            packsRejectCommand.Options.Add(packsRejectOutputOption);
            packsRejectCommand.SetAction(parseResult =>
            {
                string runId = parseResult.GetValue(packsRejectIdArgument) ?? string.Empty;
                bool jsonOutput = IsJsonOutputRequested(
                    parseResult.GetValue(packsRejectJsonOption),
                    parseResult.GetValue(packsRejectOutputOption) ?? "text");
                CliEnvironmentSnapshot snapshot = workspaceSnapshotProvider(parseResult.GetValue(workspaceOption));
                ProjectPackRunMutationResult result = new ProjectPackAcceptanceService(
                    ManagedProjectPackRunStore.Create(snapshot)).Reject(
                        runId,
                        parseResult.GetValue(packsRejectActorOption) ?? Environment.UserName,
                        parseResult.GetValue(packsRejectReasonOption) ?? string.Empty,
                        utcNowProvider());
                if (!result.Succeeded || result.Record is null || result.Checkpoint is null)
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.reject",
                        result.Diagnostic?.ErrorCode ?? ProjectPackRunErrorCode.AcceptanceNotEligible,
                        result.Diagnostic?.Summary ?? "Project pack run could not be rejected.",
                        jsonOutput,
                        runId));
                    return 1;
                }

                if (!TryUpdateProjectPackAcceptanceJob(snapshot, result.Record, utcNowProvider(), out string? jobError))
                {
                    output.WriteLine(ProjectPackRunRenderer.RenderFailure(
                        "packs.reject",
                        "job-record-write-failed",
                        jobError ?? "Human rejection job evidence could not be updated.",
                        jsonOutput,
                        runId));
                    return 1;
                }

                output.WriteLine(jsonOutput
                    ? ProjectPackRunRenderer.RenderJson(result.Record, result.Checkpoint)
                    : ProjectPackRunRenderer.RenderText(result.Record, result.Checkpoint));
                return 0;
            });
            packsCommand.Subcommands.Add(packsRejectCommand);

            return packsCommand;
        }
    }
}
