using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class WorkspacePatchTool : ITool
{
    private readonly IPatchApplier patchApplier;
    private readonly IApprovalPolicy approvalPolicy;

    public WorkspacePatchTool(IPatchApplier patchApplier, IApprovalPolicy approvalPolicy)
    {
        ArgumentNullException.ThrowIfNull(patchApplier);
        ArgumentNullException.ThrowIfNull(approvalPolicy);

        this.patchApplier = patchApplier;
        this.approvalPolicy = approvalPolicy;
    }

    public ToolDefinition Definition { get; } = new(
        "workspace.apply_patch",
        "Preview and apply a single-file exact-text replacement patch in the current workspace.",
        """{"type":"object","properties":{"path":{"type":"string"},"find":{"type":"string"},"replace":{"type":"string"}},"required":["path","find","replace"]}""",
        ToolRiskLevel.Write);

    public ToolExecutionResult Execute(
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!TryReadOperation(context.ArgumentsJson, out PatchOperation operation, out ToolExecutionResult? failure))
        {
            return failure;
        }

        PatchPreview preview = patchApplier.Preview(context.Workspace, operation);
        ApprovalDecision approval = approvalPolicy.RequestApproval(new ApprovalRequest(
            Operation: Definition.Name,
            Summary: preview.Summary,
            Diff: preview.Diff,
            IsDirtyWorkspace: preview.DirtyWorkspace.IsDirty,
            Metadata: new Dictionary<string, string>
            {
                ["path"] = operation.Path,
                ["reason"] = "Patch application modifies workspace files and requires approval."
            },
            RiskLevel: Definition.RiskLevel));

        if (!approval.Approved)
        {
            return ToolExecutionResult.Failure(
                "approval-denied",
                approval.SafeMessage,
                approvalStatus: approval.Status);
        }

        cancellationToken.ThrowIfCancellationRequested();
        PatchApplyResult applyResult = patchApplier.Apply(context.Workspace, preview);
        return applyResult.Succeeded
            ? ToolExecutionResult.Success(applyResult.Summary, applyResult.ApprovalStatus)
            : ToolExecutionResult.Failure(
                applyResult.ErrorCode ?? "patch-apply-failed",
                applyResult.Summary,
                approvalStatus: applyResult.ApprovalStatus);
    }

    private static bool TryReadOperation(
        string argumentsJson,
        out PatchOperation operation,
        out ToolExecutionResult failure)
    {
        operation = null!;
        failure = null!;

        try
        {
            using JsonDocument document = JsonDocument.Parse(argumentsJson);
            if (!TryReadRequiredString(document.RootElement, "path", allowEmpty: false, out string path) ||
                !TryReadRequiredString(document.RootElement, "find", allowEmpty: false, out string find) ||
                !TryReadRequiredString(document.RootElement, "replace", allowEmpty: true, out string replace))
            {
                failure = ToolExecutionResult.Failure(
                    "invalid-tool-arguments",
                    "Patch arguments must include path, find, and replace strings.");
                return false;
            }

            operation = new PatchOperation(path, find, replace);
            return true;
        }
        catch (JsonException)
        {
            failure = ToolExecutionResult.Failure(
                "invalid-tool-arguments",
                "Tool arguments must be valid JSON.");
            return false;
        }
    }

    private static bool TryReadRequiredString(
        JsonElement root,
        string propertyName,
        bool allowEmpty,
        out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out JsonElement element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return allowEmpty || !string.IsNullOrWhiteSpace(value);
    }
}
