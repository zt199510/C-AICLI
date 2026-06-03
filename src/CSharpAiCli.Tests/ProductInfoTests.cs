using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void Metadata_contains_cli_identity()
    {
        Assert.Equal("caicli", ProductInfo.CommandName);
        Assert.Equal("C# AI CLI", ProductInfo.DisplayName);
        Assert.Equal("Local AI engineering CLI", ProductInfo.Description);
        Assert.Equal("net9.0", ProductInfo.TargetFramework);
    }
}
