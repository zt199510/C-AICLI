using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class WorkspacePatchToolTests
{
    [Fact]
    public void Execute_applies_patch_when_approved()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = Path.Combine(temp.Path, "notes.txt");
        File.WriteAllText(filePath, "hello world");
        WorkspacePatchTool tool = CreateTool(new AlwaysApproveApprovalPolicy());

        ToolExecutionResult result = tool.Execute(CreateContext(
            temp.Path,
            """{"path":"notes.txt","find":"hello","replace":"hi"}"""));

        Assert.True(result.Succeeded);
        Assert.Equal("approved", result.ApprovalStatus);
        Assert.Equal("hi world", File.ReadAllText(filePath));
    }

    [Fact]
    public void Execute_refuses_patch_when_approval_denied()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = Path.Combine(temp.Path, "notes.txt");
        File.WriteAllText(filePath, "hello world");
        WorkspacePatchTool tool = CreateTool(new DefaultDenyApprovalPolicy());

        ToolExecutionResult result = tool.Execute(CreateContext(
            temp.Path,
            """{"path":"notes.txt","find":"hello","replace":"hi"}"""));

        Assert.False(result.Succeeded);
        Assert.Equal("approval-denied", result.ErrorCode);
        Assert.Equal("denied", result.ApprovalStatus);
        Assert.Equal("hello world", File.ReadAllText(filePath));
    }

    [Fact]
    public void Execute_requests_approval_with_patch_risk_metadata_and_preview_context()
    {
        using TempDirectory temp = TempDirectory.Create();
        string filePath = Path.Combine(temp.Path, "notes.txt");
        File.WriteAllText(filePath, "hello world");
        RecordingApprovalPolicy approvalPolicy = new(ApprovalDecision.Deny("Denied by recording policy."));
        WorkspacePatchTool tool = new(
            new SingleFilePatchApplier(
                new WorkspaceGuard(),
                new StaticDirtyWorkspaceDetector(new DirtyWorkspaceStatus(true, "2 changed path(s)"))),
            approvalPolicy);

        ToolExecutionResult result = tool.Execute(CreateContext(
            temp.Path,
            """{"path":"notes.txt","find":"hello","replace":"hi"}"""));

        Assert.False(result.Succeeded);
        ApprovalRequest request = approvalPolicy.SingleRequest;
        Assert.Equal("workspace.apply_patch", request.Operation);
        Assert.Equal(ToolRiskLevel.Write, request.RiskLevel);
        Assert.Contains("Replace 1 occurrence(s) in notes.txt", request.Summary, StringComparison.Ordinal);
        Assert.Contains("2 changed path(s)", request.Summary, StringComparison.Ordinal);
        Assert.Contains("--- a/notes.txt", request.Diff, StringComparison.Ordinal);
        Assert.True(request.IsDirtyWorkspace);
        Assert.NotNull(request.Metadata);
        IReadOnlyDictionary<string, string> metadata = request.Metadata;
        Assert.Equal("notes.txt", metadata["path"]);
        Assert.Equal("Patch application modifies workspace files and requires approval.", metadata["reason"]);
    }

    [Fact]
    public void Execute_returns_argument_failure_for_missing_fields()
    {
        using TempDirectory temp = TempDirectory.Create();
        WorkspacePatchTool tool = CreateTool(new AlwaysApproveApprovalPolicy());

        ToolExecutionResult result = tool.Execute(CreateContext(temp.Path, """{"path":"notes.txt"}"""));

        Assert.False(result.Succeeded);
        Assert.Equal("invalid-tool-arguments", result.ErrorCode);
    }

    [Fact]
    public void Execute_failure_is_recorded_in_tool_executor_without_crashing()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "notes.txt"), "hello world");
        ToolRegistry registry = new();
        registry.Register(CreateTool(new AlwaysApproveApprovalPolicy()));
        ToolExecutor executor = new(registry);

        ToolExecutionResult result = executor.Execute(
            "workspace.apply_patch",
            CreateContext(temp.Path, """{"path":"notes.txt","find":"missing","replace":"hi"}"""));

        Assert.False(result.Succeeded);
        Assert.Equal("patch-context-not-found", result.ErrorCode);
    }

    [Fact]
    public void Execute_apply_failure_after_approval_reports_approval_status()
    {
        using TempDirectory temp = TempDirectory.Create();
        WorkspacePatchTool tool = new(
            new FailingApplyPatchApplier(),
            new AlwaysApproveApprovalPolicy());

        ToolExecutionResult result = tool.Execute(CreateContext(
            temp.Path,
            """{"path":"notes.txt","find":"before","replace":"after"}"""));

        Assert.False(result.Succeeded);
        Assert.Equal("patch-apply-failed", result.ErrorCode);
        Assert.Equal("approved", result.ApprovalStatus);
    }

    [Fact]
    public void Patch_tool_records_approval_status_in_offline_agent_transcript()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "notes.txt"), "hello world");
        ToolRegistry registry = new();
        registry.Register(CreateTool(new AlwaysApproveApprovalPolicy()));
        ToolExecutor executor = new(registry);
        OfflineAgentRunner runner = new(
            new PatchToolCallingModel(),
            executor,
            () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"));
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));

        AgentRunResult result = runner.Run(new AgentRunRequest(
            "patch file",
            WorkspaceContext.Detect(temp.Path, temp.Path)), transcript);

        Assert.True(result.IsSuccess);
        ConversationToolCall toolCall = Assert.Single(transcript.ToolCalls);
        Assert.Equal("workspace.apply_patch", toolCall.ToolName);
        Assert.Equal("approved", toolCall.ApprovalStatus);
        Assert.True(toolCall.Succeeded);
        Assert.Equal("hi world", File.ReadAllText(Path.Combine(temp.Path, "notes.txt")));
    }

    private static WorkspacePatchTool CreateTool(IApprovalPolicy approvalPolicy)
    {
        return new WorkspacePatchTool(
            new SingleFilePatchApplier(
                new WorkspaceGuard(),
                new StaticDirtyWorkspaceDetector(new DirtyWorkspaceStatus(false, "clean"))),
            approvalPolicy);
    }

    private static ToolExecutionContext CreateContext(string workspaceRoot, string argumentsJson)
    {
        return new ToolExecutionContext(
            "call_patch",
            WorkspaceContext.Detect(workspaceRoot, workspaceRoot),
            argumentsJson);
    }

    private sealed class StaticDirtyWorkspaceDetector(DirtyWorkspaceStatus status) : IDirtyWorkspaceDetector
    {
        public DirtyWorkspaceStatus Detect(WorkspaceContext workspace) => status;
    }

    private sealed class RecordingApprovalPolicy(ApprovalDecision decision) : IApprovalPolicy
    {
        private readonly List<ApprovalRequest> requests = [];

        public ApprovalRequest SingleRequest => Assert.Single(requests);

        public ApprovalDecision RequestApproval(ApprovalRequest request)
        {
            requests.Add(request);
            return decision;
        }
    }

    private sealed class FailingApplyPatchApplier : IPatchApplier
    {
        public PatchPreview Preview(WorkspaceContext workspace, PatchOperation operation)
        {
            return new PatchPreview(
                operation,
                Path.Combine(workspace.RootPath, operation.Path),
                "hash",
                1,
                "Replace 1 occurrence(s) in notes.txt.",
                "--- a/notes.txt",
                new DirtyWorkspaceStatus(false, "clean"));
        }

        public PatchApplyResult Apply(WorkspaceContext workspace, PatchPreview preview)
        {
            return PatchApplyResult.Failure(
                "patch-apply-failed",
                "Patch could not be applied.");
        }
    }

    private sealed class PatchToolCallingModel : IToolCallingModel
    {
        public AgentModelTurn Start(AgentRunRequest request, CancellationToken cancellationToken = default)
        {
            return AgentModelTurn.RequestTools(new AgentToolCallRequest(
                "call_patch",
                "workspace.apply_patch",
                """{"path":"notes.txt","find":"hello","replace":"hi"}"""));
        }

        public AgentModelTurn Continue(
            AgentRunRequest request,
            IReadOnlyList<AgentToolCallResult> toolResults,
            CancellationToken cancellationToken = default)
        {
            Assert.True(Assert.Single(toolResults).Result.Succeeded);
            return AgentModelTurn.Final("done");
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "caicli-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
