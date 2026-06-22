using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class OpenAiResponseParserTests
{
    [Fact]
    public void To_agent_model_turn_returns_final_turn_for_text_response()
    {
        OpenAiResponseEnvelope response = new(
            ResponseId: "resp_text",
            Model: "gpt-test",
            Text: "done");

        AgentModelTurn turn = OpenAiResponseParser.ToAgentModelTurn(response);

        Assert.True(turn.IsFinal);
        Assert.Equal("done", turn.FinalText);
        Assert.Empty(turn.ToolCalls);
    }

    [Fact]
    public void To_agent_model_turn_returns_single_tool_call()
    {
        OpenAiResponseEnvelope response = new(
            ResponseId: "resp_tool",
            Model: "gpt-test",
            Text: "",
            ToolCalls:
            [
                new OpenAiToolCall(
                    CallId: "call_1",
                    Name: "workspace.read_text",
                    ArgumentsJson: """{"path":"README.md"}""")
            ]);

        AgentModelTurn turn = OpenAiResponseParser.ToAgentModelTurn(response);

        AgentToolCallRequest toolCall = Assert.Single(turn.ToolCalls);
        Assert.False(turn.IsFinal);
        Assert.Null(turn.FinalText);
        Assert.Equal("call_1", toolCall.CallId);
        Assert.Equal("workspace.read_text", toolCall.ToolName);
        Assert.Equal("""{"path":"README.md"}""", toolCall.ArgumentsJson);
    }

    [Fact]
    public void To_agent_model_turn_preserves_multiple_tool_call_order()
    {
        OpenAiResponseEnvelope response = new(
            ResponseId: "resp_tools",
            Model: "gpt-test",
            Text: "",
            ToolCalls:
            [
                new OpenAiToolCall("call_1", "tool.first", """{"value":1}"""),
                new OpenAiToolCall("call_2", "tool.second", """{"value":2}"""),
                new OpenAiToolCall("call_3", "tool.third", """{"value":3}""")
            ]);

        AgentModelTurn turn = OpenAiResponseParser.ToAgentModelTurn(response);

        Assert.Collection(
            turn.ToolCalls,
            toolCall => Assert.Equal("call_1", toolCall.CallId),
            toolCall => Assert.Equal("call_2", toolCall.CallId),
            toolCall => Assert.Equal("call_3", toolCall.CallId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void To_agent_model_turn_normalizes_blank_tool_arguments_to_empty_object(string? argumentsJson)
    {
        OpenAiResponseEnvelope response = new(
            ResponseId: "resp_blank_args",
            Model: "gpt-test",
            Text: "",
            ToolCalls:
            [
                new OpenAiToolCall(
                    CallId: "call_blank",
                    Name: "workspace.git_status",
                    ArgumentsJson: argumentsJson)
            ]);

        AgentModelTurn turn = OpenAiResponseParser.ToAgentModelTurn(response);

        AgentToolCallRequest toolCall = Assert.Single(turn.ToolCalls);
        Assert.Equal("{}", toolCall.ArgumentsJson);
    }

    [Fact]
    public void To_agent_model_turn_does_not_treat_blank_text_without_tool_calls_as_final()
    {
        OpenAiResponseEnvelope response = new(
            ResponseId: "resp_blank",
            Model: "gpt-test",
            Text: "   ");

        AgentModelTurn turn = OpenAiResponseParser.ToAgentModelTurn(response);

        Assert.False(turn.IsFinal);
        Assert.Null(turn.FinalText);
        Assert.Empty(turn.ToolCalls);
    }
}
