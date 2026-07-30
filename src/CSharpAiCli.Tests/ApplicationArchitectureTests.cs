using System.Reflection;
using System.Xml.Linq;
using CSharpAiCli.Application;

namespace CSharpAiCli.Tests;

public sealed class ApplicationArchitectureTests
{
    [Fact]
    public void Project_references_follow_the_application_dependency_direction()
    {
        string root = FindRepositoryRoot();
        string[] application = ProjectReferences(Path.Combine(
            root,
            "src",
            "CSharpAiCli.Application",
            "CSharpAiCli.Application.csproj"));
        string[] appHost = ProjectReferences(Path.Combine(
            root,
            "src",
            "CSharpAiCli.AppHost",
            "CSharpAiCli.AppHost.csproj"));
        string[] cli = ProjectReferences(Path.Combine(
            root,
            "src",
            "CSharpAiCli.Cli",
            "CSharpAiCli.Cli.csproj"));

        Assert.Equal(
            ["CSharpAiCli.Core", "CSharpAiCli.ProjectPacks"],
            application.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["CSharpAiCli.Application", "CSharpAiCli.Core"],
            appHost.Order(StringComparer.Ordinal));
        Assert.Contains("CSharpAiCli.Application", cli);
        Assert.DoesNotContain("CSharpAiCli.Cli", appHost);
    }

    [Fact]
    public void Public_application_contract_does_not_expose_cli_renderer_or_store_types()
    {
        Assembly assembly = typeof(ApplicationResult<>).Assembly;
        string[] prohibitedNamespaces = ["System.CommandLine", "CSharpAiCli.Cli"];
        string[] prohibitedTypeNames =
        [
            "System.IO.TextWriter",
            "System.IO.FileStream",
            "System.Text.Json.JsonDocument",
            "CSharpAiCli.Core.IConversationStore",
            "CSharpAiCli.Core.JobRecordStore",
            "CSharpAiCli.Core.ThreadStore",
            "CSharpAiCli.Core.ThreadRecord",
            "CSharpAiCli.Core.TurnRecord",
            "CSharpAiCli.Core.TimelineItemRecord",
            "CSharpAiCli.ProjectPacks.Runtime.ManagedArtifactStore"
        ];

        IEnumerable<Type> exposedTypes = assembly.GetExportedTypes().SelectMany(type =>
            new[] { type }
                .Concat(type.GetConstructors().SelectMany(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType)))
                .Concat(type.GetMethods().SelectMany(method =>
                    method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))));
        string[] exposedNames = exposedTypes.SelectMany(FlattenType)
            .Select(type => type.FullName ?? type.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.DoesNotContain(exposedNames, name => prohibitedNamespaces.Any(prefix =>
            name.StartsWith(prefix, StringComparison.Ordinal)));
        Assert.DoesNotContain(exposedNames, name => prohibitedTypeNames.Contains(name, StringComparer.Ordinal));
    }

    [Fact]
    public void Week_69_protocol_dispatch_preserves_application_and_desktop_boundaries()
    {
        string root = FindRepositoryRoot();
        string contract = File.ReadAllText(Path.Combine(root, "protocol", "desktop-v1", "contract.json"));
        string[] appHostFiles = Directory.EnumerateFiles(
            Path.Combine(root, "src", "CSharpAiCli.AppHost"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Generated{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .ToArray();
        string appHost = string.Join('\n', appHostFiles.Select(File.ReadAllText));
        string cliCommands = ReadCliCommandSources(root);
        string desktopBusinessSurface = string.Join('\n', Directory.EnumerateFiles(
                Path.Combine(root, "apps", "desktop", "src"), "*.ts*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}generated{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText));

        Assert.Contains("thread.list", contract, StringComparison.Ordinal);
        Assert.Contains("thread.changed", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("ThreadStore", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("CSharpAiCli.ProjectPacks", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("CSharpAiCli.Cli", appHost, StringComparison.Ordinal);
        Assert.Contains("DesktopAgentTurnExecutionRuntime", appHost, StringComparison.Ordinal);
        Assert.Contains("BuiltInToolRegistryFactory", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("new DeterministicFakeTurnExecutionRuntime", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain(".caicli/threads", appHost, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Console.Write", appHost, StringComparison.Ordinal);
        Assert.Contains("Console.Error.WriteLine", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("ThreadStore", cliCommands, StringComparison.Ordinal);
        Assert.DoesNotContain("ThreadApplicationService", cliCommands, StringComparison.Ordinal);
        foreach (string method in new[]
        {
            "thread.list", "thread.get", "thread.create", "thread.rename", "thread.archive", "thread.delete",
            "catalog.list", "changes.get", "report.list", "report.get", "artifact.list", "artifact.get"
        })
        {
            Assert.DoesNotContain(method, desktopBusinessSurface, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Cli_command_modules_preserve_surface_boundaries()
    {
        string root = FindRepositoryRoot();
        string source = ReadCliCommandSources(root);

        Assert.DoesNotContain("CSharpAiCli.AppHost", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Electron", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apps/desktop", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("public static class CliCommandFactory", source, StringComparison.Ordinal);
        Assert.Contains("interface ICliCommandModule", source, StringComparison.Ordinal);
        Assert.Contains("sealed class CliRootComposer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Changes_cli_handler_delegates_business_orchestration_to_application()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpAiCli.Cli",
            "Commands",
            "CliCommandFactory.cs"));
        int start = source.IndexOf("Command changesCommand", StringComparison.Ordinal);
        int end = source.IndexOf("Command jobsCommand", start, StringComparison.Ordinal);
        string handler = source[start..end];

        Assert.Contains("changesServiceFactory", handler, StringComparison.Ordinal);
        Assert.Contains("ChangesQueryRequest", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("new GitStatusTool", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("new GitDiffTool", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("conversationStoreFactory(", handler, StringComparison.Ordinal);
    }

    private static IEnumerable<Type> FlattenType(Type type)
    {
        yield return type;
        if (type.HasElementType && type.GetElementType() is Type element)
        {
            foreach (Type nested in FlattenType(element))
            {
                yield return nested;
            }
        }

        foreach (Type argument in type.GetGenericArguments())
        {
            foreach (Type nested in FlattenType(argument))
            {
                yield return nested;
            }
        }
    }

    private static string[] ProjectReferences(string projectPath)
    {
        XDocument project = XDocument.Load(projectPath);
        return project.Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFileNameWithoutExtension(value!))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string ReadCliCommandSources(string root)
    {
        string commandsDirectory = Path.Combine(root, "src", "CSharpAiCli.Cli", "Commands");
        return string.Join(
            '\n',
            Directory.EnumerateFiles(commandsDirectory, "*.cs", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
