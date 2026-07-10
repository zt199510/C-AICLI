using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed record ToolExecutionResult(
    bool Succeeded,
    string Summary,
    string? ErrorCode,
    bool Retryable,
    string ApprovalStatus = "not-required",
    IReadOnlyDictionary<string, JsonElement>? StructuredPayload = null,
    long? ApprovalDurationMs = null)
{
    private readonly IReadOnlyDictionary<string, JsonElement>? structuredPayload =
        CopyStructuredPayload(StructuredPayload);

    public IReadOnlyDictionary<string, JsonElement>? StructuredPayload
    {
        get => structuredPayload;
        init => structuredPayload = CopyStructuredPayload(value);
    }

    public long? ApprovalDurationMs { get; init; } = ApprovalDurationMs;

    public static ToolExecutionResult Success(
        string summary,
        string approvalStatus = "not-required",
        IReadOnlyDictionary<string, JsonElement>? structuredPayload = null,
        long? approvalDurationMs = null)
    {
        return new ToolExecutionResult(
            Succeeded: true,
            Summary: summary ?? string.Empty,
            ErrorCode: null,
            Retryable: false,
            ApprovalStatus: approvalStatus,
            StructuredPayload: structuredPayload,
            ApprovalDurationMs: approvalDurationMs);
    }

    public static ToolExecutionResult Failure(
        string errorCode,
        string safeMessage,
        bool retryable = false,
        string approvalStatus = "not-required",
        IReadOnlyDictionary<string, JsonElement>? structuredPayload = null,
        long? approvalDurationMs = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        return new ToolExecutionResult(
            Succeeded: false,
            Summary: safeMessage ?? string.Empty,
            ErrorCode: errorCode,
            Retryable: retryable,
            ApprovalStatus: approvalStatus,
            StructuredPayload: structuredPayload,
            ApprovalDurationMs: approvalDurationMs);
    }

    private static IReadOnlyDictionary<string, JsonElement>? CopyStructuredPayload(
        IReadOnlyDictionary<string, JsonElement>? structuredPayload)
    {
        if (structuredPayload is null)
        {
            return null;
        }

        Dictionary<string, JsonElement> copy = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JsonElement> item in structuredPayload)
        {
            copy[item.Key] = item.Value.Clone();
        }

        return new ReadOnlyDictionary<string, JsonElement>(copy);
    }
}
