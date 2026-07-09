namespace CSharpAiCli.Core;

public sealed record ShellPolicyConfiguration(
    IReadOnlyList<string> AllowedCommands,
    bool AllowedCommandsConfigured,
    string AllowedCommandsSource,
    IReadOnlyList<string> DeniedCommands,
    int? MaxTimeoutMilliseconds,
    string MaxTimeoutMillisecondsSource)
{
    public static ShellPolicyConfiguration Default { get; } = new(
        AllowedCommands: [],
        AllowedCommandsConfigured: false,
        AllowedCommandsSource: "default",
        DeniedCommands: [],
        MaxTimeoutMilliseconds: null,
        MaxTimeoutMillisecondsSource: "default");
}
