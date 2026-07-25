using System.Security.Cryptography;
using System.Text;
using CSharpAiCli.Application;
using CSharpAiCli.Core;

namespace CSharpAiCli.AppHost.Protocol;

internal sealed class DesktopApprovalPolicy(
    TurnExecutionInput input,
    IInteractiveApprovalGateway gateway,
    CancellationToken cancellationToken) : IApprovalPolicy
{
    private const string PolicyIdentity = "desktop-durable-approval";
    private const string PolicyVersion = "v1";

    public ApprovalDecision RequestApproval(ApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.RiskLevel switch
        {
            ToolRiskLevel.Read => new ApprovalDecision(
                Approved: true,
                Status: "not-required",
                SafeMessage: "Approval is not required for read-only operations."),
            ToolRiskLevel.DangerousShell => new ApprovalDecision(
                Approved: false,
                Status: "dangerous-shell-denied",
                SafeMessage: "Dangerous shell commands are denied by Desktop policy."),
            ToolRiskLevel.Write or ToolRiskLevel.Shell => RequestDurableDecision(request),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request),
                request.RiskLevel,
                "Unknown tool risk level.")
        };
    }

    private ApprovalDecision RequestDurableDecision(ApprovalRequest request)
    {
        string risk = request.RiskLevel == ToolRiskLevel.Write ? "write" : "shell";
        string targetClass = request.Metadata?.ContainsKey("path") == true
            ? "workspace-file"
            : request.Metadata?.ContainsKey("cwd") == true
                ? "workspace-directory"
                : "workspace";
        string actionHash = ComputeCanonicalActionHash(request);
        string policyRevision = Hash(
            PolicyIdentity,
            PolicyVersion,
            input.CanonicalInputSha256,
            input.WorkspaceId);
        string safeSummary = string.Join(
            "; ",
            $"operation={risk}",
            $"target={targetClass}",
            $"dirty={request.IsDirtyWorkspace.ToString().ToLowerInvariant()}",
            $"risk={risk}",
            $"action={actionHash}");
        InteractiveApprovalDecision decision = gateway.RequestAsync(
                new InteractiveApprovalAction(
                    PolicyIdentity,
                    policyRevision,
                    risk,
                    request.Operation,
                    targetClass,
                    actionHash,
                    safeSummary),
                cancellationToken)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        return decision.Decision switch
        {
            "approve" => ApprovalDecision.Approve("Approved by durable Desktop action decision."),
            "deny" => ApprovalDecision.Deny("Denied by durable Desktop action decision."),
            _ => ApprovalDecision.Deny("Desktop approval decision was invalid.")
        };
    }

    private string ComputeCanonicalActionHash(ApprovalRequest request)
    {
        List<string> parts =
        [
            PolicyIdentity,
            PolicyVersion,
            input.WorkspaceId,
            input.ThreadId,
            input.TurnId,
            input.CanonicalInputSha256,
            request.Operation,
            request.RiskLevel.ToString(),
            request.IsDirtyWorkspace ? "dirty" : "clean",
            Hash(request.Diff ?? string.Empty)
        ];
        if (request.Metadata is not null)
        {
            foreach (KeyValuePair<string, string> item in request.Metadata.OrderBy(
                item => item.Key,
                StringComparer.Ordinal))
            {
                parts.Add(item.Key);
                parts.Add(item.Value);
            }
        }
        return Hash(parts.ToArray());
    }

    private static string Hash(params string[] values)
    {
        StringBuilder canonical = new();
        foreach (string value in values)
        {
            canonical.Append(value.Length.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            canonical.Append(':');
            canonical.Append(value);
            canonical.Append('\n');
        }
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }
}
