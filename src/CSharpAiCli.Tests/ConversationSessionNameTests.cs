using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ConversationSessionNameTests
{
    [Fact]
    public void ConversationSessionName_has_no_public_constructors()
    {
        Assert.Empty(typeof(ConversationSessionName).GetConstructors());
    }

    [Theory]
    [InlineData("smoke", "smoke")]
    [InlineData("week-7_smoke", "week-7_smoke")]
    public void Parse_accepts_safe_names_and_creates_file_safe_name(string input, string expectedFileSafeName)
    {
        ConversationSessionName sessionName = ConversationSessionName.Parse(input);

        Assert.Equal(input.Trim(), sessionName.Value);
        Assert.Equal(expectedFileSafeName, sessionName.FileSafeName);
    }

    [Theory]
    [InlineData("Week-7_Smoke", "week-7_smoke")]
    [InlineData("release notes", "release-notes")]
    [InlineData("  My Session  ", "my-session")]
    public void Parse_adds_stable_suffix_when_normalized_file_safe_name_changes(string input, string expectedPrefix)
    {
        ConversationSessionName sessionName = ConversationSessionName.Parse(input);

        Assert.Equal(input.Trim(), sessionName.Value);
        Assert.StartsWith($"{expectedPrefix}~", sessionName.FileSafeName, StringComparison.Ordinal);
        string suffix = sessionName.FileSafeName[(expectedPrefix.Length + 1)..];
        Assert.Equal(8, suffix.Length);
        Assert.All(suffix, character => Assert.True(
            char.IsAsciiHexDigit(character) && char.IsLower(character) == char.IsLetter(character),
            $"Expected lower-case ASCII hex suffix, got '{suffix}'."));
    }

    [Fact]
    public void Parse_keeps_case_variants_from_colliding_after_normalization()
    {
        ConversationSessionName upperCaseName = ConversationSessionName.Parse("Smoke");
        ConversationSessionName lowerCaseName = ConversationSessionName.Parse("smoke");

        Assert.NotEqual(upperCaseName.FileSafeName, lowerCaseName.FileSafeName);
    }

    [Fact]
    public void Parse_reserves_generated_suffix_namespace_for_normalized_names()
    {
        ConversationSessionName normalizedName = ConversationSessionName.Parse("Smoke");
        ConversationSessionName alreadySafeName = ConversationSessionName.Parse("smoke");

        Assert.Equal("smoke", alreadySafeName.FileSafeName);
        Assert.StartsWith("smoke~", normalizedName.FileSafeName, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ConversationSessionName.Parse(normalizedName.FileSafeName));
    }

    [Fact]
    public void Parse_keeps_whitespace_and_dash_variants_from_colliding_after_normalization()
    {
        ConversationSessionName whitespaceName = ConversationSessionName.Parse("release notes");
        ConversationSessionName dashName = ConversationSessionName.Parse("release-notes");

        Assert.NotEqual(whitespaceName.FileSafeName, dashName.FileSafeName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../secret")]
    [InlineData("..\\secret")]
    [InlineData("a/b")]
    [InlineData("a:b")]
    [InlineData("name*")]
    public void Parse_rejects_empty_path_like_or_invalid_names(string input)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => ConversationSessionName.Parse(input));

        Assert.Contains("session", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("a.b")]
    [InlineData("a+b")]
    [InlineData("name@home")]
    [InlineData("tag#1")]
    public void Parse_rejects_punctuation_that_would_be_dropped_from_file_safe_name(string input)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => ConversationSessionName.Parse(input));

        Assert.Contains("session", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
