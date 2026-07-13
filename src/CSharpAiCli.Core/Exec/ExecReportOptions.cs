namespace CSharpAiCli.Core;

public sealed record ExecReportOptions(
    ExecReportMode Mode = ExecReportMode.None,
    string? ReportPath = null)
{
    public bool ShouldGenerate => Mode == ExecReportMode.Markdown;

    public bool ShouldWriteFile => ShouldGenerate && !string.IsNullOrWhiteSpace(ReportPath);
}

public sealed record ReportWriteResult(
    bool Succeeded,
    string? Path,
    string? ErrorCode = null,
    string? Summary = null)
{
    public static ReportWriteResult Success(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new ReportWriteResult(true, path, Summary: "Markdown report written.");
    }

    public static ReportWriteResult Failure(string errorCode, string summary, string? path = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        return new ReportWriteResult(false, path, errorCode, summary);
    }
}

public sealed record ExecReportMetadata(
    string Mode,
    bool Generated,
    string? Path = null,
    string? WriteStatus = null,
    string? ErrorCode = null,
    string? Summary = null);

