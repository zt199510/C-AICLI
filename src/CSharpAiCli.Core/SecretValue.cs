namespace CSharpAiCli.Core;

public sealed class SecretValue
{
    private SecretValue(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static SecretValue? From(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : new SecretValue(value);
    }

    public override string ToString()
    {
        return "[redacted]";
    }
}
