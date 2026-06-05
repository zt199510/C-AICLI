namespace CSharpAiCli.Core;

public static class ConversationTranscriptRecorder
{
    public static void RecordTurn(
        ConversationTranscript transcript,
        string prompt,
        ChatModelResult result,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(result);

        transcript.AddUserMessage(prompt, nowUtc);

        if (result.Response is not null)
        {
            transcript.AddAssistantMessage(result.Response, nowUtc);
            return;
        }

        if (result.Error is not null)
        {
            transcript.AddError(result.Error, nowUtc);
        }
    }
}
