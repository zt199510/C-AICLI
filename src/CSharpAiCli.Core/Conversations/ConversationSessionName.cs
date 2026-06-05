namespace CSharpAiCli.Core;

public sealed record ConversationSessionName
{
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

        string trimmedValue = value.Trim();
        if (trimmedValue.Contains("..", StringComparison.Ordinal) ||
            trimmedValue.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            trimmedValue.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal) ||
            trimmedValue.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Session name contains invalid path characters.", nameof(value));
        }

        string fileSafeName = ToFileSafeName(trimmedValue);
        if (!fileSafeName.Any(char.IsLetterOrDigit))
        {
            throw new ArgumentException("Session name must include letters or numbers.", nameof(value));
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
            else if (char.IsWhiteSpace(character))
            {
                pendingDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}
