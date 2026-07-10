namespace CSharpAiCli.Core;

internal static class ShellCommandPolicy
{
    public static ShellPolicyDecision Evaluate(
        ShellPolicyConfiguration policy,
        ShellCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(request);

        string command = request.Command.Trim();

        foreach (string deniedCommand in policy.DeniedCommands)
        {
            string denied = deniedCommand.Trim();
            if (denied.Length == 0)
            {
                continue;
            }

            if (ContainsBoundaryAwareFragment(command, denied))
            {
                return ShellPolicyDecision.Denied(
                    "denylist",
                    $"Shell command denied by configured shell policy: command contains denied entry '{denied}'.",
                    denied,
                    "configured shell policy");
            }
        }

        if (policy.MaxTimeoutMilliseconds is int maxTimeoutMilliseconds &&
            request.TimeoutMilliseconds > maxTimeoutMilliseconds)
        {
            return ShellPolicyDecision.Denied(
                "timeout",
                $"Shell command timeout {request.TimeoutMilliseconds}ms exceeds configured max {maxTimeoutMilliseconds}ms from {policy.MaxTimeoutMillisecondsSource}.",
                null,
                policy.MaxTimeoutMillisecondsSource,
                maxTimeoutMilliseconds);
        }

        if (!policy.AllowedCommandsConfigured)
        {
            return ShellPolicyDecision.Allow;
        }

        if (policy.AllowedCommands.Count == 0)
        {
            return ShellPolicyDecision.Denied(
                "allowlist",
                $"Shell command not allowed by configured shell policy: allowlist is configured but empty ({policy.AllowedCommandsSource}).",
                null,
                policy.AllowedCommandsSource);
        }

        foreach (string allowedCommand in policy.AllowedCommands)
        {
            string allowed = allowedCommand.Trim();
            if (allowed.Length == 0)
            {
                continue;
            }

            if (string.Equals(command, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return ShellPolicyDecision.Allow;
            }

            if (IsAllowedPrefixMatch(command, allowed))
            {
                string remainder = command[allowed.Length..];
                if (ContainsShellSyntax(remainder))
                {
                    return ShellPolicyDecision.Denied(
                        "allowlist-shell-syntax",
                        $"Shell command not allowed by configured shell policy: allowlisted prefix '{allowed}' is followed by shell syntax.",
                        null,
                        policy.AllowedCommandsSource);
                }

                return ShellPolicyDecision.Allow;
            }
        }

        return ShellPolicyDecision.Denied(
            "allowlist",
            $"Shell command not allowed by configured shell policy: command does not match allowlist from {policy.AllowedCommandsSource}.",
            null,
            policy.AllowedCommandsSource);
    }

    private static bool IsAllowedPrefixMatch(string command, string allowed)
    {
        return command.Length > allowed.Length &&
            char.IsWhiteSpace(command[allowed.Length]) &&
            command.StartsWith(allowed, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsShellSyntax(string text)
    {
        return text.Contains("&&", StringComparison.Ordinal) ||
            text.Contains("||", StringComparison.Ordinal) ||
            text.Contains("$(", StringComparison.Ordinal) ||
            text.IndexOfAny(['&', ';', '|', '\n', '\r', '<', '>', '`', '(', ')']) >= 0;
    }

    private static bool ContainsBoundaryAwareFragment(string command, string denied)
    {
        int index = 0;
        while ((index = command.IndexOf(denied, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int endIndex = index + denied.Length;
            if (IsFragmentBoundary(command, index - 1) &&
                IsFragmentBoundary(command, endIndex))
            {
                return true;
            }

            index++;
        }

        return false;
    }

    private static bool IsFragmentBoundary(string text, int index)
    {
        return index < 0 ||
            index >= text.Length ||
            !IsWordCharacter(text[index]);
    }

    private static bool IsWordCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_';
    }
}

internal sealed record ShellPolicyDecision(
    bool Allowed,
    string Reason,
    string SafeMessage,
    string? MatchedEntry,
    string? Source,
    int? MaxTimeoutMilliseconds)
{
    public static ShellPolicyDecision Allow { get; } = new(
        Allowed: true,
        Reason: "allowed",
        SafeMessage: "Shell command allowed by configured shell policy.",
        MatchedEntry: null,
        Source: null,
        MaxTimeoutMilliseconds: null);

    public static ShellPolicyDecision Denied(
        string reason,
        string safeMessage,
        string? matchedEntry,
        string? source,
        int? maxTimeoutMilliseconds = null)
    {
        return new ShellPolicyDecision(
            Allowed: false,
            Reason: reason,
            SafeMessage: safeMessage,
            MatchedEntry: matchedEntry,
            Source: source,
            MaxTimeoutMilliseconds: maxTimeoutMilliseconds);
    }
}
