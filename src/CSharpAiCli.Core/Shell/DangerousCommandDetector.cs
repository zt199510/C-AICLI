using System.Text.RegularExpressions;

namespace CSharpAiCli.Core;

public static partial class DangerousCommandDetector
{
    public static bool IsDangerous(string command, out string reason)
    {
        DangerousCommandDetection detection = Detect(command);
        reason = detection.Reason;
        return detection.IsDangerous;
    }

    public static DangerousCommandDetection Detect(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return Dangerous("Command is required.", "empty command");
        }

        string normalized = command.Trim();
        if (DeletePattern().IsMatch(normalized))
        {
            return Dangerous("Command contains a destructive delete pattern.", "destructive delete pattern");
        }

        if (FormatPattern().IsMatch(normalized))
        {
            return Dangerous("Command contains a destructive format pattern.", "destructive format pattern");
        }

        if (PermissionPattern().IsMatch(normalized))
        {
            return Dangerous("Command contains a permission modification pattern.", "permission modification pattern");
        }

        if (EncodedPowerShellPattern().IsMatch(normalized))
        {
            return Dangerous("Command contains opaque encoded PowerShell execution.", "encoded powershell command");
        }

        if (DownloadExecutePattern().IsMatch(normalized))
        {
            return Dangerous("Command appears to download and execute remote content.", "download and execute remote content");
        }

        if (BackgroundPattern().IsMatch(normalized))
        {
            return Dangerous("Command appears to start a background process.", "background process pattern");
        }

        if (InfiniteLoopPattern().IsMatch(normalized))
        {
            return Dangerous("Command contains an infinite loop pattern.", "infinite loop pattern");
        }

        return DangerousCommandDetection.Safe;
    }

    private static DangerousCommandDetection Dangerous(string reason, string matchedRule)
    {
        return new DangerousCommandDetection(
            IsDangerous: true,
            Reason: reason,
            MatchedRule: matchedRule);
    }

    [GeneratedRegex(@"(?i)(^|\s)(rm\s+-rf|del\s+/[a-z]*[sq]|rmdir\s+/[a-z]*s|remove-item\s+.*(-recurse|-r)\b)")]
    private static partial Regex DeletePattern();

    [GeneratedRegex(@"(?i)(^|\s)(format|diskpart)\b")]
    private static partial Regex FormatPattern();

    [GeneratedRegex(@"(?i)(^|\s)(chmod|chown|icacls|takeown)\b")]
    private static partial Regex PermissionPattern();

    [GeneratedRegex(@"(?i)(^|\s)(powershell|pwsh)(\.exe)?\b[\s\S]*\s-(encodedcommand|encodedarguments|enc|e)\b")]
    private static partial Regex EncodedPowerShellPattern();

    [GeneratedRegex(@"(?i)(curl|wget|invoke-webrequest|iwr).*(\|\s*|;\s*|&&\s*)(sh|bash|powershell|pwsh|cmd|python)\b")]
    private static partial Regex DownloadExecutePattern();

    [GeneratedRegex(@"(?i)(start-process|start-job|nohup|setsid|\s&\s*$)")]
    private static partial Regex BackgroundPattern();

    [GeneratedRegex(@"(?i)(while\s*\(\s*\$?true\s*\)|while\s+true|for\s*\(\s*;\s*;\s*\))")]
    private static partial Regex InfiniteLoopPattern();
}
