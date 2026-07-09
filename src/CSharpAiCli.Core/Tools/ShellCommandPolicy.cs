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

            if (command.Contains(denied, StringComparison.OrdinalIgnoreCase))
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

            if (IsAllowedCommandMatch(command, allowed))
            {
                return ShellPolicyDecision.Allow;
            }
        }

        return ShellPolicyDecision.Denied(
            "allowlist",
            $"Shell command not allowed by configured shell policy: command does not match allowlist from {policy.AllowedCommandsSource}.",
            null,
            policy.AllowedCommandsSource);
    }

    private static bool IsAllowedCommandMatch(string command, string allowed)
    {
        if (string.Equals(command, allowed, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return command.Length > allowed.Length &&
            char.IsWhiteSpace(command[allowed.Length]) &&
            command.StartsWith(allowed, StringComparison.OrdinalIgnoreCase);
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
