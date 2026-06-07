namespace CSharpAiCli.Core;

public sealed record CliConfigFileSource(
    string SourceName,
    string Path,
    CliConfigFile Config);
