using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ToolRegistryTests
{
    [Fact]
    public void Tool_risk_levels_expose_canonical_names()
    {
        Assert.Equal("read", ToolRiskLevel.Read.ToCanonicalName());
        Assert.Equal("write", ToolRiskLevel.Write.ToCanonicalName());
        Assert.Equal("shell", ToolRiskLevel.Shell.ToCanonicalName());
        Assert.Equal("dangerous-shell", ToolRiskLevel.DangerousShell.ToCanonicalName());
    }

    [Fact]
    public void Tool_definition_defaults_to_write_risk_for_compatibility()
    {
        ToolDefinition definition = new(
            "test.compat",
            "Compatibility tool.",
            """{"type":"object"}""");

        Assert.Equal(ToolRiskLevel.Write, definition.RiskLevel);
    }

    [Fact]
    public void Register_lists_and_finds_tool_by_name()
    {
        ToolRegistry registry = new();
        EchoTool tool = new("test.echo");

        registry.Register(tool);

        ToolDefinition definition = Assert.Single(registry.List());
        Assert.Equal("test.echo", definition.Name);
        Assert.Equal("Echoes a text argument.", definition.Description);
        Assert.Equal(ToolRiskLevel.Read, definition.RiskLevel);
        Assert.True(registry.TryGet("test.echo", out ITool? found));
        Assert.Same(tool, found);
    }

    [Fact]
    public void Register_rejects_duplicate_tool_names()
    {
        ToolRegistry registry = new();
        registry.Register(new EchoTool("test.echo"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => registry.Register(new EchoTool("test.echo")));

        Assert.Contains("already registered", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Try_get_returns_false_for_missing_or_blank_name()
    {
        ToolRegistry registry = new();

        Assert.False(registry.TryGet("missing", out ITool? missing));
        Assert.Null(missing);
        Assert.False(registry.TryGet(" ", out ITool? blank));
        Assert.Null(blank);
    }

    private sealed class EchoTool(string name) : ITool
    {
        public ToolDefinition Definition { get; } = new(
            name,
            "Echoes a text argument.",
            """{"type":"object","properties":{"text":{"type":"string"}}}""",
            ToolRiskLevel.Read);

        public ToolExecutionResult Execute(
            ToolExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            return ToolExecutionResult.Success(context.ArgumentsJson);
        }
    }
}
