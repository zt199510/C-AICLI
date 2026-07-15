using System.Globalization;
using System.Text;
using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.ProjectPacks.Runtime;

public static class ManagedArtifactRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string RenderListText(ManagedArtifactListResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        StringBuilder builder = new();
        builder.AppendLine("C# AI CLI managed artifacts");
        builder.AppendLine($"artifacts: {result.Artifacts.Count.ToString(CultureInfo.InvariantCulture)}");
        foreach ((ManagedArtifactManifest manifest, ManagedArtifactEntry artifact) in result.Artifacts)
        {
            builder.AppendLine(
                $"- {artifact.ArtifactId} run={manifest.RunId} status={manifest.RunState} kind={artifact.Kind} " +
                $"ownership={artifact.Ownership} availability={artifact.Availability} bytes={artifact.Size?.ToString(CultureInfo.InvariantCulture) ?? "unknown"} path={Safe(artifact.Path)}");
        }

        AppendDiagnostics(builder, result.Diagnostics);
        return builder.ToString().TrimEnd();
    }

    public static string RenderListJson(ManagedArtifactListResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return JsonSerializer.Serialize(new
        {
            type = "artifacts.list",
            schemaVersion = ManagedArtifactManifest.CurrentSchemaVersion,
            artifacts = result.Artifacts.Select(item => ToJson(item.Manifest, item.Artifact)),
            diagnostics = result.Diagnostics.Select(ToJson)
        }, JsonOptions);
    }

    public static string RenderShowText(ManagedArtifactManifest manifest, ManagedArtifactEntry artifact)
    {
        StringBuilder builder = new();
        builder.AppendLine("C# AI CLI managed artifact");
        builder.AppendLine($"id: {artifact.ArtifactId}");
        builder.AppendLine($"pointerId: {artifact.PointerId}");
        builder.AppendLine($"runId: {manifest.RunId}");
        builder.AppendLine($"runRevision: {manifest.RunRevision.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"runState: {manifest.RunState}");
        builder.AppendLine($"jobId: {manifest.Owner.JobId ?? "none"}");
        builder.AppendLine($"queueId: {manifest.Owner.QueueId ?? "none"}");
        builder.AppendLine($"attempt: {manifest.Owner.Attempt.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"kind: {artifact.Kind}");
        builder.AppendLine($"ownership: {artifact.Ownership}");
        builder.AppendLine($"path: {Safe(artifact.Path)}");
        builder.AppendLine($"size: {artifact.Size?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
        builder.AppendLine($"sha256: {artifact.Sha256 ?? "unknown"}");
        builder.AppendLine($"verification: {artifact.Verification}");
        builder.AppendLine($"availability: {artifact.Availability}");
        builder.AppendLine($"retentionClass: {artifact.Retention.Class}");
        builder.AppendLine($"owned: {artifact.Retention.Owned.ToString().ToLowerInvariant()}");
        builder.AppendLine($"prunable: {artifact.Retention.Prunable.ToString().ToLowerInvariant()}");
        builder.AppendLine($"declaredAtUtc: {artifact.DeclaredAtUtc.ToString("O", CultureInfo.InvariantCulture)}");
        if (artifact.Tombstone is not null)
        {
            builder.AppendLine($"prunedAtUtc: {artifact.Tombstone.RemovedAtUtc.ToString("O", CultureInfo.InvariantCulture)}");
            builder.AppendLine($"pruneReason: {Safe(artifact.Tombstone.Reason)}");
        }

        return builder.ToString().TrimEnd();
    }

    public static string RenderShowJson(ManagedArtifactManifest manifest, ManagedArtifactEntry artifact) =>
        JsonSerializer.Serialize(new
        {
            type = "artifacts.show",
            schemaVersion = ManagedArtifactManifest.CurrentSchemaVersion,
            artifact = ToJson(manifest, artifact)
        }, JsonOptions);

    public static string RenderExport(
        ManagedArtifactManifest manifest,
        ManagedArtifactEntry artifact,
        string format) => format switch
        {
            "json" => JsonSerializer.Serialize(ToJson(manifest, artifact), JsonOptions),
            "markdown" => RenderMarkdown(manifest, artifact),
            _ => RenderShowText(manifest, artifact)
        };

    public static string RenderVerifyText(ManagedArtifactVerificationResult result)
    {
        StringBuilder builder = new();
        builder.AppendLine($"artifactId: {result.ArtifactId}");
        builder.AppendLine($"status: {(result.Succeeded ? "verified" : "failed")}");
        builder.AppendLine($"availability: {result.Availability}");
        builder.AppendLine($"observedSize: {result.ObservedSize?.ToString(CultureInfo.InvariantCulture) ?? "none"}");
        builder.AppendLine($"observedSha256: {result.ObservedSha256 ?? "none"}");
        if (result.Diagnostic is not null)
        {
            builder.AppendLine($"errorCode: {result.Diagnostic.ErrorCode}");
            builder.AppendLine($"summary: {Safe(result.Diagnostic.Summary)}");
        }

        return builder.ToString().TrimEnd();
    }

    public static string RenderVerifyJson(ManagedArtifactVerificationResult result) =>
        JsonSerializer.Serialize(new
        {
            type = "artifacts.verify",
            schemaVersion = ManagedArtifactManifest.CurrentSchemaVersion,
            artifactId = result.ArtifactId,
            status = result.Succeeded ? "verified" : "failed",
            availability = result.Availability,
            observedSize = result.ObservedSize,
            observedSha256 = result.ObservedSha256,
            diagnostic = result.Diagnostic is null ? null : ToJson(result.Diagnostic)
        }, JsonOptions);

    public static string RenderFailure(string type, ManagedArtifactDiagnostic diagnostic, bool json) => json
        ? JsonSerializer.Serialize(new
        {
            type,
            schemaVersion = ManagedArtifactManifest.CurrentSchemaVersion,
            status = "failed",
            diagnostic = ToJson(diagnostic)
        }, JsonOptions)
        : $"errorCode: {diagnostic.ErrorCode}{Environment.NewLine}summary: {Safe(diagnostic.Summary)}";

    public static string RenderPruneText(ManagedArtifactPruneResult result)
    {
        StringBuilder builder = new();
        builder.AppendLine("C# AI CLI artifact prune");
        builder.AppendLine($"mode: {(result.Apply ? "apply" : "dry-run")}");
        builder.AppendLine($"candidates: {result.Items.Count.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"totalBytes: {result.TotalBytes.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"deleted: {result.DeletedCount.ToString(CultureInfo.InvariantCulture)}");
        foreach (ManagedArtifactPruneItem item in result.Items)
        {
            builder.AppendLine(
                $"- {item.ArtifactId} run={item.RunId} status={item.RunState} bytes={item.Size.ToString(CultureInfo.InvariantCulture)} " +
                $"action={(item.Deleted ? "deleted" : result.Apply ? "retained" : "candidate")} path={Safe(item.Path)} reason={Safe(item.Reason)}" +
                (item.ErrorCode is null ? string.Empty : $" errorCode={item.ErrorCode}"));
        }

        AppendDiagnostics(builder, result.Diagnostics);
        return builder.ToString().TrimEnd();
    }

    public static string RenderPruneJson(ManagedArtifactPruneResult result) =>
        JsonSerializer.Serialize(new
        {
            type = "artifacts.prune",
            schemaVersion = ManagedArtifactManifest.CurrentSchemaVersion,
            mode = result.Apply ? "apply" : "dry-run",
            candidates = result.Items.Count,
            totalBytes = result.TotalBytes,
            deleted = result.DeletedCount,
            artifacts = result.Items.Select(ToJson),
            diagnostics = result.Diagnostics.Select(ToJson)
        }, JsonOptions);

    private static string RenderMarkdown(ManagedArtifactManifest manifest, ManagedArtifactEntry artifact)
    {
        StringBuilder builder = new();
        builder.AppendLine("# C# AI CLI Managed Artifact");
        builder.AppendLine();
        builder.AppendLine($"- Artifact ID: `{artifact.ArtifactId}`");
        builder.AppendLine($"- Pointer ID: `{artifact.PointerId}`");
        builder.AppendLine($"- Run: `{manifest.RunId}` revision {manifest.RunRevision.ToString(CultureInfo.InvariantCulture)} (`{manifest.RunState}`)");
        builder.AppendLine($"- Job: `{manifest.Owner.JobId ?? "none"}`");
        builder.AppendLine($"- Queue: `{manifest.Owner.QueueId ?? "none"}`");
        builder.AppendLine($"- Attempt: `{manifest.Owner.Attempt.ToString(CultureInfo.InvariantCulture)}`");
        builder.AppendLine($"- Kind: `{artifact.Kind}`");
        builder.AppendLine($"- Ownership: `{artifact.Ownership}`");
        builder.AppendLine($"- Path: `{Safe(artifact.Path)}`");
        builder.AppendLine($"- Size: `{artifact.Size?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}`");
        builder.AppendLine($"- SHA256: `{artifact.Sha256 ?? "unknown"}`");
        builder.AppendLine($"- Verification: `{artifact.Verification}`");
        builder.AppendLine($"- Availability: `{artifact.Availability}`");
        builder.AppendLine($"- Retention: `{artifact.Retention.Class}`; prunable=`{artifact.Retention.Prunable.ToString().ToLowerInvariant()}`");
        if (artifact.Tombstone is not null)
        {
            builder.AppendLine($"- Tombstone: `{artifact.Tombstone.RemovedAtUtc.ToString("O", CultureInfo.InvariantCulture)}`; {Safe(artifact.Tombstone.Reason)}");
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static object ToJson(ManagedArtifactManifest manifest, ManagedArtifactEntry artifact) => new
    {
        artifactId = artifact.ArtifactId,
        pointerId = artifact.PointerId,
        runId = manifest.RunId,
        runRevision = manifest.RunRevision,
        runState = manifest.RunState,
        owner = new
        {
            jobId = manifest.Owner.JobId,
            queueId = manifest.Owner.QueueId,
            rootRunId = manifest.Owner.RootRunId,
            parentRunId = manifest.Owner.ParentRunId,
            attempt = manifest.Owner.Attempt
        },
        kind = artifact.Kind,
        ownership = artifact.Ownership,
        path = Safe(artifact.Path),
        size = artifact.Size,
        sha256 = artifact.Sha256,
        verification = artifact.Verification,
        availability = artifact.Availability,
        retention = artifact.Retention,
        declaredAtUtc = artifact.DeclaredAtUtc,
        tombstone = artifact.Tombstone is null ? null : new
        {
            artifact.Tombstone.RemovedAtUtc,
            reason = Safe(artifact.Tombstone.Reason),
            artifact.Tombstone.OriginalSize,
            artifact.Tombstone.OriginalSha256,
            artifact.Tombstone.RunState,
            artifact.Tombstone.JobId,
            artifact.Tombstone.QueueId
        }
    };

    private static object ToJson(ManagedArtifactPruneItem item) => new
    {
        item.ArtifactId,
        item.RunId,
        item.RunState,
        path = Safe(item.Path),
        item.Size,
        item.DeclaredAtUtc,
        reason = Safe(item.Reason),
        item.Deleted,
        item.ErrorCode,
        summary = item.Summary is null ? null : Safe(item.Summary)
    };

    private static object ToJson(ManagedArtifactDiagnostic diagnostic) => new
    {
        errorCode = diagnostic.ErrorCode,
        summary = Safe(diagnostic.Summary),
        diagnostic.ArtifactId,
        diagnostic.RunId,
        path = diagnostic.Path is null ? null : Safe(diagnostic.Path)
    };

    private static void AppendDiagnostics(StringBuilder builder, IReadOnlyList<ManagedArtifactDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return;
        }

        builder.AppendLine("diagnostics:");
        foreach (ManagedArtifactDiagnostic diagnostic in diagnostics)
        {
            builder.AppendLine($"- {diagnostic.ErrorCode} run={diagnostic.RunId ?? "unknown"} summary={Safe(diagnostic.Summary)}");
        }
    }

    private static string Safe(string value) => DiagnosticSecretRedactor.Redact(value ?? string.Empty)
        .Replace("`", "'", StringComparison.Ordinal);
}
