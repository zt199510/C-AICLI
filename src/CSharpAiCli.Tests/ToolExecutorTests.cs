using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ToolExecutorTests
{
    [Fact]
    public void Execute_invokes_registered_tool_with_normalized_arguments()
    {
        ToolRegistry registry = new();
        EchoTool tool = new();
        registry.Register(tool);
        ToolExecutor executor = new(registry);
        ToolExecutionContext context = CreateContext("""
        {
          "text": "hello"
        }
        """);

        ToolExecutionResult result = executor.Execute("test.echo", context);

        Assert.True(result.Succeeded);
        Assert.Equal("""{"text":"hello"}""", result.Summary);
        Assert.Null(result.ErrorCode);
        Assert.Equal("""{"text":"hello"}""", tool.LastArgumentsJson);
    }

    [Fact]
    public void Execute_returns_failure_for_unknown_tool()
    {
        ToolExecutor executor = new(new ToolRegistry());

        ToolExecutionResult result = executor.Execute("missing.tool", CreateContext("{}"));

        Assert.False(result.Succeeded);
        Assert.Equal(ToolErrorCode.UnknownTool, result.ErrorCode);
        Assert.Contains("missing.tool", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_returns_disabled_failure_before_registry_lookup()
    {
        ToolExecutor executor = new(
            new ToolRegistry(),
            new HashSet<string>(StringComparer.Ordinal)
            {
                "missing.tool"
            });

        ToolExecutionResult result = executor.Execute("missing.tool", CreateContext("{}"));

        Assert.False(result.Succeeded);
        Assert.Equal(ToolErrorCode.ToolDisabled, result.ErrorCode);
        Assert.Contains("disabled", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_returns_boundary_failure_before_invoking_disallowed_tool()
    {
        ToolRegistry registry = new();
        EchoTool writeTool = new();
        registry.Register(writeTool);
        ToolExecutor executor = new(
            registry,
            boundary: ToolExecutionBoundary.ReadOnly());

        ToolExecutionResult result = executor.Execute("test.echo", CreateContext("{}"));

        Assert.False(result.Succeeded);
        Assert.Equal(ToolErrorCode.ToolDisabled, result.ErrorCode);
        Assert.Contains("active tool boundary", result.Summary, StringComparison.Ordinal);
        Assert.Null(writeTool.LastArgumentsJson);
    }

    [Fact]
    public void Execute_returns_boundary_failure_for_disabled_prefix_before_registry_lookup()
    {
        ToolExecutor executor = new(
            new ToolRegistry(),
            boundary: ToolExecutionBoundary.ReadOnly());

        ToolExecutionResult result = executor.Execute("mcp.server.tool", CreateContext("{}"));

        Assert.False(result.Succeeded);
        Assert.Equal(ToolErrorCode.ToolDisabled, result.ErrorCode);
        Assert.Contains("active tool boundary", result.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    public void Execute_returns_failure_for_invalid_arguments(string argumentsJson)
    {
        ToolRegistry registry = new();
        registry.Register(new EchoTool());
        ToolExecutor executor = new(registry);

        ToolExecutionResult result = executor.Execute("test.echo", CreateContext(argumentsJson));

        Assert.False(result.Succeeded);
        Assert.Equal(ToolErrorCode.InvalidToolArguments, result.ErrorCode);
    }

    [Fact]
    public void Execute_returns_canonical_failure_when_tool_returns_null()
    {
        ToolRegistry registry = new();
        registry.Register(new NullTool());
        ToolExecutor executor = new(registry);

        ToolExecutionResult result = executor.Execute("test.null", CreateContext("{}"));

        Assert.False(result.Succeeded);
        Assert.Equal(ToolErrorCode.ToolReturnedNull, result.ErrorCode);
    }

    [Fact]
    public void Execute_rejects_write_tool_during_planning_phase()
    {
        ToolRegistry registry = new();
        EchoTool tool = new();
        registry.Register(tool);
        ToolExecutor executor = new(registry);

        ToolExecutionResult result = executor.Execute(
            "test.echo",
            CreateContext("{}") with { Phase = ToolExecutionPhase.Planning });

        Assert.False(result.Succeeded);
        Assert.Equal(ToolErrorCode.PlanningPhaseWriteDenied, result.ErrorCode);
        Assert.Equal("Only read tools can run during the planning phase.", result.Summary);
        Assert.Null(tool.LastArgumentsJson);
        IReadOnlyDictionary<string, JsonElement> payload =
            result.StructuredPayload ?? throw new InvalidOperationException("Structured payload was not set.");
        Assert.Equal("planning", payload["phase"].GetString());
        Assert.Equal("write", payload["riskLevel"].GetString());
    }

    [Fact]
    public void Execute_converts_tool_security_exception_to_safe_failure()
    {
        ToolRegistry registry = new();
        registry.Register(new ThrowingTool(new ToolSecurityException(
            "workspace-boundary-denied",
            "Tool request was outside the workspace.")));
        ToolExecutor executor = new(registry);

        ToolExecutionResult result = executor.Execute("test.throw", CreateContext("{}"));

        Assert.False(result.Succeeded);
        Assert.Equal("workspace-boundary-denied", result.ErrorCode);
        Assert.Equal("Tool request was outside the workspace.", result.Summary);
        Assert.False(result.Retryable);
    }

    [Fact]
    public void Execute_converts_unexpected_exception_to_generic_failure()
    {
        ToolRegistry registry = new();
        registry.Register(new ThrowingTool(new InvalidOperationException("secret sk-hidden should not leak")));
        ToolExecutor executor = new(registry);

        ToolExecutionResult result = executor.Execute("test.throw", CreateContext("{}"));

        Assert.False(result.Succeeded);
        Assert.Equal(ToolErrorCode.ToolExecutionFailed, result.ErrorCode);
        Assert.Equal("Tool 'test.throw' failed during execution.", result.Summary);
        Assert.DoesNotContain("sk-hidden", result.Summary, StringComparison.Ordinal);
    }

    private static ToolExecutionContext CreateContext(string argumentsJson)
    {
        WorkspaceContext workspace = new(
            RootPath: Path.GetTempPath(),
            ConfigPath: Path.Combine(Path.GetTempPath(), ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        return new ToolExecutionContext(
            CallId: "call_1",
            Workspace: workspace,
            ArgumentsJson: argumentsJson);
    }

    private sealed class EchoTool : ITool
    {
        public ToolDefinition Definition { get; } = new(
            "test.echo",
            "Echoes arguments.",
            """{"type":"object"}""");

        public string? LastArgumentsJson { get; private set; }

        public ToolExecutionResult Execute(
            ToolExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            LastArgumentsJson = context.ArgumentsJson;
            JsonDocument.Parse(context.ArgumentsJson);
            return ToolExecutionResult.Success(context.ArgumentsJson);
        }
    }

    private sealed class ThrowingTool(Exception exception) : ITool
    {
        public ToolDefinition Definition { get; } = new(
            "test.throw",
            "Throws.",
            """{"type":"object"}""");

        public ToolExecutionResult Execute(
            ToolExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            throw exception;
        }
    }

    private sealed class NullTool : ITool
    {
        public ToolDefinition Definition { get; } = new(
            "test.null",
            "Returns null.",
            """{"type":"object"}""");

        public ToolExecutionResult Execute(
            ToolExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            return null!;
        }
    }
}
