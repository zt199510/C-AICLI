using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using CSharpAiCli.ProjectPacks.GerberTiff;

namespace CSharpAiCli.ProjectPacks.Runtime;

public sealed record GerberTiffRunPlanInput(
    string Id,
    string RelativePath,
    string Extension,
    string Kind,
    string? LayerRole,
    bool PassedToExternalTool,
    long Size,
    string Sha256);

public sealed record GerberTiffRunPlanTool(
    string DependencyId,
    bool Required,
    string Status,
    string? FileName,
    long? FileSize,
    string? Sha256);

public sealed record GerberTiffRunPlanStage(
    string Id,
    string Kind,
    string? DependencyId,
    bool RequiresApproval,
    string RestartPolicy,
    int? TimeoutMilliseconds);

public sealed record GerberTiffRunPlanSnapshot
{
    public GerberTiffRunPlanSnapshot(
        string packId,
        string packVersion,
        string planId,
        string fingerprint,
        string inputDirectory,
        string outputDirectory,
        IReadOnlyList<GerberTiffRunPlanInput>? inputs,
        IReadOnlyList<GerberTiffRunPlanTool>? tools,
        IReadOnlyList<GerberTiffRunPlanStage>? stages,
        string sourceJson)
    {
        ProjectPackContractGuard.RequireId(packId, nameof(packId));
        ProjectPackContractGuard.RequireId(planId, nameof(planId));
        ProjectPackContractGuard.RequireSha256(fingerprint, nameof(fingerprint));
        PackId = packId;
        PackVersion = packVersion;
        PlanId = planId;
        Fingerprint = fingerprint.ToUpperInvariant();
        InputDirectory = inputDirectory;
        OutputDirectory = outputDirectory;
        Inputs = new ReadOnlyCollection<GerberTiffRunPlanInput>((inputs ?? []).ToArray());
        Tools = new ReadOnlyCollection<GerberTiffRunPlanTool>((tools ?? []).ToArray());
        Stages = new ReadOnlyCollection<GerberTiffRunPlanStage>((stages ?? []).ToArray());
        SourceJson = sourceJson;
    }

    public string PackId { get; }
    public string PackVersion { get; }
    public string PlanId { get; }
    public string Fingerprint { get; }
    public string InputDirectory { get; }
    public string OutputDirectory { get; }
    public IReadOnlyList<GerberTiffRunPlanInput> Inputs { get; }
    public IReadOnlyList<GerberTiffRunPlanTool> Tools { get; }
    public IReadOnlyList<GerberTiffRunPlanStage> Stages { get; }
    public string SourceJson { get; }
}

public static class GerberTiffRunPlanLoader
{
    private const int MaxPlanBytes = 2 * 1024 * 1024;

