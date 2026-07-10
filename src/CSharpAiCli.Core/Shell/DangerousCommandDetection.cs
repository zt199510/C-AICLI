namespace CSharpAiCli.Core;

public sealed record DangerousCommandDetection(
    bool IsDangerous,
    string Reason,
    string MatchedRule)
{
    public static DangerousCommandDetection Safe { get; } = new(
        IsDangerous: false,
        Reason: string.Empty,
        MatchedRule: string.Empty);
}
