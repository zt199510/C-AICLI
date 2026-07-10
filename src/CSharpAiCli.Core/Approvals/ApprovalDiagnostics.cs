using System.Diagnostics;

namespace CSharpAiCli.Core;

internal readonly record struct ApprovalDiagnosticResult(
    ApprovalDecision Decision,
    long DurationMs);

internal static class ApprovalDiagnostics
{
    public static ApprovalDiagnosticResult Request(
        IApprovalPolicy approvalPolicy,
        ApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(approvalPolicy);
        ArgumentNullException.ThrowIfNull(request);

        Stopwatch stopwatch = Stopwatch.StartNew();
        ApprovalDecision decision = approvalPolicy.RequestApproval(request);
        stopwatch.Stop();

        return new ApprovalDiagnosticResult(
            decision,
            Math.Max(0, stopwatch.ElapsedMilliseconds));
    }
}
