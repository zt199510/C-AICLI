namespace CSharpAiCli.Core;

public sealed record AgentRunLimits
{
    public const int DefaultMaxSteps = 8;
    public const int DefaultMaxTurns = 8;
    public const int DefaultMaxToolCalls = 32;
    public static readonly TimeSpan DefaultModelCallTimeout = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan DefaultOverallTimeout = TimeSpan.FromMinutes(10);

    public AgentRunLimits(
        int? MaxTurns = null,
        int? MaxToolCalls = null,
        TimeSpan? ModelCallTimeout = null,
        TimeSpan? OverallTimeout = null,
        int? MaxSteps = null)
    {
        if (MaxTurns is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTurns), "Max turns must be greater than zero.");
        }

        if (MaxSteps is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSteps), "Max steps must be greater than zero.");
        }

        if (MaxSteps.HasValue && MaxTurns.HasValue && MaxSteps.Value != MaxTurns.Value)
        {
            throw new ArgumentException("Max steps and max turns must match when both are provided.");
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

        int? effectiveMaxSteps = MaxSteps ?? MaxTurns;
        MaxStepsOverride = effectiveMaxSteps;
        MaxTurnsOverride = effectiveMaxSteps;
        MaxToolCallsOverride = MaxToolCalls;
        ModelCallTimeoutOverride = ModelCallTimeout;
        OverallTimeoutOverride = OverallTimeout;
        this.MaxSteps = effectiveMaxSteps ?? DefaultMaxSteps;
        this.MaxTurns = this.MaxSteps;
        this.MaxToolCalls = MaxToolCalls ?? DefaultMaxToolCalls;
        this.ModelCallTimeout = ModelCallTimeout ?? DefaultModelCallTimeout;
        this.OverallTimeout = OverallTimeout ?? DefaultOverallTimeout;
    }

    public int? MaxStepsOverride { get; }

    public int? MaxTurnsOverride { get; }

    public int? MaxToolCallsOverride { get; }

    public TimeSpan? ModelCallTimeoutOverride { get; }

    public TimeSpan? OverallTimeoutOverride { get; }

    public int MaxSteps { get; }

    public int MaxTurns { get; }

    public int MaxToolCalls { get; }

    public TimeSpan ModelCallTimeout { get; }

    public TimeSpan OverallTimeout { get; }

    public static AgentRunLimits Default { get; } = new();

    public AgentRunLimits MergeWith(AgentRunLimits defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);

        return new AgentRunLimits(
            MaxSteps: MaxStepsOverride ?? defaults.MaxSteps,
            MaxToolCalls: MaxToolCallsOverride ?? defaults.MaxToolCalls,
            ModelCallTimeout: ModelCallTimeoutOverride ?? defaults.ModelCallTimeout,
            OverallTimeout: OverallTimeoutOverride ?? defaults.OverallTimeout);
    }
}
