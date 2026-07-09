using System.Text;
using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class McpToolBridge
{
    private const string TrustedRegistrySource = "user config";

    private readonly IMcpToolDiscoverer discoverer;
    private readonly IMcpToolInvoker invoker;
    private readonly IApprovalPolicy approvalPolicy;

    public McpToolBridge(IMcpToolInvoker invoker)
        : this(new UnavailableMcpToolDiscoverer(), invoker, ApprovalPolicyResolver.Resolve(ApprovalMode.Never))
    {
    }

    public McpToolBridge(IMcpToolInvoker invoker, IApprovalPolicy approvalPolicy)
        : this(new UnavailableMcpToolDiscoverer(), invoker, approvalPolicy)
    {
    }

    public McpToolBridge(IMcpToolDiscoverer discoverer, IMcpToolInvoker invoker)
        : this(discoverer, invoker, ApprovalPolicyResolver.Resolve(ApprovalMode.Never))
    {
    }

    public McpToolBridge(
        IMcpToolDiscoverer discoverer,
        IMcpToolInvoker invoker,
        IApprovalPolicy approvalPolicy)
    {
        ArgumentNullException.ThrowIfNull(discoverer);
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(approvalPolicy);
        this.discoverer = discoverer;
        this.invoker = invoker;
        this.approvalPolicy = approvalPolicy;
    }

    public IReadOnlyList<ITool> CreateTools(McpConfiguration configuration)
    {
        return CreateTools(configuration, WorkspaceContext.Detect(Environment.CurrentDirectory, Environment.CurrentDirectory));
    }

    public IReadOnlyList<ITool> CreateTools(
        McpConfiguration configuration,
        WorkspaceContext workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(workspace);

        List<ITool> tools = [];
        HashSet<string> usedLocalNames = new(StringComparer.Ordinal);
        foreach (McpServerDefinition server in configuration.Servers)
        {
            if (!ShouldDiscover(server))
            {
                continue;
            }

            McpToolsListResult discovery;
            try
            {
                discovery = discoverer.DiscoverTools(server, workspace, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                continue;
            }

            if (!discovery.Succeeded)
            {
                continue;
            }

            string localServerName = NormalizeNameSegment(server.Name, "server");
            foreach (McpDiscoveredTool discoveredTool in discovery.Tools)
            {
                string localRemoteToolName = NormalizeNameSegment(discoveredTool.Name, "tool");
                string localToolName = MakeUniqueName(
                    $"mcp.{localServerName}.{localRemoteToolName}",
                    usedLocalNames);
                tools.Add(new McpExternalTool(
                    server,
                    localToolName,
                    discoveredTool.Name,
                    CreateDescription(server, discoveredTool),
                    JsonSerializer.Serialize(discoveredTool.InputSchema),
                    invoker,
                    approvalPolicy));
            }
        }

        return tools;
    }

    public void RegisterTools(IToolRegistry registry, McpConfiguration configuration)
    {
        RegisterTools(
            registry,
            configuration,
            WorkspaceContext.Detect(Environment.CurrentDirectory, Environment.CurrentDirectory));
    }

    public void RegisterTools(
        IToolRegistry registry,
        McpConfiguration configuration,
        WorkspaceContext workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);

        foreach (ITool tool in CreateTools(configuration, workspace, cancellationToken))
        {
            registry.Register(tool);
        }
    }

    private static bool ShouldDiscover(McpServerDefinition server)
    {
        return server.Enabled &&
            string.Equals(server.Status, "configured", StringComparison.Ordinal) &&
            string.Equals(server.Transport, "stdio", StringComparison.Ordinal) &&
            string.Equals(server.Source, TrustedRegistrySource, StringComparison.Ordinal);
    }

    private static string CreateDescription(McpServerDefinition server, McpDiscoveredTool discoveredTool)
    {
        string context = $"MCP server '{server.Name}', tool '{discoveredTool.Name}'.";
        return string.IsNullOrWhiteSpace(discoveredTool.Description)
            ? $"Call {context}"
            : $"{discoveredTool.Description.Trim()} ({context})";
    }

    private static string MakeUniqueName(string baseName, HashSet<string> usedLocalNames)
    {
        if (usedLocalNames.Add(baseName))
        {
            return baseName;
        }

        for (int suffix = 2; ; suffix++)
        {
            string candidate = $"{baseName}_{suffix}";
            if (usedLocalNames.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static string NormalizeNameSegment(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        StringBuilder builder = new(value.Length);
        bool lastWasSeparator = false;
        foreach (char character in value.Trim().ToLowerInvariant())
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(character);
                lastWasSeparator = false;
                continue;
            }

            if (character is '_' or '-')
            {
                builder.Append(character);
                lastWasSeparator = false;
                continue;
            }

            if (!lastWasSeparator)
            {
                builder.Append('_');
                lastWasSeparator = true;
            }
        }

        string normalized = builder.ToString().Trim('_', '-');
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private sealed class UnavailableMcpToolDiscoverer : IMcpToolDiscoverer
    {
        public McpToolsListResult DiscoverTools(
            McpServerDefinition server,
            WorkspaceContext workspace,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return McpToolsListResult.Failure(
                "mcp-discovery-unavailable",
                "MCP tool discovery is not connected in this build.");
        }
    }
}
