namespace CSharpAiCli.Core;

public sealed record McpStdioStartupCommandRiskInput(
    string Executable,
    IReadOnlyList<string> Arguments,
    McpStdioStartupCommandRiskInputKind Kind,
    string DetectorCommand)
{
    public static McpStdioStartupCommandRiskInput Create(McpStdioServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Create(options.Command, options.Args);
    }

    public static McpStdioStartupCommandRiskInput Create(string executable, IReadOnlyList<string>? arguments)
    {
        string[] argumentArray = arguments?.ToArray() ?? [];
        if (TryExtractShellCommandText(executable, argumentArray, out string shellCommandText))
        {
            return new McpStdioStartupCommandRiskInput(
                executable,
                argumentArray,
                McpStdioStartupCommandRiskInputKind.ShellCommandText,
                NormalizeLineBreaks(shellCommandText));
        }

        return new McpStdioStartupCommandRiskInput(
            executable,
            argumentArray,
            McpStdioStartupCommandRiskInputKind.DirectExecutable,
            FormatDirectExecutableRiskText(executable, argumentArray));
    }

    private static bool TryExtractShellCommandText(
        string executable,
        IReadOnlyList<string> arguments,
        out string shellCommandText)
    {
        string executableName = GetExecutableName(executable);
        if (executableName is "powershell" or "pwsh")
        {
            if (TryGetPowerShellEncodedExecutionSwitch(arguments, out string encodedSwitch))
            {
                shellCommandText = $"{executableName} {encodedSwitch}";
                return true;
            }

            return TryGetJoinedCommandAfterSwitch(
                arguments,
                static argument => IsPowerShellCommandSwitch(argument),
                out shellCommandText);
        }

        if (executableName is "cmd")
        {
            return TryGetJoinedCommandAfterSwitch(
                arguments,
                static argument => argument.Equals("/c", StringComparison.OrdinalIgnoreCase) ||
                    argument.Equals("/k", StringComparison.OrdinalIgnoreCase),
                out shellCommandText);
        }

        if (executableName is "sh" or "bash" or "zsh")
        {
            return TryGetSingleCommandAfterSwitch(
                arguments,
                static argument => IsPosixShellCommandSwitch(argument),
                out shellCommandText);
        }

        shellCommandText = string.Empty;
        return false;
    }

    private static bool TryGetJoinedCommandAfterSwitch(
        IReadOnlyList<string> arguments,
        Func<string, bool> isCommandSwitch,
        out string commandText)
    {
        for (int index = 0; index < arguments.Count; index++)
        {
            if (!isCommandSwitch(arguments[index]))
            {
                continue;
            }

            commandText = string.Join(" ", arguments.Skip(index + 1));
            return true;
        }

        commandText = string.Empty;
        return false;
    }

    private static bool TryGetSingleCommandAfterSwitch(
        IReadOnlyList<string> arguments,
        Func<string, bool> isCommandSwitch,
        out string commandText)
    {
        for (int index = 0; index < arguments.Count; index++)
        {
            if (!isCommandSwitch(arguments[index]))
            {
                continue;
            }

            commandText = index + 1 < arguments.Count ? arguments[index + 1] : string.Empty;
            return true;
        }

        commandText = string.Empty;
        return false;
    }

    private static string FormatDirectExecutableRiskText(string executable, IReadOnlyList<string> arguments)
    {
        if (!DirectExecutableNeedsArguments(executable))
        {
            return NormalizeLineBreaks(executable);
        }

        List<string> parts = [FormatPart(executable)];
        foreach (string argument in arguments)
        {
            parts.Add(FormatPart(argument));
        }

        return NormalizeLineBreaks(string.Join(" ", parts));
    }

    private static bool DirectExecutableNeedsArguments(string executable)
    {
        return GetExecutableName(executable) is "rm" or "del" or "rmdir" or "remove-item";
    }

    private static bool IsPowerShellCommandSwitch(string argument)
    {
        return argument.Equals("-Command", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("-c", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetPowerShellEncodedExecutionSwitch(
        IReadOnlyList<string> arguments,
        out string encodedSwitch)
    {
        foreach (string argument in arguments)
        {
            if (!IsPowerShellEncodedExecutionSwitch(argument))
            {
                continue;
            }

            encodedSwitch = "-EncodedCommand";
            return true;
        }

        encodedSwitch = string.Empty;
        return false;
    }

    private static bool IsPowerShellEncodedExecutionSwitch(string argument)
    {
        return argument.Equals("-EncodedCommand", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("-EncodedArguments", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("-enc", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("-e", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPosixShellCommandSwitch(string argument)
    {
        return argument.Equals("-c", StringComparison.Ordinal) ||
            (argument.StartsWith("-", StringComparison.Ordinal) &&
                !argument.StartsWith("--", StringComparison.Ordinal) &&
                argument.Contains('c', StringComparison.Ordinal));
    }

    private static string GetExecutableName(string executable)
    {
        string trimmed = executable.Trim().Trim('"');
        string fileName = Path.GetFileName(trimmed);
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = trimmed;
        }

        return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^4].ToLowerInvariant()
            : fileName.ToLowerInvariant();
    }

    private static string NormalizeLineBreaks(string value)
    {
        return value.Replace('\r', ' ').Replace('\n', ' ');
    }

    private static string FormatPart(string value)
    {
        return value.Length == 0 ? "\"\"" : value;
    }
}

public enum McpStdioStartupCommandRiskInputKind
{
    DirectExecutable,
    ShellCommandText
}
