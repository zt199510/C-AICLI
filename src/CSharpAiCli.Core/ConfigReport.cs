namespace CSharpAiCli.Core;

public sealed record ConfigReport(IReadOnlyList<string> Lines)
{
    public static ConfigReport Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string apiKeyStatus = snapshot.HasOpenAiApiKey ? "present" : "missing";

        return new ConfigReport(
        [
            $"{ProductInfo.DisplayName} effective configuration",
            $"workspace: {snapshot.CurrentDirectory}",
            $"userConfigPath: {snapshot.UserConfigPath}",
            $"workspaceConfigPath: {snapshot.WorkspaceConfigPath}",
            "model: not configured",
            $"apiKey: {apiKeyStatus}"
        ]);
    }

    public string ToDisplayText()
    {
        return string.Join(Environment.NewLine, Lines);
    }
}
