namespace CSharpAiCli.Core;

public sealed record ModelsReport(IReadOnlyList<string> Lines)
{
    public static ModelsReport Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        EffectiveConfiguration configuration = snapshot.Configuration;
        string apiKeyStatus = configuration.HasApiKey ? "present" : "missing";

        List<string> lines =
        [
            $"{ProductInfo.DisplayName} models",
            "source: local configuration only",
            "modelListApi: not called",
            $"currentModel: {configuration.Model}",
            $"currentModelSource: {configuration.ModelSource}",
            $"baseUrl: {configuration.BaseUrl}",
            $"baseUrlSource: {configuration.BaseUrlSource}",
            $"apiKey: {apiKeyStatus}",
            $"apiKeySource: {configuration.ApiKeySource}",
            "recommendedModels:",
            "  - gpt-4.1-mini",
            "  - gpt-4.1",
            "  - gpt-4o-mini",
            "examples:",
            "  OPENAI_MODEL=gpt-4.1-mini",
            "  caicli config set model gpt-4.1-mini",
            "  caicli config set baseUrl https://api.openai.com/v1",
            "  caicli config set baseUrl https://gateway.example.test/v1"
        ];

        return new ModelsReport(lines);
    }

    public string ToDisplayText()
    {
        return string.Join(Environment.NewLine, Lines);
    }
}
