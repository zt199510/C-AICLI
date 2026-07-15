using System.Text;
using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.ProjectPacks.Runtime;

public static class ProjectPackRunRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string RenderText(ProjectPackRunRecord record, ProjectPackRunCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(checkpoint);
        StringBuilder builder = new();
        builder.AppendLine($"{ProductInfo.DisplayName} project pack run");
        builder.AppendLine($"runId: {record.RunId}");
        builder.AppendLine($"pack: {record.PackId}");
        builder.AppendLine($"planId: {record.PlanId}");
        builder.AppendLine($"state: {record.State}");
        builder.AppendLine($"revision: {record.Revision}");
        builder.AppendLine($"jobId: {record.Correlation.JobId ?? "none"}");
        builder.AppendLine($"queueId: {record.Correlation.QueueId ?? "none"}");
        builder.AppendLine($"attempt: {record.Correlation.Attempt}");
        builder.AppendLine($"approvalPersisted: {checkpoint.ApprovalPersisted.ToString().ToLowerInvariant()}");
        builder.AppendLine($"restartRequired: {record.RestartRequired.ToString().ToLowerInvariant()}");
        if (record.Acceptance is not null)
        {
            builder.AppendLine($"acceptance: {record.Acceptance.Outcome}");
            builder.AppendLine($"acceptanceActor: {Safe(record.Acceptance.Actor)}");
            builder.AppendLine($"acceptanceReason: {Safe(record.Acceptance.Reason ?? "none")}");
            builder.AppendLine($"acceptanceVerificationArtifactId: {record.Acceptance.VerificationArtifactId}");
            builder.AppendLine($"acceptanceDecidedAtUtc: {record.Acceptance.DecidedAtUtc:O}");
        }
        if (record.ErrorCode is not null)
        {
            builder.AppendLine($"errorCode: {Safe(record.ErrorCode)}");
        }

        if (record.Summary is not null)
        {
            builder.AppendLine($"summary: {Safe(record.Summary)}");
        }

        foreach (ProjectPackStageCheckpoint stage in checkpoint.Stages)
        {
            builder.AppendLine($"stage: {stage.StageId} status={stage.Status} attempt={stage.Attempt}");
        }

        foreach (ProjectPackRunArtifactPointer artifact in record.Artifacts)
        {
            builder.AppendLine($"artifact: {artifact.Kind} scope={artifact.Scope} path={Safe(artifact.Path)} exists={artifact.Exists.ToString().ToLowerInvariant()}");
        }

        return builder.ToString().TrimEnd();
    }

    public static string RenderJson(ProjectPackRunRecord record, ProjectPackRunCheckpoint checkpoint) =>
        JsonSerializer.Serialize(new
        {
            type = "packs.run",
            schemaVersion = record.SchemaVersion,
            status = "ok",
            run = record,
            checkpoint,
            redaction = new
            {
                secretsRedacted = true,
                rawInputStored = false,
                rawToolArgumentsStored = false,
                approvalStored = false
            }
        }, JsonOptions);

    public static string RenderResumeText(string runId, ProjectPackResumeEligibility eligibility)
    {
        StringBuilder builder = new();
        builder.AppendLine($"{ProductInfo.DisplayName} project pack resume eligibility");
        builder.AppendLine($"runId: {runId}");
        builder.AppendLine($"state: {eligibility.State}");
        builder.AppendLine($"eligible: {eligibility.Eligible.ToString().ToLowerInvariant()}");
        builder.AppendLine($"nextAction: {eligibility.NextAction}");
        builder.AppendLine($"requiresApproval: {eligibility.RequiresApproval.ToString().ToLowerInvariant()}");
        builder.AppendLine($"restartRequired: {eligibility.RestartRequired.ToString().ToLowerInvariant()}");
        if (eligibility.ErrorCode is not null)
        {
            builder.AppendLine($"errorCode: {Safe(eligibility.ErrorCode)}");
        }

        if (eligibility.Summary is not null)
        {
            builder.AppendLine($"summary: {Safe(eligibility.Summary)}");
        }

        return builder.ToString().TrimEnd();
    }

    public static string RenderResumeJson(string runId, ProjectPackResumeEligibility eligibility) =>
        JsonSerializer.Serialize(new
        {
            type = "packs.resume",
            schemaVersion = ProjectPackRunRecord.CurrentSchemaVersion,
            status = eligibility.Eligible ? "eligible" : "ineligible",
            runId,
            state = eligibility.State,
            eligible = eligibility.Eligible,
            nextAction = eligibility.NextAction,
            requiresApproval = eligibility.RequiresApproval,
            restartRequired = eligibility.RestartRequired,
            errorCode = eligibility.ErrorCode is null ? null : Safe(eligibility.ErrorCode),
            summary = eligibility.Summary is null ? null : Safe(eligibility.Summary),
            approvalPersisted = false
        }, JsonOptions);

    public static string RenderRestartText(ProjectPackRestartPreparation preparation)
    {
        StringBuilder builder = new();
        builder.AppendLine($"{ProductInfo.DisplayName} project pack restart plan");
        builder.AppendLine($"parentRunId: {preparation.ParentRecord?.RunId ?? "none"}");
        builder.AppendLine($"newRunId: {preparation.NewRunId ?? "none"}");
        builder.AppendLine($"attempt: {preparation.Attempt?.ToString() ?? "none"}");
        builder.AppendLine($"outputDirectory: {Safe(preparation.OutputDirectory ?? "none")}");
        builder.AppendLine("from: execute");
        builder.AppendLine("approvalPersisted: false");
        builder.AppendLine("partialEvidencePreserved: true");
        return builder.ToString().TrimEnd();
    }

    public static string RenderRestartJson(ProjectPackRestartPreparation preparation) =>
        JsonSerializer.Serialize(new
        {
            type = "packs.restart",
            schemaVersion = ProjectPackRunRecord.CurrentSchemaVersion,
            status = preparation.Succeeded ? "planned" : "failed",
            parentRunId = preparation.ParentRecord?.RunId,
            newRunId = preparation.NewRunId,
            attempt = preparation.Attempt,
            outputDirectory = preparation.OutputDirectory is null ? null : Safe(preparation.OutputDirectory),
            from = "execute",
            approvalPersisted = false,
            partialEvidencePreserved = true,
            diagnostic = preparation.Diagnostic is null ? null : new
            {
                errorCode = Safe(preparation.Diagnostic.ErrorCode),
                summary = Safe(preparation.Diagnostic.Summary),
                path = preparation.Diagnostic.Path is null ? null : Safe(preparation.Diagnostic.Path),
                runId = preparation.Diagnostic.RunId is null ? null : Safe(preparation.Diagnostic.RunId)
            }
        }, JsonOptions);

    public static string RenderFailure(string type, string errorCode, string summary, bool jsonOutput, string? runId = null) =>
        jsonOutput
            ? JsonSerializer.Serialize(new
            {
                type,
                schemaVersion = ProjectPackRunRecord.CurrentSchemaVersion,
                status = "failed",
                runId,
                errorCode = Safe(errorCode),
                summary = Safe(summary)
            }, JsonOptions)
            : $"errorCode: {Safe(errorCode)}{Environment.NewLine}summary: {Safe(summary)}";

    private static string Safe(string value) => DiagnosticSecretRedactor.Redact(value);
}
