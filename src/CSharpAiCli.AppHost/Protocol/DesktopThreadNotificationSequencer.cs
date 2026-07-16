using System.Text.Json;
using CSharpAiCli.AppHost.Protocol.Generated;

namespace CSharpAiCli.AppHost.Protocol;

internal sealed class DesktopThreadNotificationSequencer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider timeProvider;
    private long sequence;

    public DesktopThreadNotificationSequencer(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public byte[]? TryCreate(
        string workspaceId,
        ReadOnlyMemory<byte> requestPayload,
        ReadOnlyMemory<byte> responsePayload)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return null;
        }

        try
        {
            using JsonDocument request = JsonDocument.Parse(requestPayload);
            using JsonDocument response = JsonDocument.Parse(responsePayload);
            string? method = request.RootElement.GetProperty("method").GetString();
            string? changeKind = method switch
            {
                DesktopProtocolDefinition.ThreadCreateMethod => "created",
                DesktopProtocolDefinition.ThreadRenameMethod => "renamed",
                DesktopProtocolDefinition.ThreadArchiveMethod => "archived",
                DesktopProtocolDefinition.ThreadDeleteMethod => "deleted",
                _ => null
            };
            if (changeKind is null ||
                !response.RootElement.TryGetProperty("result", out JsonElement result) ||
                !result.TryGetProperty("succeeded", out JsonElement succeeded) ||
                !succeeded.GetBoolean() ||
                !result.TryGetProperty("data", out JsonElement data) ||
                data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("threadId", out JsonElement threadIdElement))
            {
                return null;
            }

            string? threadId = threadIdElement.GetString();
            long revision;
            if (data.TryGetProperty("revision", out JsonElement revisionElement))
            {
                revision = revisionElement.GetInt64();
            }
            else
            {
                revision = request.RootElement.GetProperty("params").GetProperty("expectedRevision").GetInt64();
            }

            if (string.IsNullOrWhiteSpace(threadId))
            {
                return null;
            }

            ThreadChangedParams parameters = new()
            {
                SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
                EventSequence = Interlocked.Increment(ref sequence),
                WorkspaceId = workspaceId,
                ThreadId = threadId,
                Revision = revision,
                ChangeKind = changeKind,
                EmittedAtUtc = timeProvider.GetUtcNow()
            };
            return JsonSerializer.SerializeToUtf8Bytes(new
            {
                jsonrpc = "2.0",
                method = DesktopProtocolDefinition.ThreadChangedNotification,
                @params = parameters
            }, JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException
            or InvalidOperationException
            or KeyNotFoundException
            or FormatException)
        {
            return null;
        }
    }
}
