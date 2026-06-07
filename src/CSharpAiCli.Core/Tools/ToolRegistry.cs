namespace CSharpAiCli.Core;

public sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ITool> toolsByName = new(StringComparer.Ordinal);
    private readonly List<ToolDefinition> definitions = [];

    public void Register(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        string name = tool.Definition.Name;
        if (toolsByName.ContainsKey(name))
        {
            throw new InvalidOperationException($"Tool '{name}' is already registered.");
        }

        toolsByName.Add(name, tool);
        definitions.Add(tool.Definition);
    }

    public IReadOnlyList<ToolDefinition> List()
    {
        return definitions.ToArray();
    }

    public bool TryGet(string name, out ITool? tool)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            tool = null;
            return false;
        }

        return toolsByName.TryGetValue(name, out tool);
    }
}
