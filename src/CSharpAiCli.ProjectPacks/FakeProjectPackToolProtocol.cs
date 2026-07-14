using System.Collections.ObjectModel;
using System.Text.Json;

namespace CSharpAiCli.ProjectPacks;

public static class FakeProjectPackToolStatus
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string PartialOutput = "partial-output";

    public static bool IsKnown(string? value) => value is Succeeded or Failed or PartialOutput;
}

public sealed record FakeProjectPackToolOutput
{
    public FakeProjectPackToolOutput(string Id, long Size, string Sha256)
    {
        ProjectPackContractGuard.RequireId(Id, nameof(Id));
        if (Size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Size));
        }

        ProjectPackContractGuard.RequireSha256(Sha256, nameof(Sha256));
        this.Id = Id;
        this.Size = Size;
        this.Sha256 = Sha256.ToUpperInvariant();
    }

    public string Id { get; }

    public long Size { get; }

    public string Sha256 { get; }
}

public sealed record FakeProjectPackToolResult
{
    public FakeProjectPackToolResult(
        int ProtocolVersion,
        string Status,
        IReadOnlyList<FakeProjectPackToolOutput>? Outputs,
        IReadOnlyList<ProjectPackDiagnostic>? Diagnostics)
    {
        if (ProtocolVersion != FakeProjectPackToolProtocol.CurrentVersion)
        {
            throw new ProjectPackContractException("fake-tool-protocol-unsupported", "Fake tool protocol version is unsupported.");
        }

        if (!FakeProjectPackToolStatus.IsKnown(Status))
        {
            throw new ProjectPackContractException("fake-tool-status-invalid", "Fake tool status is invalid.");
        }

        this.ProtocolVersion = ProtocolVersion;
        this.Status = Status;
        this.Outputs = new ReadOnlyCollection<FakeProjectPackToolOutput>((Outputs ?? []).ToArray());
        this.Diagnostics = new ReadOnlyCollection<ProjectPackDiagnostic>((Diagnostics ?? []).ToArray());
        if (Status == FakeProjectPackToolStatus.Succeeded && this.Outputs.Count == 0)
        {
            throw new ProjectPackContractException("fake-tool-output-missing", "Successful fake tool result must declare an output.");
        }

        if (Status == FakeProjectPackToolStatus.PartialOutput && this.Outputs.Count == 0)
        {
            throw new ProjectPackContractException("fake-tool-partial-output-missing", "Partial-output fake tool result must declare retained evidence.");
        }
    }

    public int ProtocolVersion { get; }

    public string Status { get; }

    public IReadOnlyList<FakeProjectPackToolOutput> Outputs { get; }

    public IReadOnlyList<ProjectPackDiagnostic> Diagnostics { get; }

    public bool Succeeded => Status == FakeProjectPackToolStatus.Succeeded;
}

public static class FakeProjectPackToolProtocol
{
    public const int CurrentVersion = 1;
    private const int MaxDocumentBytes = 64 * 1024;

    public static FakeProjectPackToolResult Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxDocumentBytes)
        {
            throw new ProjectPackContractException("fake-tool-result-too-large", "Fake tool result exceeds the protocol size limit.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16
            });
            JsonElement root = document.RootElement;
            RequireOnlyProperties(root, "protocolVersion", "status", "outputs", "diagnostics");
            int protocolVersion = root.GetProperty("protocolVersion").GetInt32();
            string status = root.GetProperty("status").GetString() ?? string.Empty;
            List<FakeProjectPackToolOutput> outputs = [];
            foreach (JsonElement output in root.GetProperty("outputs").EnumerateArray())
            {
                RequireOnlyProperties(output, "id", "size", "sha256");
                outputs.Add(new FakeProjectPackToolOutput(
                    output.GetProperty("id").GetString() ?? string.Empty,
                    output.GetProperty("size").GetInt64(),
                    output.GetProperty("sha256").GetString() ?? string.Empty));
            }

            List<ProjectPackDiagnostic> diagnostics = [];
            foreach (JsonElement diagnostic in root.GetProperty("diagnostics").EnumerateArray())
            {
                RequireOnlyProperties(diagnostic, "code", "severity", "summary", "dependencyId", "stageId");
                diagnostics.Add(new ProjectPackDiagnostic(
                    diagnostic.GetProperty("code").GetString() ?? string.Empty,
                    diagnostic.GetProperty("severity").GetString() ?? string.Empty,
                    diagnostic.GetProperty("summary").GetString() ?? string.Empty,
                    GetOptionalString(diagnostic, "dependencyId"),
                    GetOptionalString(diagnostic, "stageId")));
            }

            return new FakeProjectPackToolResult(protocolVersion, status, outputs, diagnostics);
        }
        catch (ProjectPackContractException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException
            or InvalidOperationException
            or KeyNotFoundException
            or ArgumentException)
        {
            throw new ProjectPackContractException("fake-tool-result-invalid", "Fake tool result is not valid protocol v1 JSON.");
        }
    }

    private static string? GetOptionalString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;
    }

    private static void RequireOnlyProperties(JsonElement element, params string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ProjectPackContractException("fake-tool-result-invalid", "Fake tool protocol value must be an object.");
        }

        HashSet<string> names = new(allowed, StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!names.Contains(property.Name))
            {
                throw new ProjectPackContractException("fake-tool-field-unknown", $"Unknown fake tool field '{property.Name}'.");
            }
        }
    }
}
