using CSharpAiCli.AppHost.Protocol.Generated;
using CSharpAiCli.Application;

namespace CSharpAiCli.AppHost.Protocol;

/// <summary>Owns the explicitly-created, non-nesting Desktop sub-agent execution set.</summary>
internal sealed class DesktopSubagentSupervisor : IDisposable
{
    private const int MaxAgents = 3;
    private readonly object gate = new();
    private readonly DesktopApplicationSessionFactory sessionFactory;
    private readonly ITurnExecutionRuntime runtime;
    private readonly DesktopGitChangesSupervisor changes;
    private readonly Dictionary<string, AgentExecution> agents = new(StringComparer.Ordinal);

    public DesktopSubagentSupervisor(
        DesktopApplicationSessionFactory sessionFactory,
        ITurnExecutionRuntime runtime,
        DesktopGitChangesSupervisor changes)
    {
        this.sessionFactory = sessionFactory;
        this.runtime = runtime;
        this.changes = changes;
    }

    public void Reset()
    {
        AgentExecution[] prior;
        lock (gate)
        {
            prior = agents.Values.ToArray();
            agents.Clear();
        }
        foreach (AgentExecution agent in prior) agent.Dispose();
    }

    public SubagentResult List(string parentThreadId)
    {
        lock (gate) return Success(parentThreadId);
    }

    public SubagentResult Start(DesktopApplicationSession parent, SubagentStartParams request)
    {
        lock (gate)
        {
            if (!request.Confirmed) return Failure("subagent-confirmation-required", "Creating a Sub-agent requires explicit confirmation.", "validation");
            if (agents.Values.Any(value => value.ThreadId == request.ParentThreadId))
                return Failure("subagent-nesting-denied", "Sub-agents cannot create another Sub-agent.", "denied");
            if (!parent.GetThread(request.ParentThreadId, 0, 1).Succeeded)
                return Failure("subagent-parent-missing", "Parent task was not found.", "not-found");
            int active = agents.Values.Count(value => value.ParentThreadId == request.ParentThreadId &&
                value.Status is ("starting" or "running" or "waiting-approval"));
            if (active >= MaxAgents) return Failure("subagent-limit", "This task already has the maximum of three active Sub-agents.", "conflict");

            string agentId = "agent_" + Guid.NewGuid().ToString("N")[..24];
            string workspacePath = parent.Workspace.RootPath;
            string? worktreeId = null;
            string? branch = null;
            if (request.Mode == "write")
            {
                ChangesGetResult current = changes.Query();
                if (!current.Succeeded || current.Data?.RepositoryId is null || current.Data.Revision is null)
                    return Failure("subagent-worktree-unavailable", "A managed Git Worktree could not be created for this Sub-agent.", "unavailable");
                branch = $"caicli/subagent-{agentId[6..14]}";
                ChangesMutateResult created = changes.Mutate(new ChangesMutateParams
                {
                    SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
                    WorkspaceId = current.Data.WorkspaceId!,
                    RepositoryId = current.Data.RepositoryId,
                    ExpectedRevision = current.Data.Revision,
                    Action = "create-worktree",
                    Path = null,
                    Area = null,
                    HunkId = null,
                    Message = null,
                    SetUpstream = false,
                    Confirmed = true,
                    ClientMutationId = request.ClientMutationId + ".worktree",
                    TargetBranch = branch,
                    ThreadId = request.ParentThreadId
                });
                ManagedWorktreeData? worktree = created.Data?.Changes.Worktrees?.SingleOrDefault(value => value.Branch == branch);
                if (!created.Succeeded || worktree is null)
                    return Failure(created.Error?.Code ?? "subagent-worktree-failed", created.Error?.SafeMessage ?? "Managed Worktree creation failed.", created.Error?.Category ?? "workspace");
                workspacePath = worktree.Path;
                worktreeId = worktree.WorktreeId;
            }

            DesktopApplicationSessionOpenResult opened = sessionFactory.Open(workspacePath);
            if (!opened.Succeeded || opened.Session is null)
                return Failure("subagent-workspace-open-failed", "The Sub-agent workspace could not be opened safely.", "workspace");
            DesktopApplicationSession session = opened.Session;
            ThreadSummaryProjection? thread = session.CreateThread("Sub-agent: " + Bound(request.Prompt, 96)).Data;
            if (thread is null)
            {
                session.Dispose();
                return Failure("subagent-thread-failed", "The Sub-agent task could not be created.", "unavailable");
            }
            ComposerStateProjection? composer = session.GetComposer(thread.ThreadId).Data;
            ComposerStateProjection? queued = composer is null ? null : session.EnqueueComposer(
                thread.ThreadId, composer.ThreadRevision, composer.QueueRevision, request.ClientMutationId + ".enqueue",
                request.Prompt, [], [], approvalPreference: request.Mode == "read-only" ? "read-only" : "on-request").Data;
            TurnExecutionStateProjection? turn = queued is null ? null : session.StartTurn(
                thread.ThreadId, queued.ThreadRevision, queued.QueueRevision, request.ClientMutationId + ".start").Data;
            if (turn is null)
            {
                session.Dispose();
                return Failure("subagent-start-failed", "The Sub-agent turn could not be started.", "unavailable");
            }

            AgentExecution agent = new(agentId, request.ParentThreadId, thread.ThreadId, request.Mode,
                workspacePath, worktreeId, branch, session, DateTimeOffset.UtcNow);
            DesktopWriteExecutionSupervisor supervisor = new(state => OnCommittedAsync(agentId, state), runtime);
            agent.Supervisor = supervisor;
            agents.Add(agentId, agent);
            agent.Status = "running";
            if (!supervisor.TryStart(session, turn))
            {
                agent.Status = "failed";
                agent.ResultSummary = "The execution supervisor was busy.";
            }
            return Success(request.ParentThreadId);
        }
    }

