using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class AgentVerificationCommandSelectorTests
{
    [Fact]
    public void TrySelect_prefers_explicit_project_instruction_command()
    {
        AgentRunRequest request = CreateRequest(
            instructions: "ValidationCommand: dotnet test",
            workflow: new WorkflowConfiguration(
            [
                new WorkflowProfile(
                    Name: "default",
                    WorkspacePath: null,
                    ValidationCommand: "ctest",
                    Description: "",
                    Source: "workspace config")
            ]));

        bool selected = AgentVerificationCommandSelector.TrySelect(
            request,
            out AgentVerificationCommand? command,
            out string skippedReason);

        Assert.True(selected);
        Assert.Equal("dotnet test", command?.Command);
        Assert.Equal("project-instructions", command?.Source);
        Assert.Equal(string.Empty, skippedReason);
    }

    [Fact]
    public void TrySelect_uses_single_workflow_validation_command_when_instructions_do_not_define_one()
    {
        AgentRunRequest request = CreateRequest(
            instructions: "Use repo style.",
            workflow: new WorkflowConfiguration(
            [
                new WorkflowProfile(
                    Name: "default",
                    WorkspacePath: null,
                    ValidationCommand: "dotnet test",
                    Description: "",
                    Source: "workspace config")
            ]));

        bool selected = AgentVerificationCommandSelector.TrySelect(
            request,
            out AgentVerificationCommand? command,
            out string skippedReason);

        Assert.True(selected);
        Assert.Equal("dotnet test", command?.Command);
        Assert.Equal("workflow:workspace config", command?.Source);
        Assert.Equal("default", command?.ProfileName);
        Assert.Equal(string.Empty, skippedReason);
    }

    [Fact]
    public void TrySelect_skips_when_no_explicit_command_exists()
    {
        AgentRunRequest request = CreateRequest(
            instructions: "Run the usual checks.",
            workflow: WorkflowConfiguration.Empty);

        bool selected = AgentVerificationCommandSelector.TrySelect(
            request,
            out AgentVerificationCommand? command,
            out string skippedReason);

        Assert.False(selected);
        Assert.Null(command);
        Assert.Equal("No explicit verification command configured.", skippedReason);
    }

    [Fact]
    public void TrySelect_skips_ambiguous_workflow_validation_commands()
    {
        AgentRunRequest request = CreateRequest(
            instructions: null,
            workflow: new WorkflowConfiguration(
            [
                new WorkflowProfile("a", null, "dotnet test", "", "workspace config"),
                new WorkflowProfile("b", null, "ctest", "", "workspace config")
            ]));

        bool selected = AgentVerificationCommandSelector.TrySelect(
            request,
            out AgentVerificationCommand? command,
            out string skippedReason);

        Assert.False(selected);
        Assert.Null(command);
        Assert.Contains("Multiple workflow validation commands", skippedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void TrySelect_uses_workspace_matched_workflow_command_to_disambiguate()
    {
        string workspaceRoot = Path.Combine(Path.GetTempPath(), "caicli-selector-tests");
        AgentRunRequest request = CreateRequest(
            instructions: null,
            workflow: new WorkflowConfiguration(
            [
                new WorkflowProfile("generic", null, "dotnet test", "", "workspace config"),
                new WorkflowProfile("matched", workspaceRoot, "ctest", "", "workspace config")
            ]));

        bool selected = AgentVerificationCommandSelector.TrySelect(
            request,
            out AgentVerificationCommand? command,
            out string skippedReason);

        Assert.True(selected);
        Assert.Equal("ctest", command?.Command);
        Assert.Equal("matched", command?.ProfileName);
        Assert.Equal(string.Empty, skippedReason);
    }

    [Fact]
    public void TrySelect_skips_workflow_command_for_different_workspace()
    {
        string otherWorkspace = Path.Combine(Path.GetTempPath(), "caicli-other-workspace");
        AgentRunRequest request = CreateRequest(
            instructions: null,
            workflow: new WorkflowConfiguration(
            [
                new WorkflowProfile("other", otherWorkspace, "dotnet test", "", "user config")
            ]));

        bool selected = AgentVerificationCommandSelector.TrySelect(
            request,
            out AgentVerificationCommand? command,
            out string skippedReason);

        Assert.False(selected);
        Assert.Null(command);
        Assert.Equal("No workflow validation command matched the current workspace.", skippedReason);
    }

    private static AgentRunRequest CreateRequest(
        string? instructions,
        WorkflowConfiguration workflow)
    {
        string workspaceRoot = Path.Combine(Path.GetTempPath(), "caicli-selector-tests");
        WorkspaceContext workspace = new(
            RootPath: workspaceRoot,
            ConfigPath: Path.Combine(workspaceRoot, ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);
        AgentTaskContext taskContext = new(
            CurrentDirectory: workspaceRoot,
            WorkspaceRoot: workspaceRoot,
            WorkspaceStatus: WorkspaceStatus.Ready.ToString(),
            CurrentDirectoryErrorCode: null,
            Instructions: instructions,
            InstructionSources: [],
            InstructionWarnings: [],
            SessionName: null,
            HasTranscriptContext: false,
            Git: new AgentGitContextSummary(
                "working tree clean",
                StatusSucceeded: true,
                StatusErrorCode: null,
                IsDirty: false,
                StatusSummaryTruncated: false,
                "no diff",
                DiffSucceeded: true,
                DiffErrorCode: null,
                DiffOutputTruncated: false,
                DiffSummaryTruncated: false));

        return new AgentRunRequest(
            "patch",
            workspace,
            TaskContext: taskContext,
            WorkflowConfiguration: workflow);
    }
}
