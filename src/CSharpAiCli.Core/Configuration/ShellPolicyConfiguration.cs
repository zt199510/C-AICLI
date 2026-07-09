namespace CSharpAiCli.Core;

public sealed record ShellPolicyConfiguration(
    IReadOnlyList<string> AllowedCommands,
    IReadOnlyList<string> DeniedCommands,
    int? MaxTimeoutMilliseconds,
    string MaxTimeoutMillisecondsSource)
{
    public static ShellPolicyConfiguration Default { get; } = new(
        AllowedCommands: [],
        DeniedCommands: [],
        MaxTimeoutMilliseconds: null,
        MaxTimeoutMillisecondsSource: "default");
}
