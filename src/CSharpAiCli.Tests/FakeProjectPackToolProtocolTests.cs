using CSharpAiCli.ProjectPacks;

namespace CSharpAiCli.Tests;

public sealed class FakeProjectPackToolProtocolTests
{
    [Fact]
    public void Parses_success_fixture_without_paths()
    {
        string json = ReadFixture("success.json");

        FakeProjectPackToolResult result = FakeProjectPackToolProtocol.Parse(json);

        Assert.True(result.Succeeded);
        FakeProjectPackToolOutput output = Assert.Single(result.Outputs);
        Assert.Equal("primary-output", output.Id);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, output.Id);
    }

    [Fact]
    public void Parses_controlled_failure_fixture()
    {
        FakeProjectPackToolResult result = FakeProjectPackToolProtocol.Parse(ReadFixture("failure.json"));

        Assert.False(result.Succeeded);
        Assert.Equal(FakeProjectPackToolStatus.Failed, result.Status);
        Assert.Empty(result.Outputs);
        Assert.Equal("fake-conversion-failed", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Partial_output_is_failure_evidence_not_success()
    {
        FakeProjectPackToolResult result = FakeProjectPackToolProtocol.Parse(ReadFixture("partial-output.json"));

        Assert.False(result.Succeeded);
        Assert.Equal(FakeProjectPackToolStatus.PartialOutput, result.Status);
        Assert.Equal("partial-evidence", Assert.Single(result.Outputs).Id);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == ProjectPackDiagnosticSeverity.Error);
    }

    [Fact]
    public void Rejects_unknown_protocol_field()
    {
        string json = """
            {"protocolVersion":1,"status":"failed","outputs":[],"diagnostics":[],"command":"not-allowed"}
            """;

        ProjectPackContractException exception = Assert.Throws<ProjectPackContractException>(
            () => FakeProjectPackToolProtocol.Parse(json));

        Assert.Equal("fake-tool-field-unknown", exception.ErrorCode);
    }

    [Fact]
    public void Rejects_success_without_declared_output()
    {
        string json = """
            {"protocolVersion":1,"status":"succeeded","outputs":[],"diagnostics":[]}
            """;

        ProjectPackContractException exception = Assert.Throws<ProjectPackContractException>(
            () => FakeProjectPackToolProtocol.Parse(json));

        Assert.Equal("fake-tool-output-missing", exception.ErrorCode);
    }

    private static string ReadFixture(string fileName)
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "GerberTiff",
            "fake",
            "protocol-v1",
            fileName);
        return File.ReadAllText(path);
    }
}
