using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ConversationSessionNameTests
{
    [Theory]
    [InlineData("smoke", "smoke")]
    [InlineData("Week-7_Smoke", "week-7_smoke")]
    [InlineData("release notes", "release-notes")]
    [InlineData("  My Session  ", "my-session")]
    public void Parse_accepts_safe_names_and_creates_file_safe_name(string input, string expectedFileSafeName)
    {
        ConversationSessionName sessionName = ConversationSessionName.Parse(input);

        Assert.Equal(input.Trim(), sessionName.Value);
        Assert.Equal(expectedFileSafeName, sessionName.FileSafeName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../secret")]
    [InlineData("..\\secret")]
    [InlineData("a/b")]
    [InlineData("a:b")]
    [InlineData("name*")]
    public void Parse_rejects_empty_path_like_invalid_names(string input)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => ConversationSessionName.Parse(input));

        Assert.Contains("session", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
