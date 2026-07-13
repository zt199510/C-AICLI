using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class WorkflowReferenceResolverTests
{
    [Fact]
    public void Parser_extracts_inline_file_and_folder_tokens()
    {
        WorkflowReferenceParser parser = new();

        IReadOnlyList<WorkflowReferenceToken> tokens = parser.Parse(
            """Fix @file:src/App.cs, then inspect @folder:"tests/fixtures".""");

        Assert.Equal(2, tokens.Count);
        Assert.Equal("file", tokens[0].Kind);
        Assert.Equal("src/App.cs", tokens[0].RequestedPath);
        Assert.Equal("@file:src/App.cs", tokens[0].SourceToken);
        Assert.Equal("folder", tokens[1].Kind);
        Assert.Equal("tests/fixtures", tokens[1].RequestedPath);
    }

    [Fact]
    public void Resolve_file_truncates_large_text_and_records_warning()
    {
        using TempDirectory temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "hello world");
        WorkflowReferenceResolver resolver = new(
            new WorkspaceGuard(),
            options: new WorkflowReferenceResolverOptions { MaxFileBytes = 5 });

        WorkflowReferenceResolution result = resolver.Resolve(
            CreateWorkspace(temp.Path),
            "summarize @file:note.txt");

        WorkflowReferenceEntry reference = Assert.Single(result.References);
        Assert.Equal("warning", reference.Status);
        WorkflowReferenceFile file = Assert.Single(reference.Files);
        Assert.Equal("note.txt", file.Path);
        Assert.Equal("hello", file.Content);
        Assert.True(file.Truncated);
        Assert.Contains(reference.Warnings, warning => warning.ErrorCode == WorkflowReferenceErrorCode.TooLarge);
    }

    [Fact]
    public void Resolve_file_outside_workspace_returns_boundary_error()
    {
        using TempDirectory temp = TempDirectory.Create();
        WorkflowReferenceResolver resolver = new();

        WorkflowReferenceResolution result = resolver.Resolve(
            CreateWorkspace(temp.Path),
            "read @file:../outside.txt");

        WorkflowReferenceEntry reference = Assert.Single(result.References);
        Assert.True(result.HasErrors);
        Assert.Equal("failure", reference.Status);
        Assert.Equal(WorkflowReferenceErrorCode.BoundaryDenied, reference.ErrorCode);
        Assert.Empty(reference.Files);
    }

    [Fact]
    public void Resolve_folder_applies_bounds_and_skips_ignored_or_binary_children()
    {
        using TempDirectory temp = TempDirectory.Create();
        string src = Path.Combine(temp.Path, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "a.txt"), "alpha");
        Directory.CreateDirectory(Path.Combine(src, "bin"));
        File.WriteAllText(Path.Combine(src, "bin", "skip.txt"), "skip");
        File.WriteAllBytes(Path.Combine(src, "image.bin"), [0, 1, 2, 3]);
        WorkflowReferenceResolver resolver = new(
            new WorkspaceGuard(),
            options: new WorkflowReferenceResolverOptions
            {
                MaxFolderFiles = 10,
                MaxFolderBytes = 100,
                MaxFolderDepth = 2
            });

        WorkflowReferenceResolution result = resolver.Resolve(
            CreateWorkspace(temp.Path),
            "review @folder:src");

        WorkflowReferenceEntry reference = Assert.Single(result.References);
        Assert.Equal("warning", reference.Status);
        WorkflowReferenceFile file = Assert.Single(reference.Files);
        Assert.Equal("src/a.txt", file.Path);
        Assert.Contains(reference.Warnings, warning => warning.ErrorCode == WorkflowReferenceErrorCode.BinaryNotSupported);
        Assert.Contains(reference.Warnings, warning => warning.ErrorCode == "workflow-reference-folder-ignored");
    }

    private static WorkspaceContext CreateWorkspace(string path)
    {
        return new WorkspaceContext(
            RootPath: path,
            ConfigPath: Path.Combine(path, ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        private TempDirectory(string path)
        {
            Path = path;
        }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
