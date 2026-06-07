namespace CSharpAiCli.ProjectPacks.GerberTiff;

public sealed record GerberTiffStatusReport(
    string WorkspaceRoot,
    string PlansDirectory,
    string Status,
    IReadOnlyList<string> PlanFiles);
