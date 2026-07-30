using System.CommandLine;
using CSharpAiCli.Application;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class ChangesCommandModule : ICliCommandModule
{
    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("changes", "Show a read-only changes view for the workspace.");
        Option<bool> jsonOption = new("--json")
        {
            Description = "Write a single JSON changes view object.",
        };
        Option<string> outputOption = new("--output")
        {
            Description = "Select text or json output.",
        };
        Option<string> sessionOption = new("--session")
        {
            Description = "Include the latest task report from a named session transcript.",
        };
        outputOption.DefaultValueFactory = _ => "text";
        outputOption.Validators.Add(result =>
        {
            string outputMode = result.GetValueOrDefault<string>() ?? "text";
            if (!string.Equals(outputMode, "text", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError("Invalid value for --output. Allowed values are text and json.");
            }
        });
        command.Options.Add(jsonOption);
        command.Options.Add(outputOption);
        command.Options.Add(sessionOption);
        command.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            bool jsonRequested = parseResult.GetValue(jsonOption);
            string outputMode = parseResult.GetValue(outputOption) ?? "text";
            string? session = parseResult.GetValue(sessionOption);
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            DiagnosticContext? traceContext = context.CreateTraceContext(parseResult, snapshot);
            context.WriteVerboseDiagnostics(
                parseResult,
                "changes",
                snapshot,
                humanReadableOutput: !CliCommandContext.IsJsonOutputRequested(jsonRequested, outputMode));

            ConversationSessionName? sessionName = null;
            if (!string.IsNullOrWhiteSpace(session))
            {
                if (!context.TryParseSessionName(session, out ConversationSessionName parsedSessionName))
                {
                    return 1;
                }

                sessionName = parsedSessionName;
            }

            ApplicationResult<ChangesViewReport> query = context.ChangesServiceFactory(snapshot).Query(
                new ChangesQueryRequest(snapshot, sessionName));
            if (!query.Succeeded || query.Data is null)
            {
                ApplicationError error = query.Error ?? new ApplicationError(
                    "application-internal",
                    ApplicationErrorCategory.Internal,
                    "Changes query failed.",
                    Retryable: false);
                context.WriteSafeFailure(error.Code, error.SafeMessage);
                return 1;
            }

            ChangesViewReport report = query.Data;
            context.TryWriteTraceCommandEvent(
                "changes",
                snapshot,
                traceContext,
                "changes.view",
                0,
                report.Status,
                report.Summary,
                timestampUtc: context.Dependencies.UtcNowProvider());

            if (CliCommandContext.IsJsonOutputRequested(jsonRequested, outputMode))
            {
                new ChangesJsonRenderer(context.Output).Write(report);
            }
            else
            {
                new ChangesTextRenderer(context.Output).Write(report);
            }

            return report.ExitCode;
        });
        return command;
    }
}
