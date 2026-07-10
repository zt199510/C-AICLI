using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class WorkspacePatchTool : ITool
{
    private const string ToolName = "workspace.apply_patch";

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
        ToolName,
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

        PatchPreview preview;
        try
        {
            preview = patchApplier.Preview(context.Workspace, operation);
        }
        catch (ToolExecutionException exception)
        {
            return ToolExecutionResult.Failure(
                exception.ErrorCode,
                exception.SafeMessage,
                retryable: exception.Retryable,
                structuredPayload: CreatePreviewFailurePayload(operation.Path, exception.ErrorCode));
        }

        ApprovalDiagnosticResult approval = ApprovalDiagnostics.Request(approvalPolicy, new ApprovalRequest(
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

        if (!approval.Decision.Approved)
        {
            return ToolExecutionResult.Failure(
                ToolErrorCode.ApprovalDenied,
                approval.Decision.SafeMessage,
                approvalStatus: approval.Decision.Status,
                structuredPayload: CreatePatchPayload(preview, approval.Decision.Status),
                approvalDurationMs: approval.DurationMs);
        }

        cancellationToken.ThrowIfCancellationRequested();
        PatchApplyResult applyResult = patchApplier.Apply(context.Workspace, preview);
        return applyResult.Succeeded
            ? ToolExecutionResult.Success(
                applyResult.Summary,
                approval.Decision.Status,
                structuredPayload: CreatePatchPayload(preview, approval.Decision.Status, applyResult.Diff),
                approvalDurationMs: approval.DurationMs)
            : ToolExecutionResult.Failure(
                applyResult.ErrorCode ?? ToolErrorCode.PatchApplyFailed,
                applyResult.Summary,
                approvalStatus: approval.Decision.Status,
                structuredPayload: CreatePatchPayload(
                    preview,
                    approval.Decision.Status,
                    applyResult.Diff,
                    applyResult.ErrorCode ?? ToolErrorCode.PatchApplyFailed),
                approvalDurationMs: approval.DurationMs);
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
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                failure = ToolExecutionResult.Failure(
                    ToolErrorCode.InvalidToolArguments,
                    "Tool arguments must be a JSON object.",
                    structuredPayload: ToolStructuredPayload.InvalidArguments(ToolName, "arguments"));
                return false;
            }

            if (!TryReadRequiredString(document.RootElement, "path", allowEmpty: false, out string path))
            {
                failure = CreateInvalidArgumentsFailure("path");
                return false;
            }

            if (!TryReadRequiredString(document.RootElement, "find", allowEmpty: false, out string find))
            {
                failure = CreateInvalidArgumentsFailure("find");
                return false;
            }

            if (!TryReadRequiredString(document.RootElement, "replace", allowEmpty: true, out string replace))
            {
                failure = CreateInvalidArgumentsFailure("replace");
                return false;
            }

            operation = new PatchOperation(path, find, replace);
            return true;
        }
        catch (JsonException)
        {
            failure = ToolExecutionResult.Failure(
                ToolErrorCode.InvalidToolArguments,
                "Tool arguments must be valid JSON.",
                structuredPayload: ToolStructuredPayload.InvalidArguments(ToolName, "arguments"));
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

    private static IReadOnlyDictionary<string, JsonElement> CreatePatchPayload(
        PatchPreview preview,
        string approvalStatus,
        string? diff = null,
        string? errorCode = null)
    {
        bool hasDiff = !string.IsNullOrWhiteSpace(diff ?? preview.Diff);
        object dryRunPreview = CreateDryRunPreviewPayload(preview, hasDiff);
        return errorCode is null
            ? ToolStructuredPayload.Create(
                ("path", preview.Operation.Path),
                ("approvalStatus", approvalStatus),
                ("replacements", preview.Replacements),
                ("hasDiff", hasDiff),
                ("dryRunPreview", dryRunPreview))
            : ToolStructuredPayload.Create(
                ("path", preview.Operation.Path),
                ("approvalStatus", approvalStatus),
                ("replacements", preview.Replacements),
                ("hasDiff", hasDiff),
                ("dryRunPreview", dryRunPreview),
                ("errorCode", errorCode));
    }

    private static object CreateDryRunPreviewPayload(PatchPreview preview, bool hasDiff)
    {
        string path = preview.Operation.Path;
        return new
        {
            type = "patch.dry_run_preview",
            paths = new[] { path },
            files = new[]
            {
                new
                {
                    path,
                    replacements = preview.Replacements,
                    hasDiff
                }
            },
            totalReplacements = preview.Replacements,
            hasDiff,
            isDirtyWorkspace = preview.DirtyWorkspace.IsDirty,
            dirtyWorkspaceSummary = preview.DirtyWorkspace.Summary ?? string.Empty
        };
    }

    private static ToolExecutionResult CreateInvalidArgumentsFailure(string argument)
    {
        return ToolExecutionResult.Failure(
            ToolErrorCode.InvalidToolArguments,
            "Patch arguments must include path, find, and replace strings.",
            structuredPayload: ToolStructuredPayload.InvalidArguments(ToolName, argument));
    }

    private static IReadOnlyDictionary<string, JsonElement> CreatePreviewFailurePayload(
        string path,
        string errorCode)
    {
        return ToolStructuredPayload.Create(
            ("path", path),
            ("errorCode", errorCode));
    }
}
