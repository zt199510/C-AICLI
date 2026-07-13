namespace CSharpAiCli.Core;

public enum ExecReportMode
{
    None,
    Markdown
}

public static class ExecReportModeParser
{
    public static bool TryParse(string? value, out ExecReportMode mode)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
        {
            mode = ExecReportMode.None;
            return true;
        }

        if (string.Equals(value, "markdown", StringComparison.OrdinalIgnoreCase))
        {
            mode = ExecReportMode.Markdown;
            return true;
        }

        mode = ExecReportMode.None;
        return false;
    }

    public static string ToCanonicalName(this ExecReportMode mode)
    {
        return mode switch
        {
            ExecReportMode.None => "none",
            ExecReportMode.Markdown => "markdown",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown report mode.")
        };
    }
}

