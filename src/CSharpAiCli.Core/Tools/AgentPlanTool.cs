using System.Text;
using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class AgentPlanTool : ITool
{
    public const string ToolName = "agent.plan";
    public const int MaxPlanSummaryCharacters = 4096;
    private const int MaxItems = 20;
    private const int MaxItemCharacters = 200;

    public ToolDefinition Definition { get; } = new(
        ToolName,
        "Record a read-only execution plan with goal, candidate files, expected tools, and risks.",
        """{"type":"object","properties":{"goal":{"type":"string"},"candidateFiles":{"type":"array","items":{"type":"string"}},"expectedTools":{"type":"array","items":{"type":"string"}},"risks":{"type":"array","items":{"type":"string"}}},"required":["goal"]}""",
        ToolRiskLevel.Read);

    public ToolExecutionResult Execute(
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadPlan(context.ArgumentsJson, out PlanArguments plan, out ToolExecutionResult? failure))
        {
            return failure!;
        }

        string summary = FormatPlan(plan);
        bool truncated = false;
        if (summary.Length > MaxPlanSummaryCharacters)
        {
            summary = summary[..MaxPlanSummaryCharacters].TrimEnd() +
                Environment.NewLine +
                AgentStartupPlanBuilder.TruncationWarning;
            truncated = true;
        }

        return ToolExecutionResult.Success(
            summary,
            structuredPayload: ToolStructuredPayload.Create(
                ("goal", plan.Goal),
                ("candidateFiles", plan.CandidateFiles),
                ("expectedTools", plan.ExpectedTools),
                ("risks", plan.Risks),
                ("truncated", truncated)));
    }

    private static bool TryReadPlan(
        string argumentsJson,
        out PlanArguments plan,
        out ToolExecutionResult? failure)
    {
        plan = default!;
        failure = null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(argumentsJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                failure = ToolExecutionResult.Failure(
                    ToolErrorCode.InvalidToolArguments,
                    "Tool arguments must be a JSON object.",
                    structuredPayload: ToolStructuredPayload.InvalidArguments(ToolName, "arguments"));
                return false;
            }

            if (!root.TryGetProperty("goal", out JsonElement goalElement) ||
                goalElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(goalElement.GetString()))
            {
                failure = ToolExecutionResult.Failure(
                    ToolErrorCode.InvalidToolArguments,
                    "Plan arguments must include a non-empty goal.",
                    structuredPayload: ToolStructuredPayload.InvalidArguments(ToolName, "goal"));
                return false;
            }

            plan = new PlanArguments(
                BoundItem(goalElement.GetString()!),
                ReadStringArray(root, "candidateFiles"),
                ReadStringArray(root, "expectedTools"),
                ReadStringArray(root, "risks"));
            return true;
        }
        catch (JsonException)
        {
            failure = ToolExecutionResult.Failure(
                ToolErrorCode.InvalidToolArguments,
                "Tool arguments must be valid JSON.",
                structuredPayload: ToolStructuredPayload.InvalidArguments(ToolName, "arguments"));
            return false;
        }
    }

    private static string[] ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement arrayElement) ||
            arrayElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return arrayElement
            .EnumerateArray()
            .Where(element => element.ValueKind == JsonValueKind.String)
            .Select(element => element.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => BoundItem(value!))
            .Take(MaxItems)
            .ToArray();
    }

    private static string BoundItem(string value)
    {
        string normalized = value.Trim();
        return normalized.Length <= MaxItemCharacters
            ? normalized
            : normalized[..MaxItemCharacters];
    }

    private static string FormatPlan(PlanArguments plan)
    {
        StringBuilder builder = new();
        builder.AppendLine("Goal: " + plan.Goal);
        builder.AppendLine("Candidate files: " + FormatList(plan.CandidateFiles));
        builder.AppendLine("Expected tools: " + FormatList(plan.ExpectedTools));
        builder.AppendLine("Risks: " + FormatList(plan.Risks));
        return builder.ToString().TrimEnd();
    }

    private static string FormatList(IReadOnlyList<string> values)
    {
        return values.Count == 0
            ? "none listed"
            : string.Join(", ", values);
    }

    private sealed record PlanArguments(
        string Goal,
        string[] CandidateFiles,
        string[] ExpectedTools,
        string[] Risks);
}
