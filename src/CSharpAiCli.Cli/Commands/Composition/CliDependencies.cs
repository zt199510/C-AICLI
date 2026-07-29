using CSharpAiCli.Application;
using CSharpAiCli.Core;

namespace CSharpAiCli.Cli;

internal sealed class CliDependencies
{
    public CliDependencies(
        Func<string?, string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger,
        Func<CliEnvironmentSnapshot, IChatModelClient> chatModelClientFactory,
        Func<TextWriter, IChatStreamingRenderer> streamingRendererFactory,
        Func<CliEnvironmentSnapshot, IConversationStore> conversationStoreFactory,
        Func<DateTimeOffset> utcNowProvider,
        Func<CliEnvironmentSnapshot, ToolRegistry, IToolExecutor, IAgentRunner> execAgentRunnerFactory,
        Func<string, string?> environmentVariableProvider)
    {
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        ArgumentNullException.ThrowIfNull(commandLogger);
        ArgumentNullException.ThrowIfNull(chatModelClientFactory);
        ArgumentNullException.ThrowIfNull(streamingRendererFactory);
        ArgumentNullException.ThrowIfNull(conversationStoreFactory);
        ArgumentNullException.ThrowIfNull(utcNowProvider);
        ArgumentNullException.ThrowIfNull(execAgentRunnerFactory);
        ArgumentNullException.ThrowIfNull(environmentVariableProvider);

        SnapshotProvider = snapshotProvider;
        CommandLogger = commandLogger;
        ChatModelClientFactory = chatModelClientFactory;
        StreamingRendererFactory = streamingRendererFactory;
        ConversationStoreFactory = conversationStoreFactory;
        UtcNowProvider = utcNowProvider;
        ExecAgentRunnerFactory = execAgentRunnerFactory;
        EnvironmentVariableProvider = environmentVariableProvider;
    }

    public Func<string?, string?, CliEnvironmentSnapshot> SnapshotProvider { get; }

    public Action<string, CliEnvironmentSnapshot> CommandLogger { get; }

    public Func<CliEnvironmentSnapshot, IChatModelClient> ChatModelClientFactory { get; }

    public Func<TextWriter, IChatStreamingRenderer> StreamingRendererFactory { get; }

    public Func<CliEnvironmentSnapshot, IConversationStore> ConversationStoreFactory { get; }

    public Func<DateTimeOffset> UtcNowProvider { get; }

    public Func<CliEnvironmentSnapshot, ToolRegistry, IToolExecutor, IAgentRunner> ExecAgentRunnerFactory { get; }

    public Func<string, string?> EnvironmentVariableProvider { get; }
}
