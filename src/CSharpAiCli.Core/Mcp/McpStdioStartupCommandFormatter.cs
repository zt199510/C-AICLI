namespace CSharpAiCli.Core;

public static class McpStdioStartupCommandFormatter
{
    public static string FormatForRiskDetection(McpStdioServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return FormatForRiskDetection(options.Command, options.Args);
    }

    public static string FormatForRiskDetection(string command, IReadOnlyList<string>? arguments)
    {
        List<string> parts = [FormatPart(command)];
        if (arguments is not null)
        {
            foreach (string argument in arguments)
            {
                parts.Add(FormatPart(argument));
            }
        }

        return string.Join(" ", parts);
    }

    private static string FormatPart(string value)
    {
        return value.Length == 0 ? "\"\"" : value;
    }
}
