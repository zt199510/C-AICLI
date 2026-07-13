using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ExpertProfileTests
{
    [Theory]
    [InlineData("bugfix", false)]
    [InlineData("reviewer", true)]
    [InlineData("tester", false)]
    [InlineData("security", true)]
    [InlineData("refactor", false)]
    public void Catalog_returns_supported_profiles(string name, bool readOnly)
    {
        Assert.True(ExpertProfileCatalog.TryGet(name, out ExpertProfile? profile));

        Assert.NotNull(profile);
        Assert.Equal(name, profile!.Name);
        Assert.Equal(readOnly, profile.IsReadOnly);
    }

    [Fact]
    public void Catalog_rejects_unknown_profile()
    {
        Assert.False(ExpertProfileCatalog.TryGet("unknown", out ExpertProfile? profile));
        Assert.Null(profile);
    }

    [Fact]
    public void Prompt_formatter_injects_expert_guidance_without_changing_task_text()
    {
        ExpertProfile profile = ExpertProfileCatalog.GetOrNull("reviewer")!;

        string prompt = ExpertProfilePromptFormatter.FormatWithCurrentPrompt(
            profile,
            "Review src/App.cs");

        Assert.Contains("Expert profile:", prompt, StringComparison.Ordinal);
        Assert.Contains("- Name: reviewer", prompt, StringComparison.Ordinal);
        Assert.Contains("patch, shell, and MCP tools are disabled", prompt, StringComparison.Ordinal);
        Assert.Contains("Current task:", prompt, StringComparison.Ordinal);
        Assert.Contains("Review src/App.cs", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Expert_boundary_read_only_disables_write_shell_and_mcp_tools()
    {
        ToolExecutionBoundary boundary = ExpertToolBoundary.FromExpert(
            ExpertProfileCatalog.GetOrNull("security"));

        Assert.False(boundary.AllowMcpDiscovery);
        Assert.True(boundary.AllowsRisk(ToolRiskLevel.Read));
        Assert.False(boundary.AllowsRisk(ToolRiskLevel.Write));
        Assert.False(boundary.AllowsRisk(ToolRiskLevel.Shell));
        Assert.True(boundary.IsDisabledByName("workspace.apply_patch"));
        Assert.True(boundary.IsDisabledByName("workspace.run_shell"));
        Assert.True(boundary.IsDisabledByPrefix("mcp.server.tool"));
    }
}

