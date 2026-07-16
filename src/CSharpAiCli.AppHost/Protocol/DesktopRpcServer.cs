using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CSharpAiCli.AppHost.Protocol.Generated;
using CSharpAiCli.Application;

namespace CSharpAiCli.AppHost.Protocol;

public sealed class DesktopRpcServer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly WorkspaceApplicationService workspaceService;
    private bool initialized;
    private bool shutdownRequested;

    public DesktopRpcServer()
        : this(new WorkspaceApplicationService())
    {
    }

    public DesktopRpcServer(WorkspaceApplicationService workspaceService)
    {
        this.workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
    }

    public async Task RunAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        while (!shutdownRequested)
        {
            byte[]? payload = await DesktopProtocolFraming.ReadFrameAsync(input, cancellationToken).ConfigureAwait(false);
            if (payload is null)
            {
                return;
            }

            byte[] response = Handle(payload);
            await DesktopProtocolFraming.WriteFrameAsync(output, response, cancellationToken).ConfigureAwait(false);
        }
    }

    public byte[] Handle(ReadOnlyMemory<byte> payload)
    {
        JsonNode? id = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Error(id, -32600, "invalid-request", "JSON-RPC request must be an object.");
            }

            if (root.TryGetProperty("id", out JsonElement idElement))
            {
                id = JsonNode.Parse(idElement.GetRawText());
            }

            if (!root.TryGetProperty("jsonrpc", out JsonElement jsonRpc) ||
                jsonRpc.ValueKind != JsonValueKind.String ||
                !string.Equals(jsonRpc.GetString(), "2.0", StringComparison.Ordinal) ||
                !root.TryGetProperty("method", out JsonElement methodElement) ||
                methodElement.ValueKind != JsonValueKind.String)
            {
                return Error(id, -32600, "invalid-request", "JSON-RPC version and method are required.");
            }

            string method = methodElement.GetString()!;
            JsonElement parameters = root.TryGetProperty("params", out JsonElement paramsElement)
                ? paramsElement
                : default;

            if (!string.Equals(method, DesktopProtocolDefinition.InitializeMethod, StringComparison.Ordinal) && !initialized)
            {
                return Error(id, -32002, "initialize-required", "app.initialize must complete first.");
            }

            return method switch
            {
                DesktopProtocolDefinition.InitializeMethod => Initialize(id, parameters),
                DesktopProtocolDefinition.WorkspaceOpenMethod => OpenWorkspace(id, parameters),
                DesktopProtocolDefinition.ShutdownMethod => Shutdown(id, parameters),
                _ => Error(id, -32601, "method-not-found", "Desktop protocol method is not available.")
            };
        }
        catch (JsonException)
        {
            return Error(id, -32700, "parse-error", "JSON payload is invalid.");
        }
    }

    private byte[] Initialize(JsonNode? id, JsonElement parameters)
    {
        if (!TryDeserialize(parameters, out InitializeParams? request) ||
            request is null ||
            string.IsNullOrWhiteSpace(request.ClientName) ||
            string.IsNullOrWhiteSpace(request.ClientVersion))
        {
            return Error(id, -32602, "invalid-params", "Initialize parameters are invalid.");
        }

        if (!string.Equals(request.ProtocolVersion, DesktopProtocolDefinition.Version, StringComparison.Ordinal))
        {
            return Error(id, -32001, "protocol-version-unsupported", "Desktop protocol version is not supported.");
        }

        initialized = true;
        InitializeResult result = new(
            DesktopProtocolDefinition.Version,
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
            [DesktopProtocolDefinition.InitializeMethod, DesktopProtocolDefinition.WorkspaceOpenMethod],
            new SecuritySummary(false, false, false, "framed-json-rpc-stdio"));
        return Success(id, result);
    }

    private byte[] OpenWorkspace(JsonNode? id, JsonElement parameters)
    {
        if (!TryDeserialize(parameters, out WorkspaceOpenParams? request) ||
            request is null ||
            string.IsNullOrWhiteSpace(request.Path))
        {
            return Error(id, -32602, "invalid-params", "Workspace path is required.");
        }

        WorkspaceOpenApplicationResult opened = workspaceService.Open(new WorkspaceOpenRequest(request.Path));
        WorkspaceOpenResult result = new(
            opened.Success,
            opened.WorkspaceId,
            opened.RootPath,
            opened.Status,
            opened.ErrorCode,
            opened.SafeMessage);
        return Success(id, result);
    }

    private byte[] Shutdown(JsonNode? id, JsonElement parameters)
    {
        if (!TryDeserialize(parameters, out ShutdownParams? request) || request is null)
        {
            return Error(id, -32602, "invalid-params", "Shutdown parameters are invalid.");
        }

        shutdownRequested = true;
        return Success(id, new ShutdownResult(true));
    }

    private static bool TryDeserialize<T>(JsonElement element, out T? value)
    {
        value = default;
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return false;
        }

        try
        {
            value = element.Deserialize<T>(JsonOptions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static byte[] Success<T>(JsonNode? id, T result)
    {
        JsonObject response = new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = JsonSerializer.SerializeToNode(result, JsonOptions)
        };
        return JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
    }

    private static byte[] Error(JsonNode? id, int code, string errorCode, string safeMessage)
    {
        JsonObject response = new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = safeMessage,
                ["data"] = new JsonObject
                {
                    ["errorCode"] = errorCode,
                    ["safeMessage"] = safeMessage
                }
            }
        };
        return JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
    }
}
