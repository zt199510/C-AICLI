using System.Text.RegularExpressions;

namespace CSharpAiCli.Core;

public static partial class DangerousCommandDetector
{
    public static bool IsDangerous(string command, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(command))
        {
            reason = "Command is required.";
            return true;
        }

        string normalized = command.Trim();
        if (DeletePattern().IsMatch(normalized))
        {
            reason = "Command contains a destructive delete pattern.";
            return true;
        }

        if (FormatPattern().IsMatch(normalized))
        {
            reason = "Command contains a destructive format pattern.";
            return true;
        }

        if (PermissionPattern().IsMatch(normalized))
        {
            reason = "Command contains a permission modification pattern.";
            return true;
        }

        if (DownloadExecutePattern().IsMatch(normalized))
        {
            reason = "Command appears to download and execute remote content.";
            return true;
        }

        if (BackgroundPattern().IsMatch(normalized))
        {
            reason = "Command appears to start a background process.";
            return true;
        }

        if (InfiniteLoopPattern().IsMatch(normalized))
        {
            reason = "Command contains an infinite loop pattern.";
            return true;
        }

        return false;
    }

    [GeneratedRegex(@"(?i)(^|\s)(rm\s+-rf|del\s+/[a-z]*[sq]|rmdir\s+/[a-z]*s|remove-item\s+.*(-recurse|-r)\b)")]
    private static partial Regex DeletePattern();

    [GeneratedRegex(@"(?i)(^|\s)(format|diskpart)\b")]
    private static partial Regex FormatPattern();

    [GeneratedRegex(@"(?i)(^|\s)(chmod|chown|icacls|takeown)\b")]
    private static partial Regex PermissionPattern();

    [GeneratedRegex(@"(?i)(curl|wget|invoke-webrequest|iwr).*(\|\s*|;\s*|&&\s*)(sh|bash|powershell|pwsh|cmd|python)\b")]
    private static partial Regex DownloadExecutePattern();

    [GeneratedRegex(@"(?i)(start-process|start-job|nohup|setsid|\s&\s*$)")]
    private static partial Regex BackgroundPattern();

    [GeneratedRegex(@"(?i)(while\s*\(\s*\$?true\s*\)|while\s+true|for\s*\(\s*;\s*;\s*\))")]
    private static partial Regex InfiniteLoopPattern();
}