    public static GerberTiffRunPlanSnapshot Load(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (Encoding.UTF8.GetByteCount(json) > MaxPlanBytes)
        {
            throw new ProjectPackContractException(ProjectPackRunErrorCode.PlanInvalid, "Project pack plan exceeds the size limit.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            });
            JsonElement root = document.RootElement;
            RequireString(root, "type", "packs.plan");
            RequireInt(root, "schemaVersion", ProjectPackSchema.CurrentVersion);
            RequireString(root, "planSchema", "gerber-tiff.plan.v1");
            string status = RequiredString(root, "status");
            if (status is not "runnable" and not "ready-for-staging")
            {
                throw Invalid("Only runnable or ready-for-staging plans can create a run.");
            }

            bool runnable = RequiredBoolean(root, "runnable");
            if (!RequiredBoolean(root, "readyForStaging") ||
                RequiredBoolean(root, "conversionExecuted") ||
                RequiredBoolean(root, "executionAuthorized") ||
                RequiredBoolean(root, "approvalPersisted"))
            {
                throw Invalid("Project pack plan authorization flags are invalid.");
            }

            string packId = RequiredString(root, "pack");
            string packVersion = RequiredString(root, "packVersion");
            string planId = RequiredString(root, "planId");
            string fingerprint = RequiredString(root, "fingerprint");
            JsonElement input = RequiredObject(root, "input");
            JsonElement output = RequiredObject(root, "outputDirectory");
            string inputDirectory = RequiredString(input, "directory");
            string outputDirectory = RequiredString(output, "directory");
            if (packId != GerberTiffWorkflowPack.ProfileName ||
                packVersion.Length > 64 || packVersion.Any(character =>
                    !(char.IsAsciiLetterOrDigit(character) || character is '.' or '+' or '-')) ||
                IsUnsafeRelativePath(inputDirectory) || IsUnsafeRelativePath(outputDirectory))
            {
                throw Invalid("Project pack plan identity or workspace-relative paths are invalid.");
            }

            if ((status == "runnable") != runnable)
            {
                throw Invalid("Project pack plan status and runnable flag are inconsistent.");
            }

            if (RequiredString(output, "overwritePolicy") != "deny-existing-output-directory")
            {
                throw Invalid("Project pack plan overwrite policy is unsupported.");
            }

            List<GerberTiffRunPlanInput> inputs = [];
            JsonElement inventory = RequiredObject(root, "inventory");
            foreach (JsonElement file in RequiredArray(inventory, "files"))
            {
                if (!RequiredBoolean(file, "supported"))
                {
                    continue;
                }

                string id = RequiredString(file, "id");
                string relativePath = RequiredString(file, "relativePath");
                string extension = RequiredString(file, "extension");
                string kind = RequiredString(file, "kind");
                string? layerRole = OptionalString(file, "layerRole");
                bool passedToExternalTool = RequiredBoolean(file, "passedToExternalTool");
                long size = RequiredInt64(file, "size");
                string sha256 = RequiredString(file, "sha256");
                ProjectPackContractGuard.RequireId(id, nameof(id));
                ProjectPackContractGuard.RequireId(kind, nameof(kind));
                if (layerRole is not null)
                {
                    ProjectPackContractGuard.RequireId(layerRole, nameof(layerRole));
                }

                ProjectPackContractGuard.RequireSha256(sha256, nameof(sha256));
                if (size <= 0 || size > GerberTiffInputEnvelope.MaxSingleFileBytes ||
                    !IsSupportedExtension(extension) || IsUnsafeRelativePath(relativePath))
                {
                    throw Invalid("Project pack plan input inventory is invalid.");
                }

                inputs.Add(new GerberTiffRunPlanInput(
                    id, relativePath, extension.ToLowerInvariant(), kind, layerRole,
                    passedToExternalTool, size, sha256.ToUpperInvariant()));
            }

            if (inputs.Count == 0 || inputs.Count > GerberTiffInputEnvelope.MaxFileCount ||
                inputs.Sum(item => item.Size) > GerberTiffInputEnvelope.MaxTotalBytes ||
                inputs.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != inputs.Count ||
                inputs.Select(item => item.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != inputs.Count)
            {
                throw Invalid("Project pack plan input inventory is empty, duplicate, or over limit.");
            }

            List<GerberTiffRunPlanTool> tools = [];
            foreach (JsonElement tool in RequiredArray(root, "tools"))
            {
                string dependencyId = RequiredString(tool, "dependencyId");
                string toolStatus = RequiredString(tool, "status");
                string? fileName = OptionalString(tool, "fileName");
                string? toolSha256 = OptionalString(tool, "sha256");
                ProjectPackContractGuard.RequireId(dependencyId, nameof(dependencyId));
                if (toolStatus is not ProjectPackDoctorStatus.StaticOk and not ProjectPackDoctorStatus.Unavailable ||
                    fileName is not null && (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) ||
                        fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                {
                    throw Invalid("Project pack plan tool identity is invalid.");
                }

                if (toolSha256 is not null)
                {
                    ProjectPackContractGuard.RequireSha256(toolSha256, nameof(toolSha256));
                }

                tools.Add(new GerberTiffRunPlanTool(
                    dependencyId,
                    RequiredBoolean(tool, "required"),
                    toolStatus,
                    fileName,
                    OptionalInt64(tool, "fileSize"),
                    toolSha256));
            }

            List<GerberTiffRunPlanStage> stages = [];
            foreach (JsonElement stage in RequiredArray(root, "stages"))
            {
                string id = RequiredString(stage, "id");
                string restartPolicy = RequiredString(stage, "restartPolicy");
                string kind = RequiredString(stage, "kind");
                string? dependencyId = OptionalString(stage, "dependencyId");
                ProjectPackContractGuard.RequireId(id, nameof(id));
                ProjectPackContractGuard.RequireId(kind, nameof(kind));
                if (dependencyId is not null)
                {
                    ProjectPackContractGuard.RequireId(dependencyId, nameof(dependencyId));
                }

                if (!ProjectPackRestartPolicy.IsKnown(restartPolicy))
                {
                    throw Invalid("Project pack plan stage restart policy is invalid.");
                }

                stages.Add(new GerberTiffRunPlanStage(
                    id,
                    kind,
                    dependencyId,
                    RequiredBoolean(stage, "requiresApproval"),
                    restartPolicy,
                    OptionalInt32(stage, "timeoutMilliseconds")));
            }

            if (stages.Count == 0 || stages.Select(stage => stage.Id).Distinct(StringComparer.Ordinal).Count() != stages.Count)
            {
                throw Invalid("Project pack plan stages are missing or duplicate.");
            }

            string sanitizedJson = RenderSanitized(
                status, packId, packVersion, planId, fingerprint, inputDirectory, outputDirectory,
                runnable, inputs, tools, stages);
            return new GerberTiffRunPlanSnapshot(
                packId, packVersion, planId, fingerprint, inputDirectory, outputDirectory,
                inputs.OrderBy(item => item.RelativePath, StringComparer.Ordinal).ToArray(),
                tools.OrderBy(item => item.DependencyId, StringComparer.Ordinal).ToArray(),
                stages,
                sanitizedJson);
        }
        catch (ProjectPackContractException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or OverflowException)
        {
            throw Invalid("Project pack plan is not valid schema-v1 JSON.");
        }
    }

    private static bool IsSupportedExtension(string extension) =>
        GerberTiffInputEnvelope.GerberExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
        GerberTiffInputEnvelope.DrillExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
        GerberTiffInputEnvelope.SidecarExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    private static bool IsUnsafeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.Contains(':') ||
            value.StartsWith("//", StringComparison.Ordinal) || value.StartsWith("\\\\", StringComparison.Ordinal) ||
            value.IndexOfAny(['*', '?']) >= 0 || value.Length > GerberTiffInputEnvelope.MaxRelativePathCharacters)
        {
            return true;
        }

        string[] segments = value.Replace('\\', '/').Split('/');
        return segments.Any(segment => segment.Length == 0 || segment == ".." ||
            segment == "." && segments.Length > 1);
    }

    private static string RenderSanitized(
        string status,
        string packId,
        string packVersion,
        string planId,
        string fingerprint,
        string inputDirectory,
        string outputDirectory,
        bool runnable,
        IReadOnlyList<GerberTiffRunPlanInput> inputs,
        IReadOnlyList<GerberTiffRunPlanTool> tools,
        IReadOnlyList<GerberTiffRunPlanStage> stages) =>
        JsonSerializer.Serialize(new
        {
            type = "packs.plan",
            schemaVersion = ProjectPackSchema.CurrentVersion,
            planSchema = "gerber-tiff.plan.v1",
            status,
            pack = packId,
            packVersion,
            planId,
            fingerprint,
            input = new { directory = inputDirectory, source = "--input" },
            outputDirectory = new
            {
                directory = outputDirectory,
                source = "--output-dir",
                overwritePolicy = "deny-existing-output-directory"
            },
            readyForStaging = true,
            runnable,
            conversionExecuted = false,
            executionAuthorized = false,
            approvalPersisted = false,
            inventory = new
            {
                files = inputs.Select(input => new
                {
                    input.Id,
                    input.RelativePath,
                    input.Extension,
                    input.Kind,
                    input.LayerRole,
                    supported = true,
                    input.PassedToExternalTool,
                    input.Size,
                    input.Sha256
                })
            },
            tools,
            stages
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static JsonElement RequiredObject(JsonElement element, string name) =>
        Required(element, name, JsonValueKind.Object);

    private static JsonElement.ArrayEnumerator RequiredArray(JsonElement element, string name) =>
        Required(element, name, JsonValueKind.Array).EnumerateArray();

    private static JsonElement Required(JsonElement element, string name, JsonValueKind kind)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != kind)
        {
            throw Invalid($"Project pack plan field '{name}' is missing or invalid.");
        }

        return value;
    }

    private static string RequiredString(JsonElement element, string name)
    {
        JsonElement value = Required(element, name, JsonValueKind.String);
        string? text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? throw Invalid($"Project pack plan field '{name}' is empty.") : text;
    }

    private static string? OptionalString(JsonElement element, string name) =>
        !element.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null
            ? null
            : value.ValueKind == JsonValueKind.String ? value.GetString() : throw Invalid($"Project pack plan field '{name}' is invalid.");

    private static bool RequiredBoolean(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw Invalid($"Project pack plan field '{name}' is missing or invalid.");
        }

        return value.GetBoolean();
    }

