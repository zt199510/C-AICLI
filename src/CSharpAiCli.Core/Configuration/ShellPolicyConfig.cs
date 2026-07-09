namespace CSharpAiCli.Core;

public sealed class ShellPolicyConfig
{
    public string[]? AllowedCommands { get; init; }

    public string[]? DeniedCommands { get; init; }

    public int? MaxTimeoutMilliseconds { get; init; }
}
