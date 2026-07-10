using System.Diagnostics;

namespace CSharpAiCli.Core;

internal sealed class DiagnosticDurationClock
{
    private readonly Func<long> timestampProvider;

    public DiagnosticDurationClock(Func<long>? timestampProvider = null)
    {
        this.timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
    }

    public long GetTimestamp()
    {
        return timestampProvider();
    }

    public long GetElapsedMilliseconds(long startedTimestamp)
    {
        long completedTimestamp = timestampProvider();
        if (completedTimestamp <= startedTimestamp)
        {
            return 0;
        }

        return Math.Max(
            0,
            (long)Stopwatch.GetElapsedTime(startedTimestamp, completedTimestamp).TotalMilliseconds);
    }
}
