using System.CommandLine;
using CSharpAiCli.Application;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class CliCommandContext
{
    public CliCommandContext(
        TextWriter output,
        TextReader input,
        CliDependencies dependencies,
        CliGlobalOptions globalOptions)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(globalOptions);

        Output = output;
        Input = input;
        Dependencies = dependencies;
        GlobalOptions = globalOptions;
        WorkspaceSnapshotProvider = workspacePath => dependencies.SnapshotProvider(workspacePath, null);
        ChangesServiceFactory = _ => new ChangesApplicationService(dependencies.ConversationStoreFactory);
    }

    public TextWriter Output { get; }

    public TextReader Input { get; }

    public CliDependencies Dependencies { get; }

    public CliGlobalOptions GlobalOptions { get; }

    public Func<string?, CliEnvironmentSnapshot> WorkspaceSnapshotProvider { get; }

    public Func<CliEnvironmentSnapshot, ChangesApplicationService> ChangesServiceFactory { get; }

    public void WriteVerboseDiagnostics(
        ParseResult parseResult,
        string commandName,
        CliEnvironmentSnapshot snapshot,
        bool humanReadableOutput = true)
    {
        if (!humanReadableOutput || !parseResult.GetValue(GlobalOptions.Verbose))
        {
            return;
        }

        DiagnosticContext context = DiagnosticContext.Create(
            workspace: snapshot.CurrentDirectory,
            utcNowProvider: Dependencies.UtcNowProvider);
        Output.WriteLine(VerboseDiagnosticsReport.Create(commandName, snapshot, context).ToDisplayText());
        Output.WriteLine();
    }

    public bool IsTraceEnabled(ParseResult parseResult)
    {
        return parseResult.GetValue(GlobalOptions.Trace) ||
            string.Equals(
                Dependencies.EnvironmentVariableProvider("CAICLI_TRACE"),
                "1",
                StringComparison.Ordinal);
    }

    public DiagnosticContext? CreateTraceContext(
        ParseResult parseResult,
        CliEnvironmentSnapshot snapshot)
    {
        return IsTraceEnabled(parseResult)
            ? DiagnosticContext.Create(
                workspace: snapshot.CurrentDirectory,
                utcNowProvider: Dependencies.UtcNowProvider)
            : null;
    }

    public void TryWriteTraceCommandEvent(
        string commandName,
        CliEnvironmentSnapshot snapshot,
        DiagnosticContext? context,
        string type,
        long sequence,
        string status,
        string? summary = null,
        string? errorCode = null,
        DateTimeOffset? timestampUtc = null)
    {
        if (context is null)
        {
            return;
        }

        try
        {
            TraceLogger.AppendCommandEvent(
                commandName,
                snapshot,
                context,
                type,
                sequence,
                status,
                summary,
                errorCode,
                timestampUtc);
        }
        catch
        {
        }
    }

    public void TryWriteTraceExecResult(
        string commandName,
        CliEnvironmentSnapshot snapshot,
        DiagnosticContext? context,
        ExecResult result)
    {
        if (context is null)
        {
            return;
        }

        try
        {
            TraceLogger.AppendExecResult(commandName, snapshot, context, result);
        }
        catch
        {
        }
    }

    public void TryWriteCommandLog(string commandName, CliEnvironmentSnapshot snapshot)
    {
        try
        {
            Dependencies.CommandLogger(commandName, snapshot);
        }
        catch
        {
        }
    }

    public static bool IsJsonOutputRequested(bool jsonRequested, string outputMode)
    {
        return jsonRequested ||
            string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase);
    }

    public void WriteToolResult(ToolExecutionResult result)
    {
        Output.WriteLine(result.Succeeded ? "status: succeeded" : "status: failed");
        Output.WriteLine($"approvalStatus: {result.ApprovalStatus}");
        if (!string.IsNullOrWhiteSpace(result.ErrorCode))
        {
            Output.WriteLine($"errorCode: {result.ErrorCode}");
        }

        Output.WriteLine("summary:");
        Output.WriteLine(result.Summary);
    }

    public void WriteSafeFailure(string errorCode, string summary)
    {
        Output.WriteLine("status: failed");
        Output.WriteLine($"errorCode: {errorCode}");
        Output.WriteLine("summary:");
        Output.WriteLine(summary);
    }

    public bool TryParseSessionName(string name, out ConversationSessionName sessionName)
    {
        try
        {
            sessionName = ConversationSessionName.Parse(name);
            return true;
        }
        catch (ArgumentException exception)
        {
            WriteSafeFailure("invalid-session-name", GetSafeSessionNameParseMessage(exception));
            sessionName = null!;
            return false;
        }
    }

    private static string GetSafeSessionNameParseMessage(ArgumentException exception)
    {
        string message = exception.Message;
        if (string.IsNullOrEmpty(exception.ParamName))
        {
            return message;
        }

        string parameterSuffix = $" (Parameter '{exception.ParamName}')";
        return message.EndsWith(parameterSuffix, StringComparison.Ordinal)
            ? message[..^parameterSuffix.Length]
            : message;
    }
}
