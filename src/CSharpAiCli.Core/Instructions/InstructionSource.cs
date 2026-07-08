namespace CSharpAiCli.Core;

public sealed record InstructionSource
{
    public InstructionSource(string sourcePath, int order)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (order < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(order), "Instruction source order cannot be negative.");
        }

        SourcePath = sourcePath;
        Order = order;
    }

    public string SourcePath { get; }

    public int Order { get; }
}
