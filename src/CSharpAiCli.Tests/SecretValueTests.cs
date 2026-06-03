using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class SecretValueTests
{
    [Fact]
    public void From_returns_null_for_blank_values()
    {
        Assert.Null(SecretValue.From(null));
        Assert.Null(SecretValue.From(""));
        Assert.Null(SecretValue.From("   "));
    }

    [Fact]
    public void Value_keeps_secret_available_but_ToString_redacts_it()
    {
        SecretValue secret = SecretValue.From("sk-test-secret")!;

        Assert.Equal("sk-test-secret", secret.Value);
        Assert.Equal("[redacted]", secret.ToString());
        Assert.DoesNotContain("sk-test-secret", $"{secret}", StringComparison.Ordinal);
    }
}
