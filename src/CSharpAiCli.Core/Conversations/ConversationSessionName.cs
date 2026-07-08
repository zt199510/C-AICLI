namespace CSharpAiCli.Core;

using System.Security.Cryptography;
using System.Text;

public sealed record ConversationSessionName
{
    private static readonly char[] ReservedFileNameCharacters = ['<', '>', ':', '"', '|', '?', '*'];

    private ConversationSessionName(string value, string fileSafeName)
    {
        Value = value;
        FileSafeName = fileSafeName;
    }

    public string Value { get; }

    public string FileSafeName { get; }

    public static ConversationSessionName Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Session name must not be empty.", nameof(value));
        }

        string trimmedValue = value.Trim(' ');
        if (trimmedValue.Any(IsUnsupportedWhitespace))
        {
            throw new ArgumentException("Session name contains unsupported characters.", nameof(value));
        }

        if (trimmedValue.Contains("..", StringComparison.Ordinal) ||
            trimmedValue.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            trimmedValue.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal) ||
            trimmedValue.IndexOfAny(ReservedFileNameCharacters) >= 0)
        {
            throw new ArgumentException("Session name contains invalid path characters.", nameof(value));
        }

        if (trimmedValue.Any(character => !IsSupportedCharacter(character)))
        {
            throw new ArgumentException("Session name contains unsupported characters.", nameof(value));
        }

        string fileSafeName = ToFileSafeName(trimmedValue);
        if (string.IsNullOrWhiteSpace(fileSafeName))
        {
            throw new ArgumentException("Session name must produce a file-safe value.", nameof(value));
        }

        if (!string.Equals(fileSafeName, trimmedValue, StringComparison.Ordinal))
        {
            fileSafeName = $"{fileSafeName}~{CreateStableHashSuffix(trimmedValue)}";
        }

        return new ConversationSessionName(trimmedValue, fileSafeName);
    }

    private static string ToFileSafeName(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        bool pendingDash = false;

        foreach (char character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || character is '_' or '-')
            {
                if (pendingDash && builder.Length > 0 && builder[^1] != '-')
                {
                    builder.Append('-');
                }

                builder.Append(character);
                pendingDash = false;
            }
            else if (character == ' ')
            {
                pendingDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string CreateStableHashSuffix(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    private static bool IsSupportedCharacter(char character) =>
        char.IsLetterOrDigit(character) ||
        character == ' ' ||
        character is '_' or '-';

    private static bool IsUnsupportedWhitespace(char character) =>
        character != ' ' && char.IsWhiteSpace(character);
}
