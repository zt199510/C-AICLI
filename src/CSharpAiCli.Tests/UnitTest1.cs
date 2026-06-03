using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void Name_returns_product_command_name()
    {
        Assert.Equal("caicli", ProductInfo.CommandName);
    }
}
