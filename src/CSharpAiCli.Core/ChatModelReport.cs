namespace CSharpAiCli.Core;

public sealed record ChatModelReport(IReadOnlyList<string> Lines)
{
    public static ChatModelReport Create(CliEnvironmentSnapshot snapshot, ChatModelResult result)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(result);

        List<string> lines =
        [
            $"{ProductInfo.DisplayName} chat",
            $"workspace: {snapshot.CurrentDirectory}",
            $"workspace status: {FormatWorkspaceStatus(snapshot.WorkspaceStatus)}",
            $"api key: {(snapshot.Configuration.HasApiKey ? "present" : "missing")}",
            $"api key source: {snapshot.Configuration.ApiKeySource}"
        ];

        if (result.Response is not null)
        {
            lines.Add("status: completed");
            lines.Add($"provider: {result.Response.Provider}");
            lines.Add($"model: {result.Response.Model}");
            lines.Add($"responseId: {result.Response.ResponseId}");
            lines.Add(string.Empty);
            lines.Add(result.Response.Text);
            return new ChatModelReport(lines);
        }

        ModelError error = result.Error ?? new ModelError(
            Provider: "unknown",
            Operation: "unknown",
            StatusCode: null,
            LocalErrorCode: "missing-error",
            SafeMessage: "Model call failed without a detailed error.",
            Retryable: false);

        lines.Add("status: failed");
        lines.Add($"provider: {error.Provider}");
        lines.Add($"operation: {error.Operation}");
        lines.Add($"statusCode: {(error.StatusCode.HasValue ? error.StatusCode.Value.ToString() : "none")}");
        lines.Add($"localErrorCode: {error.LocalErrorCode ?? "none"}");
        lines.Add($"safeMessage: {error.SafeMessage}");
        lines.Add($"retryable: {error.Retryable.ToString().ToLowerInvariant()}");

        return new ChatModelReport(lines);
    }

    public string ToDisplayText()
    {
        return string.Join(Environment.NewLine, Lines);
    }

    private static string FormatWorkspaceStatus(WorkspaceStatus status)
    {
        return status switch
        {
            WorkspaceStatus.Ready => "ready",
            WorkspaceStatus.Missing => "missing",
            WorkspaceStatus.NotDirectory => "not directory",
            _ => "unknown"
        };
    }
}
