using System.Text.Json;
using System.Text;

namespace CSharpAiCli.Core;

public sealed class WorkspaceSearchTool : ITool
{
    private const string ToolName = "workspace.search_text";

    public const int DefaultMaxResults = 20;

    private const int MaximumMaxResults = 100;
    private const int MaxSnippetLength = 200;

    private readonly IWorkspaceGuard workspaceGuard;
    private readonly long maxFileBytes;

    public WorkspaceSearchTool(
        IWorkspaceGuard workspaceGuard,
        long maxFileBytes = WorkspaceFileReadTool.DefaultMaxFileBytes)
    {
        ArgumentNullException.ThrowIfNull(workspaceGuard);
        if (maxFileBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFileBytes), "File size limit must be greater than zero.");
        }

        this.workspaceGuard = workspaceGuard;
        this.maxFileBytes = maxFileBytes;
    }

    public ToolDefinition Definition { get; } = new(
        ToolName,
        "Search text files inside the current workspace.",
        """{"type":"object","properties":{"query":{"type":"string"},"path":{"type":"string"},"maxResults":{"type":"integer"}},"required":["query"]}""",
        ToolRiskLevel.Read);

    public ToolExecutionResult Execute(
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!TryReadArguments(context.ArgumentsJson, out SearchArguments arguments, out ToolExecutionResult? failure))
        {
            return failure;
        }

        WorkspaceGuardResult guardResult = workspaceGuard.ResolvePath(context.Workspace, arguments.Path);
        if (!guardResult.IsAllowed)
        {
            return ToolExecutionResult.Failure(
                guardResult.ErrorCode ?? ToolErrorCode.WorkspaceBoundaryDenied,
                guardResult.SafeMessage,
                structuredPayload: CreateSearchPayload(arguments));
        }

        string rootPath = guardResult.FullPath!;
        if (!Directory.Exists(rootPath))
        {
            return ToolExecutionResult.Failure(
                ToolErrorCode.SearchPathNotDirectory,
                "Search path must be a workspace directory.",
                structuredPayload: CreateSearchPayload(arguments));
        }

        List<string> matches = [];
        HashSet<string> visitedDirectories = new(GetPathComparer());
        SearchDirectory(
            context.Workspace,
            rootPath,
            arguments.Query,
            arguments.MaxResults,
            matches,
            visitedDirectories,
            cancellationToken);

        string summary = matches.Count == 0
            ? "No matches found."
            : string.Join(Environment.NewLine, matches);
        return ToolExecutionResult.Success(
            summary,
            structuredPayload: CreateSearchPayload(arguments, matches.Count));
    }

    private void SearchDirectory(
        WorkspaceContext workspace,
        string directoryPath,
        string query,
        int maxResults,
        List<string> matches,
        HashSet<string> visitedDirectories,
        CancellationToken cancellationToken)
    {
        WorkspaceGuardResult currentDirectory = workspaceGuard.ResolvePath(workspace, directoryPath);
        if (!currentDirectory.IsAllowed || currentDirectory.FullPath is null || !visitedDirectories.Add(currentDirectory.FullPath))
        {
            return;
        }

        foreach (string filePath in SafeEnumerateFiles(currentDirectory.FullPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (matches.Count >= maxResults)
            {
                return;
            }

            SearchFile(workspace, filePath, query, maxResults, matches);
        }

        foreach (string childDirectory in SafeEnumerateDirectories(currentDirectory.FullPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (matches.Count >= maxResults)
            {
                return;
            }

            WorkspaceGuardResult childResult = workspaceGuard.ResolvePath(workspace, childDirectory);
            if (!childResult.IsAllowed || childResult.FullPath is null)
            {
                continue;
            }

            SearchDirectory(workspace, childResult.FullPath, query, maxResults, matches, visitedDirectories, cancellationToken);
        }
    }

    private void SearchFile(
        WorkspaceContext workspace,
        string filePath,
        string query,
        int maxResults,
        List<string> matches)
    {
        WorkspaceGuardResult guardResult = workspaceGuard.ResolvePath(workspace, filePath);
        if (!guardResult.IsAllowed || guardResult.FullPath is null || !File.Exists(guardResult.FullPath))
        {
            return;
        }

        FileInfo fileInfo = new(guardResult.FullPath);
        if (fileInfo.Length > maxFileBytes || TextFileUtilities.IsLikelyBinary(guardResult.FullPath))
        {
            return;
        }

        try
        {
            int lineNumber = 0;
            foreach (string line in File.ReadLines(guardResult.FullPath))
            {
                lineNumber++;
                if (!line.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relativePath = Path.GetRelativePath(workspace.RootPath, guardResult.FullPath);
                matches.Add($"{relativePath}:{lineNumber}: {CreateSnippet(line)}");
                if (matches.Count >= maxResults)
                {
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or DecoderFallbackException)
        {
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string directoryPath)
    {
        try
        {
            return Directory.EnumerateFiles(directoryPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string directoryPath)
    {
        try
        {
            return Directory.EnumerateDirectories(directoryPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string CreateSnippet(string line)
    {
        string trimmed = line.Trim();
        return trimmed.Length <= MaxSnippetLength
            ? trimmed
            : trimmed[..MaxSnippetLength];
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    private static bool TryReadArguments(
        string argumentsJson,
        out SearchArguments arguments,
        out ToolExecutionResult failure)
    {
        arguments = default;
        failure = null!;

        try
        {
            using JsonDocument document = JsonDocument.Parse(argumentsJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                failure = ToolExecutionResult.Failure(
                    ToolErrorCode.InvalidToolArguments,
                    "Tool arguments must be a JSON object.",
                    structuredPayload: ToolStructuredPayload.InvalidArguments(ToolName, "arguments"));
                return false;
            }

            if (!root.TryGetProperty("query", out JsonElement queryElement) ||
                queryElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(queryElement.GetString()))
            {
                failure = ToolExecutionResult.Failure(
                    ToolErrorCode.InvalidToolArguments,
                    "Tool arguments must include a non-empty query.",
                    structuredPayload: ToolStructuredPayload.InvalidArguments(ToolName, "query"));
                return false;
            }

            string query = queryElement.GetString()!;
            string path = ".";
            if (root.TryGetProperty("path", out JsonElement pathElement))
            {
                if (pathElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(pathElement.GetString()))
                {
                    failure = ToolExecutionResult.Failure(
                        ToolErrorCode.InvalidToolArguments,
                        "Search path must be a non-empty string.",
                        structuredPayload: ToolStructuredPayload.InvalidArguments(ToolName, "path"));
                    return false;
                }

                path = pathElement.GetString()!;
            }

            int maxResults = DefaultMaxResults;
            if (root.TryGetProperty("maxResults", out JsonElement maxResultsElement))
            {
                if (maxResultsElement.ValueKind != JsonValueKind.Number ||
                    !maxResultsElement.TryGetInt32(out maxResults) ||
                    maxResults <= 0)
                {
                    failure = ToolExecutionResult.Failure(
                        ToolErrorCode.InvalidToolArguments,
                        "maxResults must be a positive integer.",
                        structuredPayload: ToolStructuredPayload.InvalidArguments(ToolName, "maxResults"));
                    return false;
                }

                maxResults = Math.Min(maxResults, MaximumMaxResults);
            }

            arguments = new SearchArguments(query, path, maxResults);
            return true;
        }
        catch (JsonException)
        {
            failure = ToolExecutionResult.Failure(
                ToolErrorCode.InvalidToolArguments,
                "Tool arguments must be valid JSON.",
                structuredPayload: ToolStructuredPayload.InvalidArguments(ToolName, "arguments"));
            return false;
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> CreateSearchPayload(
        SearchArguments arguments,
        int? matchCount = null)
    {
        return matchCount is null
            ? ToolStructuredPayload.Create(
                ("query", arguments.Query),
                ("path", arguments.Path),
                ("maxResults", arguments.MaxResults))
            : ToolStructuredPayload.Create(
                ("query", arguments.Query),
                ("path", arguments.Path),
                ("matchCount", matchCount.Value),
                ("maxResults", arguments.MaxResults));
    }

    private readonly record struct SearchArguments(string Query, string Path, int MaxResults);
}
