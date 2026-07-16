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
        Assert.Equal(["CSharpAiCli.Application"], appHost);
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
            "CSharpAiCli.Core.IConversationStore",
            "CSharpAiCli.Core.JobRecordStore",
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