    public SubagentResult Cancel(SubagentMutationParams request, bool takeover)
    {
        lock (gate)
        {
            if (!request.Confirmed) return Failure("subagent-confirmation-required", "This Sub-agent action requires explicit confirmation.", "validation");
            if (!agents.TryGetValue(request.AgentId, out AgentExecution? agent))
                return Failure("subagent-not-found", "Sub-agent was not found.", "not-found");
            if (takeover && agent.Mode != "write") return Failure("subagent-takeover-read-only", "Only a write Sub-agent Worktree can be taken over.", "validation");
            StopPersisted(agent, request.ClientMutationId);
            agent.Status = takeover ? "taken-over" : "canceled";
            agent.UpdatedAtUtc = DateTimeOffset.UtcNow;
            agent.ResultSummary ??= takeover ? "Execution stopped; the managed Worktree is retained for the parent task." : "Execution canceled; evidence and managed Worktree are retained.";
            return Success(agent.ParentThreadId);
        }
    }

    public SubagentResult ResolveApproval(SubagentApprovalResolveParams request)
    {
        lock (gate)
        {
            if (!agents.TryGetValue(request.AgentId, out AgentExecution? agent))
                return Failure("subagent-not-found", "Sub-agent was not found.", "not-found");
            ThreadDetailProjection? detail = agent.Session.GetThread(agent.ThreadId, 0, 1).Data;
            TurnSummaryProjection? turn = detail?.Turns.SingleOrDefault(value => value.TurnId == detail.Thread.ActiveTurnId);
            if (turn?.Approval is null || agent.Supervisor is null)
                return Failure("subagent-approval-stale", "Sub-agent approval is stale.", "conflict");
            DurableApprovalProjection approval = turn.Approval;
            ApplicationResult<TurnExecutionStateProjection> result = agent.Supervisor.ResolveApproval(agent.Session,
                agent.ThreadId, turn.TurnId, approval.RequestId, request.Decision,
                detail!.Thread.Revision, turn.Revision, approval.ApprovalRevision, request.ClientMutationId);
            if (!result.Succeeded) return Failure(result.Error?.Code ?? "subagent-approval-stale", result.Error?.SafeMessage ?? "Sub-agent approval is stale.", "conflict");
            agent.Status = "running";
            agent.UpdatedAtUtc = DateTimeOffset.UtcNow;
            return Success(agent.ParentThreadId);
        }
    }

    public void Dispose() => Reset();

