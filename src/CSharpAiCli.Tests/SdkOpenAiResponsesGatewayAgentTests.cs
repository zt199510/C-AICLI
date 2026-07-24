#pragma warning disable OPENAI001

using System.Text.Json;
using CSharpAiCli.Core;
using OpenAI.Responses;

namespace CSharpAiCli.Tests;

public sealed class SdkOpenAiResponsesGatewayAgentTests
{
    [Fact]
    public void Create_agent_options_maps_prompt_tools_and_structured_tool_outputs()
    {
        using JsonDocument document = JsonDocument.Parse("""
        {
          "path": "note.txt",
          "lineCount": 2
        }
        """);
        Dictionary<string, JsonElement> structuredPayload = document.RootElement
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value);
        OpenAiAgentRequest request = new(
            Model: "gpt-test",
            Prompt: "Inspect note.txt.",
            PreviousResponseId: "resp_previous",
            Instructions: "Use local tools.",
            Tools:
            [
                new OpenAiToolDefinition(
                    Type: "function",
                    Name: "workspace.read_text",
                    Description: "Read a text file.",
                    ParametersSchema: """{"type":"object","properties":{"path":{"type":"string"}}}""")
            ],
            ToolResults:
            [
                new OpenAiToolResultInput(
                    CallId: "call_read",
                    ToolName: "workspace.read_text",
                    Succeeded: true,
                    Summary: "Read note.txt.",
                    ErrorCode: null,
                    ApprovalStatus: "approved",
                    StructuredPayload: structuredPayload)
            ]);

        CreateResponseOptions options = SdkOpenAiResponsesGateway.CreateAgentOptions(request);

        Assert.Equal("gpt-test", options.Model);
        Assert.Equal("resp_previous", options.PreviousResponseId);
        Assert.Equal("Use local tools.", options.Instructions);
        Assert.False(options.StreamingEnabled);
        Assert.Equal(2, options.InputItems.Count);

        FunctionTool tool = Assert.IsType<FunctionTool>(Assert.Single(options.Tools));
        Assert.Matches("^[a-zA-Z0-9_-]+$", tool.FunctionName);
        Assert.StartsWith("workspace_read_text_", tool.FunctionName, StringComparison.Ordinal);
        Assert.NotEqual("workspace.read_text", tool.FunctionName);
        Assert.Equal("Read a text file.", tool.FunctionDescription);
        Assert.Equal(
            """{"type":"object","properties":{"path":{"type":"string"}}}""",
            tool.FunctionParameters.ToString());

        FunctionCallOutputResponseItem toolOutput =
            Assert.IsType<FunctionCallOutputResponseItem>(options.InputItems[1]);
        Assert.Equal("call_read", toolOutput.CallId);

