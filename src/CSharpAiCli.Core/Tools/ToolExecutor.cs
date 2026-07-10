using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class ToolExecutor : IToolExecutor
{
    private readonly IToolRegistry registry;
    private readonly IReadOnlySet<string> disabledTools;

    public ToolExecutor(
        IToolRegistry registry,
        IReadOnlySet<string>? disabledTools = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        this.registry = registry;
        this.disabledTools = disabledTools ?? new HashSet<string>(StringComparer.Ordinal);
    }

    public ToolExecutionResult Execute(
        string toolName,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (disabledTools.Contains(toolName))
        {
            return ToolExecutionResult.Failure(
                ToolErrorCode.ToolDisabled,
                $"Tool '{toolName}' is disabled by configuration.");
        }

        if (!registry.TryGet(toolName, out ITool? tool) || tool is null)
        {
            return ToolExecutionResult.Failure(
                ToolErrorCode.UnknownTool,
                $"Tool '{toolName}' is not registered.");
        }

        ITool executableTool = tool;
        if (!TryNormalizeArguments(context.ArgumentsJson, out string normalizedArguments, out ToolExecutionResult? failure))
        {
            return failure;
        }

        ToolExecutionContext normalizedContext = context with { ArgumentsJson = normalizedArguments };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return executableTool.Execute(normalizedContext, cancellationToken)
                ?? ToolExecutionResult.Failure(
                    ToolErrorCode.ToolReturnedNull,
                    "Tool returned no execution result.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ToolExecutionException exception)
        {
            return ToolExecutionResult.Failure(
                exception.ErrorCode,
                exception.SafeMessage,
                exception.Retryable);
        }
        catch (JsonException)
        {
            return ToolExecutionResult.Failure(
                ToolErrorCode.InvalidToolArguments,
                "Tool arguments were invalid.");
        }
        catch (ArgumentException)
        {
            return ToolExecutionResult.Failure(
                ToolErrorCode.InvalidToolArguments,
                "Tool arguments were invalid.");
        }
        catch
        {
            return ToolExecutionResult.Failure(
                ToolErrorCode.ToolExecutionFailed,
                $"Tool '{executableTool.Definition.Name}' failed during execution.");
        }
    }

    private static bool TryNormalizeArguments(
        string? argumentsJson,
        out string normalizedArguments,
        out ToolExecutionResult failure)
    {
        normalizedArguments = string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson;
        failure = null!;

        try
        {
            using JsonDocument document = JsonDocument.Parse(normalizedArguments);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                failure = ToolExecutionResult.Failure(
                    ToolErrorCode.InvalidToolArguments,
                    "Tool arguments must be a JSON object.");
                return false;
            }

            normalizedArguments = JsonSerializer.Serialize(document.RootElement);
            return true;
        }
        catch (JsonException)
        {
            failure = ToolExecutionResult.Failure(
                ToolErrorCode.InvalidToolArguments,
                "Tool arguments must be valid JSON.");
            return false;
        }
    }
}
