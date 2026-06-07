namespace CSharpAiCli.Core;

public interface IToolRegistry
{
    void Register(ITool tool);

    IReadOnlyList<ToolDefinition> List();

    bool TryGet(string name, out ITool? tool);
}
