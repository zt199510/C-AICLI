namespace CSharpAiCli.Core;

public static class OpenAiResponseParser
{
    public static AgentModelTurn ToAgentModelTurn(OpenAiResponseEnvelope response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.ToolCalls.Count > 0)
        {
            AgentToolCallRequest[] toolCalls = response.ToolCalls
                .Select(toolCall => new AgentToolCallRequest(
                    CallId: toolCall.CallId,
                    ToolName: toolCall.Name,
                    ArgumentsJson: NormalizeArgumentsJson(toolCall.ArgumentsJson)))
                .ToArray();

            return AgentModelTurn.RequestTools(toolCalls);
        }

        if (!string.IsNullOrWhiteSpace(response.Text))
        {
            return AgentModelTurn.Final(response.Text);
        }

        return new AgentModelTurn(FinalText: null, ToolCalls: []);
    }

    private static string NormalizeArgumentsJson(string? argumentsJson)
    {
        return string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson;
    }
}
