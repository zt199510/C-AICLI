namespace CSharpAiCli.Core;

public sealed record AgentRunLimits
{
    public const int DefaultMaxTurns = 8;
    public const int DefaultMaxToolCalls = 32;
    public static readonly TimeSpan DefaultModelCallTimeout = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan DefaultOverallTimeout = TimeSpan.FromMinutes(10);

    public AgentRunLimits(
        int? MaxTurns = null,
        int? MaxToolCalls = null,
        TimeSpan? ModelCallTimeout = null,
        TimeSpan? OverallTimeout = null)
    {
        if (MaxTurns is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTurns), "Max turns must be greater than zero.");
        }

        if (MaxToolCalls is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxToolCalls), "Max tool calls must be greater than zero.");
        }

        if (ModelCallTimeout is { } modelCallTimeout && modelCallTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ModelCallTimeout), "Model call timeout must be greater than zero.");
        }

        if (OverallTimeout is { } overallTimeout && overallTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(OverallTimeout), "Overall timeout must be greater than zero.");
        }

        this.MaxTurns = MaxTurns ?? DefaultMaxTurns;
        this.MaxToolCalls = MaxToolCalls ?? DefaultMaxToolCalls;
        this.ModelCallTimeout = ModelCallTimeout ?? DefaultModelCallTimeout;
        this.OverallTimeout = OverallTimeout ?? DefaultOverallTimeout;
    }

    public int MaxTurns { get; }

    public int MaxToolCalls { get; }

    public TimeSpan ModelCallTimeout { get; }

    public TimeSpan OverallTimeout { get; }

    public static AgentRunLimits Default { get; } = new();
}