        using JsonDocument outputDocument = JsonDocument.Parse(toolOutput.FunctionOutput);
        JsonElement output = outputDocument.RootElement;
        Assert.Equal("call_read", output.GetProperty("callId").GetString());
        Assert.Equal("workspace.read_text", output.GetProperty("toolName").GetString());
        Assert.True(output.GetProperty("succeeded").GetBoolean());
        Assert.Equal("Read note.txt.", output.GetProperty("summary").GetString());
        Assert.Equal(JsonValueKind.Null, output.GetProperty("errorCode").ValueKind);
        Assert.Equal("approved", output.GetProperty("approvalStatus").GetString());
        Assert.False(output.GetProperty("retryable").GetBoolean());
        Assert.Equal("note.txt", output.GetProperty("structuredPayload").GetProperty("path").GetString());
        Assert.Equal(2, output.GetProperty("structuredPayload").GetProperty("lineCount").GetInt32());
    }

    [Fact]
    public void Create_api_tool_name_map_preserves_valid_names_and_maps_invalid_names_uniquely()
    {
        OpenAiToolDefinition[] tools =
        [
            new("function", "valid_tool-1", "Valid.", """{"type":"object"}"""),
            new("function", "workspace.read_text", "Read.", """{"type":"object"}"""),
            new("function", "workspace/read_text", "Read slash.", """{"type":"object"}""")
        ];

        IReadOnlyDictionary<string, string> names =
            SdkOpenAiResponsesGateway.CreateApiToolNameMap(tools);

        Assert.Equal("valid_tool-1", names["valid_tool-1"]);
        Assert.Matches("^[a-zA-Z0-9_-]+$", names["workspace.read_text"]);
        Assert.Matches("^[a-zA-Z0-9_-]+$", names["workspace/read_text"]);
        Assert.NotEqual(names["workspace.read_text"], names["workspace/read_text"]);
        Assert.All(names.Values, name => Assert.InRange(name.Length, 1, 64));
    }

    [Fact]
    public void Create_api_tool_name_map_rejects_duplicate_canonical_names()
    {
        OpenAiToolDefinition[] tools =
        [
            new("function", "workspace.read_text", "Read.", """{"type":"object"}"""),
            new("function", "workspace.read_text", "Read duplicate.", """{"type":"object"}""")
        ];

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => SdkOpenAiResponsesGateway.CreateApiToolNameMap(tools));

        Assert.Equal("OpenAI tool definitions contain a duplicate name.", exception.Message);
    }

    [Fact]
    public void Create_tool_result_output_json_includes_failure_error_and_retryability()
    {
        string outputJson = SdkOpenAiResponsesGateway.CreateToolResultOutputJson(
            new OpenAiToolResultInput(
                CallId: "call_shell",
                ToolName: "workspace.run_shell",
                Succeeded: false,
                Summary: "Shell command was denied.",
                ErrorCode: "approval-denied",
                ApprovalStatus: "denied",
                Retryable: true));

        using JsonDocument document = JsonDocument.Parse(outputJson);
        JsonElement output = document.RootElement;
        Assert.False(output.GetProperty("succeeded").GetBoolean());
        Assert.Equal("Shell command was denied.", output.GetProperty("summary").GetString());
        Assert.Equal("approval-denied", output.GetProperty("errorCode").GetString());
        Assert.Equal("denied", output.GetProperty("approvalStatus").GetString());
        Assert.True(output.GetProperty("retryable").GetBoolean());
        Assert.False(output.TryGetProperty("structuredPayload", out _));
    }

    [Fact]
    public void To_envelope_extracts_function_calls_from_response_output_items()
    {
        ResponseResult response = new()
        {
            Id = "resp_tool",
            Model = "gpt-test"
        };
        response.OutputItems.Add(ResponseItem.CreateFunctionCallItem(
            "call_read",
            "workspace.read_text",
            BinaryData.FromString("""{"path":"note.txt"}""")));

        OpenAiResponseEnvelope envelope = SdkOpenAiResponsesGateway.ToEnvelope(
            response,
            fallbackModel: "fallback-model");

        Assert.Equal("resp_tool", envelope.ResponseId);
        Assert.Equal("gpt-test", envelope.Model);
        OpenAiToolCall toolCall = Assert.Single(envelope.ToolCalls);
        Assert.Equal("call_read", toolCall.CallId);
        Assert.Equal("workspace.read_text", toolCall.Name);
        Assert.Equal("""{"path":"note.txt"}""", toolCall.ArgumentsJson);
    }

    [Fact]
    public void To_envelope_restores_canonical_tool_name_from_api_alias()
    {
        ResponseResult response = new()
        {
            Id = "resp_tool",
            Model = "gpt-test"
        };
        response.OutputItems.Add(ResponseItem.CreateFunctionCallItem(
            "call_read",
            "workspace_read_text_abc123",
            BinaryData.FromString("""{"path":"note.txt"}""")));

        OpenAiResponseEnvelope envelope = SdkOpenAiResponsesGateway.ToEnvelope(
            response,
            fallbackModel: "fallback-model",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["workspace_read_text_abc123"] = "workspace.read_text"
            });

        Assert.Equal("workspace.read_text", Assert.Single(envelope.ToolCalls).Name);
    }
}

#pragma warning restore OPENAI001
