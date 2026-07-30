using System.CommandLine;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class RunCommandModule : ICliCommandModule
{
    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("run", "Run a deterministic local workspace task through the direct tool layer.");
        Argument<string> taskArgument = new("task")
        {
            Description = "Task text. Supported smoke tasks: create smoke note, read <path>, shell <command>.",
        };
        Option<bool> approveOption = new("--approve")
        {
            Description = "Approve patch or shell tools used by this run.",
        };
        command.Arguments.Add(taskArgument);
        command.Options.Add(approveOption);
        command.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            string task = parseResult.GetValue(taskArgument) ?? string.Empty;
            bool approve = parseResult.GetValue(approveOption);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.TryWriteCommandLog("run", snapshot);
            context.WriteVerboseDiagnostics(parseResult, "run", snapshot);
            ApprovalMode? cliApprovalMode = approve ? ApprovalMode.Always : null;
            IApprovalPolicy approvalPolicy = ApprovalPolicyResolver.Resolve(
                snapshot.Configuration.ApprovalMode,
                cliApprovalMode);
            ToolRegistry registry = CliToolFactory.CreateRegistry(snapshot, approvalPolicy);
            ToolExecutor executor = new(registry, snapshot.Configuration.DisabledTools);
            ExecRunner runner = new(approvalPolicy);
            ExecRequest request = new(task, WorkspaceRoot: snapshot.Workspace.RootPath);
            ExecResult result = runner.Run(request, snapshot.Workspace, executor);

            WriteResult(context.Output, result);
            return result.ExitCode;
        });
        return command;
    }

    private static void WriteResult(TextWriter output, ExecResult result)
    {
        output.WriteLine(result.IsSuccess ? "status: succeeded" : "status: failed");
        output.WriteLine($"approvalStatus: {result.ApprovalStatus ?? "not-required"}");
        if (!string.IsNullOrWhiteSpace(result.ErrorCode))
        {
            output.WriteLine($"errorCode: {result.ErrorCode}");
        }

        output.WriteLine("summary:");
        output.WriteLine(result.Summary);
    }
}
