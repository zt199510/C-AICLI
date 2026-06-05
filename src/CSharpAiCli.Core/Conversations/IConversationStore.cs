namespace CSharpAiCli.Core;

public interface IConversationStore
{
    ConversationTranscript LoadOrCreate(ConversationSessionName sessionName, DateTimeOffset nowUtc);

    string Save(ConversationSessionName sessionName, ConversationTranscript transcript);
}
