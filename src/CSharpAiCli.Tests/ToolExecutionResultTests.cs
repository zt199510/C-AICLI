using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ToolExecutionResultTests
{
    [Fact]
    public void Success_preserves_existing_defaults_without_structured_payload()
    {
        ToolExecutionResult result = ToolExecutionResult.Success("safe summary", "approved");

        Assert.True(result.Succeeded);
        Assert.Equal("safe summary", result.Summary);
        Assert.Null(result.ErrorCode);
        Assert.False(result.Retryable);
        Assert.Equal("approved", result.ApprovalStatus);
        Assert.Null(result.StructuredPayload);
    }

    [Fact]
    public void Failure_preserves_existing_defaults_without_structured_payload()
    {
        ToolExecutionResult result = ToolExecutionResult.Failure(
            "file-not-found",
            "File was not found.",
            retryable: true,
            approvalStatus: "denied");

        Assert.False(result.Succeeded);
        Assert.Equal("File was not found.", result.Summary);
        Assert.Equal("file-not-found", result.ErrorCode);
        Assert.True(result.Retryable);
        Assert.Equal("denied", result.ApprovalStatus);
        Assert.Null(result.StructuredPayload);
    }

    [Fact]
    public void Success_can_carry_structured_payload()
    {
        ToolExecutionResult result;
        Dictionary<string, JsonElement> sourcePayload;
        using (JsonDocument document = JsonDocument.Parse("""
        {
          "path": "notes.txt",
          "lineCount": 3,
          "truncated": false
        }
        """))
        {
            sourcePayload = CreatePayload(document.RootElement);

            result = ToolExecutionResult.Success(
                "Read notes.txt.",
                structuredPayload: sourcePayload);
        }

        using JsonDocument extraDocument = JsonDocument.Parse("""{"ignored":true}""");
        sourcePayload["extra"] = extraDocument.RootElement.Clone();

        Assert.True(result.Succeeded);
        IReadOnlyDictionary<string, JsonElement> structuredPayload =
            result.StructuredPayload ?? throw new InvalidOperationException("Structured payload was not set.");
        Assert.Equal("notes.txt", structuredPayload["path"].GetString());
        Assert.Equal(3, structuredPayload["lineCount"].GetInt32());
        Assert.False(structuredPayload["truncated"].GetBoolean());
        Assert.False(structuredPayload.ContainsKey("extra"));
    }

    [Fact]
    public void Failure_can_carry_structured_payload()
    {
        using JsonDocument document = JsonDocument.Parse("""
        {
          "path": "missing.txt",
          "retryAfterSeconds": 10
        }
        """);

        ToolExecutionResult result = ToolExecutionResult.Failure(
            "file-not-found",
            "File was not found.",
            retryable: false,
            structuredPayload: CreatePayload(document.RootElement));

        Assert.False(result.Succeeded);
        Assert.Equal("file-not-found", result.ErrorCode);
        IReadOnlyDictionary<string, JsonElement> structuredPayload =
            result.StructuredPayload ?? throw new InvalidOperationException("Structured payload was not set.");
        Assert.Equal("missing.txt", structuredPayload["path"].GetString());
        Assert.Equal(10, structuredPayload["retryAfterSeconds"].GetInt32());
    }

    [Fact]
    public void Factories_reject_negative_approval_duration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ToolExecutionResult.Success(
                "Done.",
                approvalDurationMs: -1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ToolExecutionResult.Failure(
                "approval-denied",
                "Denied.",
                approvalDurationMs: -1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ToolExecutionResult(
                Succeeded: true,
                Summary: "Done.",
                ErrorCode: null,
                Retryable: false,
                ApprovalDurationMs: -1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ToolExecutionResult.Success("Done.") with
            {
                ApprovalDurationMs = -1
            });
    }

    private static Dictionary<string, JsonElement> CreatePayload(JsonElement root)
    {
        return root.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value);
    }
}