    private void StopPersisted(AgentExecution agent, string mutationId)
    {
        ThreadDetailProjection? detail = agent.Session.GetThread(agent.ThreadId, 0, 1).Data;
        TurnSummaryProjection? turn = detail?.Turns.SingleOrDefault(value => value.TurnId == detail.Thread.ActiveTurnId);
        if (turn is not null && agent.Supervisor is not null)
        {
            agent.Supervisor.Cancel(agent.Session, agent.ThreadId, turn.TurnId,
                detail!.Thread.Revision, turn.Revision, mutationId);
        }
        agent.Supervisor?.Stop();
    }

    private ValueTask OnCommittedAsync(string agentId, TurnExecutionStateProjection state)
    {
        lock (gate)
        {
            if (!agents.TryGetValue(agentId, out AgentExecution? agent)) return ValueTask.CompletedTask;
            agent.Status = state.Approval is not null ? "waiting-approval" : state.Status switch
            {
                "completed" => "completed",
                "failed" => "failed",
                "canceled" or "cancelled" => "canceled",
                _ => "running"
            };
            agent.Approval = state.Approval;
            agent.UpdatedAtUtc = DateTimeOffset.UtcNow;
            if (agent.Status is "completed" or "failed" or "canceled")
            {
                ThreadDetailProjection? detail = agent.Session.GetThread(agent.ThreadId, 0, 100).Data;
                agent.ResultSummary = detail?.Timeline.LastOrDefault(item => item.Type is "assistant.final" or "warning.raised" or "turn.completed")?.Summary;
            }
        }
        return ValueTask.CompletedTask;
    }

    private SubagentResult Success(string parentThreadId) => new()
    {
        SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
        Succeeded = true,
        Data = new SubagentListData
        {
            ParentThreadId = parentThreadId,
            MaxAgents = MaxAgents,
            Agents = agents.Values.Where(value => value.ParentThreadId == parentThreadId).OrderBy(value => value.CreatedAtUtc).Select(Map).ToArray()
        },
        Error = null,
        Diagnostics = [],
        Truncated = false
    };

    private static SubagentResult Failure(string code, string safeMessage, string category) => new()
    {
        SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
        Succeeded = false,
        Data = null,
        Error = new ApplicationErrorData { Code = code, Category = category, SafeMessage = safeMessage, Retryable = false },
        Diagnostics = [],
        Truncated = false
    };

    private static SubagentData Map(AgentExecution value) => new()
    {
        AgentId = value.AgentId,
        ParentThreadId = value.ParentThreadId,
        ThreadId = value.ThreadId,
        Mode = value.Mode,
        Status = value.Status,
        WorkspacePath = value.WorkspacePath,
        WorktreeId = value.WorktreeId,
        Branch = value.Branch,
        ResultSummary = value.ResultSummary,
        Approval = value.Approval is null ? null : DesktopProtocolMapper.MapApproval(value.Approval),
        CreatedAtUtc = value.CreatedAtUtc,
        UpdatedAtUtc = value.UpdatedAtUtc
    };

    private static string Bound(string value, int length)
    {
        string text = value.Trim().ReplaceLineEndings(" ");
        return text.Length > length ? text[..length] : text;
    }

    private sealed class AgentExecution : IDisposable
    {
        public AgentExecution(string agentId, string parentThreadId, string threadId, string mode, string workspacePath,
            string? worktreeId, string? branch, DesktopApplicationSession session, DateTimeOffset createdAtUtc)
        {
            AgentId = agentId; ParentThreadId = parentThreadId; ThreadId = threadId; Mode = mode; WorkspacePath = workspacePath;
            WorktreeId = worktreeId; Branch = branch; Session = session; CreatedAtUtc = createdAtUtc; UpdatedAtUtc = createdAtUtc;
        }
        public string AgentId { get; }
        public string ParentThreadId { get; }
        public string ThreadId { get; }
        public string Mode { get; }
        public string WorkspacePath { get; }
        public string? WorktreeId { get; }
        public string? Branch { get; }
        public DesktopApplicationSession Session { get; }
        public DesktopWriteExecutionSupervisor? Supervisor { get; set; }
        public string Status { get; set; } = "starting";
        public string? ResultSummary { get; set; }
        public DurableApprovalProjection? Approval { get; set; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public void Dispose() { Supervisor?.Dispose(); Session.Dispose(); }
    }
}