    private static long RequiredInt64(JsonElement element, string name)
    {
        JsonElement value = Required(element, name, JsonValueKind.Number);
        return value.TryGetInt64(out long result) ? result : throw Invalid($"Project pack plan field '{name}' is invalid.");
    }

    private static long? OptionalInt64(JsonElement element, string name) =>
        !element.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null
            ? null
            : value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long result)
                ? result
                : throw Invalid($"Project pack plan field '{name}' is invalid.");

    private static int? OptionalInt32(JsonElement element, string name) =>
        !element.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null
            ? null
            : value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result)
                ? result
                : throw Invalid($"Project pack plan field '{name}' is invalid.");

    private static void RequireString(JsonElement element, string name, string expected)
    {
        if (!string.Equals(RequiredString(element, name), expected, StringComparison.Ordinal))
        {
            throw Invalid($"Project pack plan field '{name}' is unsupported.");
        }
    }

    private static void RequireInt(JsonElement element, string name, int expected)
    {
        if (RequiredInt64(element, name) != expected)
        {
            throw new ProjectPackContractException(ProjectPackRunErrorCode.SchemaUnsupported, "Project pack plan schema is unsupported.");
        }
    }

    private static ProjectPackContractException Invalid(string message) =>
        new(ProjectPackRunErrorCode.PlanInvalid, message);
}
