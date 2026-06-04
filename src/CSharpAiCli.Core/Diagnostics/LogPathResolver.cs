namespace CSharpAiCli.Core;

public static class LogPathResolver
{
    public static string ResolveLogDirectory(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Workspace.IsUsable)
        {
            return Path.Combine(snapshot.Workspace.RootPath, ".caicli", "logs");
        }

        string? userConfigDirectory = Path.GetDirectoryName(snapshot.UserConfigPath);
        return Path.Combine(userConfigDirectory ?? snapshot.Workspace.RootPath, "logs");
    }
}
