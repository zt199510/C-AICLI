namespace CSharpAiCli.Core;

public interface IConversationStore
{
    IReadOnlyList<string> ListSessionNames() => throw new NotSupportedException("This conversation store does not support listing sessions.");

    bool Exists(ConversationSessionName sessionName) => throw new NotSupportedException("This conversation store does not support checking session existence.");

    ConversationTranscript LoadOrCreate(ConversationSessionName sessionName, DateTimeOffset nowUtc);

    bool Rename(ConversationSessionName sourceSessionName, ConversationSessionName destinationSessionName) =>
        throw new NotSupportedException("This conversation store does not support renaming sessions.");

    string Save(ConversationSessionName sessionName, ConversationTranscript transcript);

    bool Delete(ConversationSessionName sessionName) => throw new NotSupportedException("This conversation store does not support deleting sessions.");
}
