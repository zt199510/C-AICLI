using System.Reflection;

namespace CSharpAiCli.Core;

public static class ProductInfo
{
    public const string CommandName = "caicli";
    public const string DisplayName = "C# AI CLI";
    public const string Description = "Local AI engineering CLI";
    public const string TargetFramework = "net9.0";
    public const string ReleaseRuntime = "win-x64";

    public static string Version => typeof(ProductInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "0.0.0";
}
