namespace CSharpAiCli.Core;

public interface IConversationStore
{
    IReadOnlyList<string> ListSessionNames() => throw new NotSupportedException("This conversation store does not support listing sessions.");

    IReadOnlyList<ConversationTranscriptSummary> ListSummaries() =>
        throw new NotSupportedException("This conversation store does not support listing session summaries.");

    IReadOnlyList<ConversationTranscriptSummary> ListSummaries(int? limit) => limit is > 0
        ? ListSummaries().Take(limit.Value).ToArray()
        : ListSummaries();

    bool Exists(ConversationSessionName sessionName) => throw new NotSupportedException("This conversation store does not support checking session existence.");

    bool TryGetSummary(ConversationSessionName sessionName, out ConversationTranscriptSummary? summary)
    {
        throw new NotSupportedException("This conversation store does not support retrieving session summaries.");
    }

    bool TryLoad(ConversationSessionName sessionName, out ConversationTranscript? transcript)
    {
        throw new NotSupportedException("This conversation store does not support loading existing sessions.");
    }

    ConversationTranscript LoadOrCreate(ConversationSessionName sessionName, DateTimeOffset nowUtc);

    bool Rename(ConversationSessionName sourceSessionName, ConversationSessionName destinationSessionName) =>
        throw new NotSupportedException("This conversation store does not support renaming sessions.");

    string Save(ConversationSessionName sessionName, ConversationTranscript transcript);

    bool Delete(ConversationSessionName sessionName) => throw new NotSupportedException("This conversation store does not support deleting sessions.");
}
