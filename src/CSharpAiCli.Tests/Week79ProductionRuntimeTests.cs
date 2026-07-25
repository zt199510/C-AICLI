using CSharpAiCli.AppHost.Protocol;
using CSharpAiCli.Application;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class Week79ProductionRuntimeTests
{
    [Fact]
    public void Default_apphost_composition_uses_production_runtime()
    {
        using DesktopRpcServer server = new();

        Assert.IsType<DesktopAgentTurnExecutionRuntime>(server.TurnExecutionRuntime);
        Assert.IsNotType<DeterministicFakeTurnExecutionRuntime>(server.TurnExecutionRuntime);
    }

    [Fact]
    public void Fake_runtime_requires_explicit_injection()
    {
        DeterministicFakeTurnExecutionRuntime fake = new();
        using DesktopRpcServer server = new(new DesktopApplicationSessionFactory(), fake);

        Assert.Same(fake, server.TurnExecutionRuntime);
    }

    [Fact]
    public void Shared_registry_preserves_builtin_tools_and_disables_mcp()
    {
        using TestWorkspace workspace = new();
        CliEnvironmentSnapshot snapshot = workspace.CreateSnapshot();

        ToolRegistry registry = BuiltInToolRegistryFactory.Create(
            snapshot,
            new DefaultDenyApprovalPolicy(),
            new ToolExecutionBoundary(AllowMcpDiscovery: false));

        Assert.Equal(
            [
                "agent.plan",
                "workspace.read_text",
                "workspace.search_text",
                "workspace.apply_patch",
                "workspace.run_shell",
                "git.status",
                "git.diff"
            ],
            registry.List().Select(tool => tool.Name));
        Assert.DoesNotContain(registry.List(), tool => tool.Name.StartsWith("mcp.", StringComparison.Ordinal));
    }

    [Fact]
    public void Agent_factory_rejects_workspace_key_without_creating_gateway()
    {
        using TestWorkspace workspace = new();
        CliEnvironmentSnapshot snapshot = workspace.CreateSnapshot(
            model: "gpt-test",
            apiKey: "sk-workspace-value",
            apiKeySource: "workspace config");
        ToolRegistry registry = new();
        ToolExecutor executor = new(registry);
        bool gatewayCreated = false;
        OpenAiAgentRunnerFactory factory = new((_, _) =>
        {
            gatewayCreated = true;
            throw new InvalidOperationException("Gateway must not be created.");
        });

        AgentRunResult result = factory.Create(snapshot, registry, executor).Run(
            new AgentRunRequest("Inspect the workspace.", snapshot.Workspace));

        Assert.False(result.IsSuccess);
        Assert.Equal("unsupported-api-key-source", result.Error?.LocalErrorCode);
        Assert.False(gatewayCreated);
    }

    [Fact]
    public async Task Missing_model_fails_closed_without_fake_success_timeline()
    {
        using TestWorkspace workspace = new();
        CliEnvironmentSnapshot snapshot = workspace.CreateSnapshot();
        DesktopAgentTurnExecutionRuntime runtime = new();
        CollectingSink sink = new();
        TurnExecutionInput input = new(
            snapshot,
            "workspace_test",
            snapshot.Workspace.RootPath,
            "thread_0123456789abcdef01234567",
            "turn_0123456789abcdef0123456789abcdef",
            new string('a', 64),
            new PendingComposerIntentRecord
            {
                IntentId = "intent_test",
                WorkspaceId = "workspace_test",
                WorkspaceRootIdentity = snapshot.Workspace.RootPath,
                ThreadId = "thread_0123456789abcdef01234567",
                Prompt = "Inspect the workspace.",
                EffectiveModel = snapshot.Configuration.Model,
                ModelSource = snapshot.Configuration.ModelSource,
                ApprovalMode = snapshot.Configuration.ApprovalMode.ToString(),
                ApprovalModeSource = snapshot.Configuration.ApprovalModeSource,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });

        TurnRuntimeResult result = await runtime.ExecuteAsync(
            input,
            sink,
            new RejectingApprovalGateway(),
            CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Equal("missing-model", result.ErrorCode);
        TurnRuntimeEvent runtimeEvent = Assert.Single(sink.Events);
        Assert.Equal(TurnRuntimeEventKind.Warning, runtimeEvent.Kind);
        Assert.DoesNotContain(sink.Events, item => item.Kind == TurnRuntimeEventKind.Final);
        Assert.DoesNotContain(sink.Events, item => item.Summary.Contains(
            "deterministic",
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Runtime_commits_tool_events_before_followup_model_call_finishes()
    {
        using TestWorkspace workspace = new();
        CliEnvironmentSnapshot snapshot = workspace.CreateSnapshot(
            model: "gpt-test",
            apiKey: "sk-test-value",
            apiKeySource: "OPENAI_API_KEY");
        BlockingFollowupGateway gateway = new();
        DesktopAgentTurnExecutionRuntime runtime = new(
            new OpenAiAgentRunnerFactory((_, _) => gateway));
        CollectingSink sink = new();
        TurnExecutionInput input = CreateInput(snapshot);

        Task<TurnRuntimeResult> execution = runtime.ExecuteAsync(
            input,
            sink,
            new RejectingApprovalGateway(),
            CancellationToken.None);

        await gateway.FollowupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await sink.ToolCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(execution.IsCompleted);
        Assert.Contains(sink.Events, item => item.Kind == TurnRuntimeEventKind.ToolStarted);
        Assert.Contains(sink.Events, item => item.Kind == TurnRuntimeEventKind.ToolCompleted);

        gateway.ReleaseFollowup.TrySetResult();
        TurnRuntimeResult result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("completed", result.Status);
        Assert.Equal(
            Enumerable.Range(1, sink.Events.Count).Select(value => (long)value),
            sink.Events.Select(item => item.EventSequence));
        Assert.Equal(TurnRuntimeEventKind.Final, sink.Events[^1].Kind);
    }

    [Fact]
    public async Task Runtime_commits_tool_started_before_requesting_write_approval()
    {
        using TestWorkspace workspace = new();
        File.WriteAllText(Path.Combine(workspace.Root, "target.txt"), "before");
        CliEnvironmentSnapshot snapshot = workspace.CreateSnapshot(
            model: "gpt-test",
            apiKey: "sk-test-value",
            apiKeySource: "OPENAI_API_KEY");
        PatchThenFinalGateway gateway = new();
        DesktopAgentTurnExecutionRuntime runtime = new(
            new OpenAiAgentRunnerFactory((_, _) => gateway));
        BlockingToolStartedSink sink = new();
        CapturingApprovalGateway approval = new("approve");

        Task<TurnRuntimeResult> execution = runtime.ExecuteAsync(
            CreateInput(snapshot),
            sink,
            approval,
            CancellationToken.None);

        await sink.ToolStartedEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(approval.Actions);

        sink.ReleaseToolStarted.TrySetResult();
        TurnRuntimeResult result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("completed", result.Status);
        Assert.Single(approval.Actions);
        Assert.Equal("after", File.ReadAllText(Path.Combine(workspace.Root, "target.txt")));
    }

    [Fact]
    public async Task Runtime_honors_workspace_agent_turn_limit()
    {
        using TestWorkspace workspace = new();
        CliEnvironmentSnapshot snapshot = workspace.CreateSnapshot(
            model: "gpt-test",
            apiKey: "sk-test-value",
            apiKeySource: "OPENAI_API_KEY",
            agentRunLimits: new AgentRunLimits(
                MaxTurns: 1,
                MaxToolCalls: 4,
                MaxRetries: 0,
                ModelCallTimeout: TimeSpan.FromSeconds(5),
                OverallTimeout: TimeSpan.FromSeconds(5)));
        RepeatingPlanGateway gateway = new();
        DesktopAgentTurnExecutionRuntime runtime = new(
            new OpenAiAgentRunnerFactory((_, _) => gateway));

        TurnRuntimeResult result = await runtime.ExecuteAsync(
            CreateInput(snapshot),
            new CollectingSink(),
            new RejectingApprovalGateway(),
            CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Equal("agent-loop-limit-reached", result.ErrorCode);
        Assert.Equal(2, gateway.Calls);
    }

    [Fact]
    public void Desktop_approval_policy_is_per_action_stable_and_redacted()
    {
        using TestWorkspace workspace = new();
        CliEnvironmentSnapshot snapshot = workspace.CreateSnapshot();
        CapturingApprovalGateway gateway = new("approve");
        DesktopApprovalPolicy policy = new(
            CreateInput(snapshot),
            gateway,
            CancellationToken.None);
        ApprovalRequest request = new(
            "workspace.apply_patch",
            $"Patch {workspace.Root} with apiKey=sk-sensitive-value",
            "--- old secret\n+++ new secret",
            IsDirtyWorkspace: true,
            Metadata: new Dictionary<string, string>
            {
                ["path"] = "src/Example.cs",
                ["reason"] = "Controlled edit."
            },
            RiskLevel: ToolRiskLevel.Write);

        ApprovalDecision first = policy.RequestApproval(request);
        ApprovalDecision second = policy.RequestApproval(request);
        policy.RequestApproval(request with
        {
            Metadata = new Dictionary<string, string>
            {
                ["path"] = "src/Other.cs",
                ["reason"] = "Controlled edit."
            }
        });

        Assert.True(first.Approved);
        Assert.True(second.Approved);
        Assert.Equal(3, gateway.Actions.Count);
        Assert.Equal(
            gateway.Actions[0].CanonicalActionSha256,
            gateway.Actions[1].CanonicalActionSha256);
        Assert.NotEqual(
            gateway.Actions[0].CanonicalActionSha256,
            gateway.Actions[2].CanonicalActionSha256);
        Assert.All(gateway.Actions, action =>
        {
            Assert.DoesNotContain(workspace.Root, action.SafeSummary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sk-sensitive", action.SafeSummary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", action.SafeSummary, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("workspace-file", action.TargetClass);
            Assert.Matches("^[0-9a-f]{64}$", action.CanonicalActionSha256);
        });
    }

    [Fact]
    public void Desktop_approval_policy_never_prompts_for_read_or_dangerous_shell()
    {
        using TestWorkspace workspace = new();
        CapturingApprovalGateway gateway = new("approve");
        DesktopApprovalPolicy policy = new(
            CreateInput(workspace.CreateSnapshot()),
            gateway,
            CancellationToken.None);

        ApprovalDecision read = policy.RequestApproval(new ApprovalRequest(
            "workspace.read_text",
            "Read a file.",
            null,
            false,
            RiskLevel: ToolRiskLevel.Read));
        ApprovalDecision dangerous = policy.RequestApproval(new ApprovalRequest(
            "workspace.run_shell",
            "Run a dangerous command.",
            null,
            false,
            RiskLevel: ToolRiskLevel.DangerousShell));

        Assert.True(read.Approved);
        Assert.Equal("not-required", read.Status);
        Assert.False(dangerous.Approved);
        Assert.Equal("dangerous-shell-denied", dangerous.Status);
        Assert.Empty(gateway.Actions);
    }

    [Fact]
    public async Task Desktop_approval_waiter_rotates_identity_after_each_resolved_action()
    {
        DesktopWriteExecutionSupervisor.ApprovalWaiter waiter = new();
        DurableApprovalProjection first = Approval("approval_first", 1);
        DurableApprovalProjection second = Approval("approval_second", 2);

        ValueTask<InteractiveApprovalDecision> firstPending =
            waiter.WaitAsync(first, CancellationToken.None);
        Assert.True(waiter.Matches(first.RequestId));
        Assert.True(waiter.TryResolve(new InteractiveApprovalDecision(
            "approve", "decision-first", first.TurnRevision, first.ApprovalRevision)));
        Assert.Equal("decision-first", (await firstPending).ClientMutationId);

        ValueTask<InteractiveApprovalDecision> secondPending =
            waiter.WaitAsync(second, CancellationToken.None);
        Assert.False(waiter.Matches(first.RequestId));
        Assert.True(waiter.Matches(second.RequestId));
        Assert.True(waiter.TryResolve(new InteractiveApprovalDecision(
            "approve", "decision-second", second.TurnRevision, second.ApprovalRevision)));
        Assert.Equal("decision-second", (await secondPending).ClientMutationId);
    }

    private static DurableApprovalProjection Approval(string requestId, long approvalRevision) => new(
        requestId,
        "workspace_test",
        "thread_0123456789abcdef01234567",
        "turn_0123456789abcdef0123456789abcdef",
        approvalRevision,
        approvalRevision,
        "desktop-durable-approval",
        new string('a', 64),
        "write",
        "workspace.apply_patch",
        "workspace-file",
        "operation=write; target=workspace-file",
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddMinutes(30));

    private static TurnExecutionInput CreateInput(CliEnvironmentSnapshot snapshot) => new(
        snapshot,
        "workspace_test",
        snapshot.Workspace.RootPath,
        "thread_0123456789abcdef01234567",
        "turn_0123456789abcdef0123456789abcdef",
        new string('a', 64),
        new PendingComposerIntentRecord
        {
            IntentId = "intent_test",
            WorkspaceId = "workspace_test",
            WorkspaceRootIdentity = snapshot.Workspace.RootPath,
            ThreadId = "thread_0123456789abcdef01234567",
            Prompt = "Inspect the workspace.",
            EffectiveModel = snapshot.Configuration.Model,
            ModelSource = snapshot.Configuration.ModelSource,
            ApprovalMode = snapshot.Configuration.ApprovalMode.ToString(),
            ApprovalModeSource = snapshot.Configuration.ApprovalModeSource,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

    private sealed class CollectingSink : ITurnExecutionEventSink
    {
        public List<TurnRuntimeEvent> Events { get; } = [];
        public TaskCompletionSource ToolCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask EmitAsync(TurnRuntimeEvent runtimeEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add(runtimeEvent);
            if (runtimeEvent.Kind == TurnRuntimeEventKind.ToolCompleted)
            {
                ToolCompleted.TrySetResult();
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RejectingApprovalGateway : IInteractiveApprovalGateway
    {
        public ValueTask<InteractiveApprovalDecision> RequestAsync(
            InteractiveApprovalAction action,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Approval was not expected.");
    }

    private sealed class CapturingApprovalGateway(string decision) : IInteractiveApprovalGateway
    {
        public List<InteractiveApprovalAction> Actions { get; } = [];

        public ValueTask<InteractiveApprovalDecision> RequestAsync(
            InteractiveApprovalAction action,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Actions.Add(action);
            return ValueTask.FromResult(new InteractiveApprovalDecision(
                decision,
                "approval-decision-test",
                1,
                1));
        }
    }

    private sealed class BlockingFollowupGateway : IOpenAiResponsesGateway
    {
        private int calls;

        public TaskCompletionSource FollowupEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFollowup { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public OpenAiResponseEnvelope CreateAgentResponse(
            OpenAiAgentRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                return new OpenAiResponseEnvelope(
                    "response-tool",
                    "gpt-test",
                    string.Empty,
                    [new OpenAiToolCall(
                        "call-plan",
                        "agent.plan",
                        """{"goal":"Inspect the workspace."}""")]);
            }

            FollowupEntered.TrySetResult();
            ReleaseFollowup.Task.Wait(cancellationToken);
            return new OpenAiResponseEnvelope(
                "response-final",
                "gpt-test",
                "Inspection completed.");
        }

        public OpenAiResponseEnvelope CreateResponse(
            string model,
            string prompt,
            string? instructions = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IEnumerable<OpenAiStreamingResponseUpdate> CreateResponseStreaming(
            string model,
            string prompt,
            string? instructions = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RepeatingPlanGateway : IOpenAiResponsesGateway
    {
        public int Calls { get; private set; }

        public OpenAiResponseEnvelope CreateAgentResponse(
            OpenAiAgentRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return new OpenAiResponseEnvelope(
                $"response-{Calls}",
                "gpt-test",
                string.Empty,
                [new OpenAiToolCall(
                    $"call-{Calls}",
                    "agent.plan",
                    """{"goal":"Inspect the workspace."}""")]);
        }

        public OpenAiResponseEnvelope CreateResponse(
            string model,
            string prompt,
            string? instructions = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IEnumerable<OpenAiStreamingResponseUpdate> CreateResponseStreaming(
            string model,
            string prompt,
            string? instructions = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class PatchThenFinalGateway : IOpenAiResponsesGateway
    {
        private int calls;

        public OpenAiResponseEnvelope CreateAgentResponse(
            OpenAiAgentRequest request,
            CancellationToken cancellationToken = default)
        {
            calls++;
            return calls == 1
                ? new OpenAiResponseEnvelope(
                    "response-patch",
                    "gpt-test",
                    string.Empty,
                    [new OpenAiToolCall(
                        "call-patch",
                        "workspace.apply_patch",
                        """{"path":"target.txt","find":"before","replace":"after"}""")])
                : new OpenAiResponseEnvelope(
                    "response-final",
                    "gpt-test",
                    "Patch completed.",
                    []);
        }

        public OpenAiResponseEnvelope CreateResponse(
            string model,
            string prompt,
            string? instructions = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IEnumerable<OpenAiStreamingResponseUpdate> CreateResponseStreaming(
            string model,
            string prompt,
            string? instructions = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class BlockingToolStartedSink : ITurnExecutionEventSink
    {
        public TaskCompletionSource ToolStartedEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseToolStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask EmitAsync(
            TurnRuntimeEvent runtimeEvent,
            CancellationToken cancellationToken)
        {
            if (runtimeEvent.Kind != TurnRuntimeEventKind.ToolStarted)
            {
                return;
            }

            ToolStartedEntered.TrySetResult();
            await ReleaseToolStarted.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "caicli-week79-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public CliEnvironmentSnapshot CreateSnapshot(
            string model = "not configured",
            string? apiKey = null,
            string apiKeySource = "missing",
            AgentRunLimits? agentRunLimits = null)
        {
            WorkspaceContext workspace = WorkspaceContext.Detect(Root, Root);
            EffectiveConfiguration configuration = new(
                WorkspaceRoot: Root,
                UserConfigPath: Path.Combine(Root, "profile", "config.json"),
                WorkspaceConfigPath: workspace.ConfigPath,
                Model: model,
                ModelSource: model == "not configured" ? "default" : "test",
                AgentBackend: "direct",
                AgentBackendSource: "default",
                DisabledTools: new HashSet<string>(StringComparer.Ordinal),
                ApiKey: SecretValue.From(apiKey),
                ApiKeySource: apiKeySource,
                LoadedConfigPaths: [],
                Warnings: [],
                ConfigSources: [])
            {
                AgentRunLimits = agentRunLimits ?? AgentRunLimits.Default
            };
            return new CliEnvironmentSnapshot(
                workspace,
                configuration,
                "9.0.308",
                ".NET 9.0.0",
                "net9.0",
                false);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
            }
        }
    }
}
