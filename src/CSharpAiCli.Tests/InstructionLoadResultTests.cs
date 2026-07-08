using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class InstructionLoadResultTests
{
    [Fact]
    public void Loaded_with_legacy_source_path_populates_sources()
    {
        InstructionLoadResult result = InstructionLoadResult.Loaded(
            "Use project instructions.",
            "workspace/AGENTS.md");

        InstructionSource source = Assert.Single(result.Sources);
        Assert.Equal("workspace/AGENTS.md", source.SourcePath);
        Assert.Equal(0, source.Order);
        Assert.Equal("workspace/AGENTS.md", result.SourcePath);
    }

    [Fact]
    public void Constructor_with_source_path_populates_compatible_single_source()
    {
        InstructionLoadResult result = new(
            Instructions: "Use project instructions.",
            SourcePath: "workspace/AGENTS.md",
            Warnings: []);

        InstructionSource source = Assert.Single(result.Sources);
        Assert.Equal("workspace/AGENTS.md", source.SourcePath);
        Assert.Equal(0, source.Order);
        Assert.Equal("workspace/AGENTS.md", result.SourcePath);
    }

    [Fact]
    public void Empty_initialized_sources_with_source_path_populates_compatible_single_source()
    {
        InstructionLoadResult result = new(
            Instructions: "Use project instructions.",
            SourcePath: "workspace/AGENTS.md",
            Warnings: [])
        {
            Sources = []
        };

        InstructionSource source = Assert.Single(result.Sources);
        Assert.Equal("workspace/AGENTS.md", source.SourcePath);
        Assert.Equal(0, source.Order);
        Assert.Equal("workspace/AGENTS.md", result.SourcePath);
    }

    [Fact]
    public void SourcePath_follows_first_source_when_sources_are_initialized()
    {
        InstructionLoadResult result = new(
            Instructions: "Use project instructions.",
            SourcePath: "workspace/legacy/AGENTS.md",
            Warnings: [])
        {
            Sources =
            [
                new("workspace/AGENTS.md", 7),
                new("workspace/src/AGENTS.md", 3)
            ]
        };

        Assert.Equal("workspace/AGENTS.md", result.SourcePath);
        Assert.Equal("workspace/AGENTS.md", result.Sources[0].SourcePath);
        Assert.Equal(0, result.Sources[0].Order);
        Assert.Equal("workspace/src/AGENTS.md", result.Sources[1].SourcePath);
        Assert.Equal(1, result.Sources[1].Order);
    }

    [Fact]
    public void Sources_are_defensively_copied_when_initialized()
    {
        List<InstructionSource> sources =
        [
            new("workspace/AGENTS.md", 0)
        ];

        InstructionLoadResult result = new(
            Instructions: "Use project instructions.",
            SourcePath: "workspace/AGENTS.md",
            Warnings: [])
        {
            Sources = sources
        };

        sources.Add(new InstructionSource("workspace/src/AGENTS.md", 1));

        InstructionSource source = Assert.Single(result.Sources);
        Assert.Equal("workspace/AGENTS.md", source.SourcePath);
        Assert.Equal(0, source.Order);
    }

    [Fact]
    public void Sources_cannot_be_mutated_by_downcasting_returned_collection()
    {
        InstructionLoadResult result = InstructionLoadResult.Loaded(
            "Use project instructions.",
            [
                new("workspace/AGENTS.md", 0),
                new("workspace/src/AGENTS.md", 1)
            ]);

        if (result.Sources is InstructionSource[] array)
        {
            array[0] = new InstructionSource("workspace/changed/AGENTS.md", 0);
        }

        if (result.Sources is IList<InstructionSource> list)
        {
            try
            {
                list[0] = new InstructionSource("workspace/changed-again/AGENTS.md", 0);
            }
            catch (NotSupportedException)
            {
            }
        }

        Assert.Equal("workspace/AGENTS.md", result.SourcePath);
        Assert.Equal("workspace/AGENTS.md", result.Sources[0].SourcePath);
        Assert.Equal(0, result.Sources[0].Order);
    }

    [Fact]
    public void Sources_rejects_null_when_initialized()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new InstructionLoadResult(
                Instructions: "Use project instructions.",
                SourcePath: "workspace/AGENTS.md",
                Warnings: [])
            {
                Sources = null!
            });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void InstructionSource_rejects_blank_source_paths(string sourcePath)
    {
        Assert.Throws<ArgumentException>(() => new InstructionSource(sourcePath, 0));
    }

    [Fact]
    public void InstructionSource_rejects_negative_order()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InstructionSource("workspace/AGENTS.md", -1));
    }
}
