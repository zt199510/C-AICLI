namespace CSharpAiCli.Core;

public sealed class ConversationTranscript
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string SessionName { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public List<ConversationMessage> Messages { get; init; } = [];
    public List<ConversationToolCall> ToolCalls { get; init; } = [];
    public List<ConversationError> Errors { get; init; } = [];

    public static ConversationTranscript Create(string sessionName, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);

        return new ConversationTranscript
        {
            SchemaVersion = CurrentSchemaVersion,
            SessionName = sessionName,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }

    public void AddUserMessage(string content, DateTimeOffset nowUtc)
    {
        Messages.Add(new ConversationMessage(
            Role: "user",
            CreatedAtUtc: nowUtc,
            Content: content ?? string.Empty,
            Provider: null,
            Model: null,
            ResponseId: null));
        UpdatedAtUtc = nowUtc;
    }

    public void AddAssistantMessage(ChatResponse response, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(response);

        Messages.Add(new ConversationMessage(
            Role: "assistant",
            CreatedAtUtc: nowUtc,
            Content: response.Text,
            Provider: response.Provider,
            Model: response.Model,
            ResponseId: response.ResponseId));
        UpdatedAtUtc = nowUtc;
    }

    public void AddError(ModelError error, DateTimeOffset nowUtc)
    {
        Errors.Add(ConversationError.FromModelError(error, nowUtc));
        UpdatedAtUtc = nowUtc;
    }

    public void AddToolCall(ConversationToolCall toolCall)
    {
        ArgumentNullException.ThrowIfNull(toolCall);

        ToolCalls.Add(toolCall);
        UpdatedAtUtc = toolCall.CompletedAtUtc;
    }
}
