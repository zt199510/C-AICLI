using System.CommandLine;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class DoctorCommandModule : ICliCommandModule
{
    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("doctor", "Inspect runtime, workspace, and configuration readiness.");
        command.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            DiagnosticContext? traceContext = context.CreateTraceContext(parseResult, snapshot);
            context.TryWriteTraceCommandEvent(
                "doctor",
                snapshot,
                traceContext,
                "command.start",
                0,
                "started",
                timestampUtc: context.Dependencies.UtcNowProvider());
            context.TryWriteCommandLog("doctor", snapshot);
            context.WriteVerboseDiagnostics(parseResult, "doctor", snapshot);
            context.Output.WriteLine(DoctorReport.Create(snapshot).ToDisplayText());
            context.TryWriteTraceCommandEvent(
                "doctor",
                snapshot,
                traceContext,
                "command.complete",
                1,
                "success",
                timestampUtc: context.Dependencies.UtcNowProvider());
            return 0;
        });
        return command;
    }
}
