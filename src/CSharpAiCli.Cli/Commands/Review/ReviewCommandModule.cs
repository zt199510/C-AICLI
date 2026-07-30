using System.CommandLine;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class ReviewCommandModule : ICliCommandModule
{
    public Command Create(CliCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("review", "Review the current git diff with the configured model.");
        Option<bool> jsonOption = new("--json")
        {
            Description = "Write a single JSON review result object.",
        };
        Option<string> outputOption = new("--output")
        {
            Description = "Select text or json output.",
        };
        outputOption.DefaultValueFactory = _ => "text";
        CliCommandContext.AddTextJsonOutputValidator(outputOption);
        command.Options.Add(jsonOption);
        command.Options.Add(outputOption);
        command.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(context.GlobalOptions.Workspace);
            bool jsonRequested = parseResult.GetValue(jsonOption);
            string outputMode = parseResult.GetValue(outputOption) ?? "text";
            CliEnvironmentSnapshot snapshot = context.WorkspaceSnapshotProvider(workspacePath);
            context.WriteVerboseDiagnostics(
                parseResult,
                "review",
                snapshot,
                humanReadableOutput: !CliCommandContext.IsJsonOutputRequested(jsonRequested, outputMode));

            GitDiffTool gitDiffTool = new(new WorkspaceGuard());
            ToolExecutionResult gitDiff = gitDiffTool.Execute(new ToolExecutionContext(
                "cli_review",
                snapshot.Workspace,
                "{}"));
            if (!gitDiff.Succeeded)
            {
                WriteReport(context.Output, ReviewReport.ToolFailure(gitDiff), jsonRequested, outputMode);
                return 1;
            }

            string prompt = ReviewPromptBuilder.Build(gitDiff.Summary);
            ChatRequest request = new(prompt, Instructions: snapshot.Instructions.Instructions);
            ChatModelResult result = context.Dependencies.ChatModelClientFactory(snapshot).Send(request);
            if (result.Response is not null)
            {
                ChatResponse response = gitDiff.Summary.Contains(GitDiffTool.TruncationWarning, StringComparison.Ordinal)
                    ? PrependWarning(result.Response, GitDiffTool.TruncationWarning)
                    : result.Response;
                WriteReport(context.Output, ReviewReport.Completed(response), jsonRequested, outputMode);
                return 0;
            }

            WriteReport(context.Output, ReviewReport.ModelFailure(result), jsonRequested, outputMode);
            return 1;
        });
        return command;
    }

    private static void WriteReport(
        TextWriter output,
        ReviewReport report,
        bool jsonRequested,
        string outputMode)
    {
        output.WriteLine(CliCommandContext.IsJsonOutputRequested(jsonRequested, outputMode)
            ? report.ToJson()
            : report.ToDisplayText());
    }

    private static ChatResponse PrependWarning(ChatResponse response, string warning)
    {
        string text = string.IsNullOrWhiteSpace(response.Text)
            ? warning
            : warning + Environment.NewLine + Environment.NewLine + response.Text.TrimStart('\r', '\n');
        return response with { Text = text };
    }
}
