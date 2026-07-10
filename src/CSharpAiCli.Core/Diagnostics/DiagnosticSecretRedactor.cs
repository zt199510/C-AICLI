using System.Text.RegularExpressions;

namespace CSharpAiCli.Core;

internal static class DiagnosticSecretRedactor
{
    private const string QuotedSecretValuePattern = """\\?["'](?:\\\\.|\\.|[^"'\\])*\\?["']""";
    private const string BearerSecretValuePattern = @"Bearer\s+(?:\[redacted\]|[A-Za-z0-9._~+/=-]+)";
    private const string AuthorizationSchemeSecretValuePattern =
        @"[A-Za-z][A-Za-z0-9._~-]*\s+(?:\[redacted\]|[^\r\n}\]]+)";
    private const string AuthorizationKeyNamePattern = @"(?:[A-Za-z0-9]+[_-]+)*authorization";
    private const string PlainSecretValuePattern = @"[^\s,;}\]]+";
    private const string SecretKeyNamePattern =
        @"(?:[A-Za-z0-9]+[_-]+)*(?:api[_-]?key|access[_-]?token|refresh[_-]?token|token|client[_-]?secret|secret[_-]?access[_-]?key|password|secret|authorization|private[_-]?key|secret[_-]?key)" +
        "|apiKey|accessToken|refreshToken|clientSecret|awsSecretAccessKey|privateKey|secretKey";

    private static readonly Regex SecretKeyNameRegex = new(
        "^(?:" + SecretKeyNamePattern + ")$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex KeyValueSecretPattern = new(
        $$"""(?<![A-Za-z0-9_-])(?<prefix>(?:\\?["'](?i:{{AuthorizationKeyNamePattern}})\\?["']|(?i:{{AuthorizationKeyNamePattern}}))(?![A-Za-z0-9_-])\s*[:=]\s*)(?<value>{{QuotedSecretValuePattern}}|{{BearerSecretValuePattern}}|{{AuthorizationSchemeSecretValuePattern}}|{{PlainSecretValuePattern}})|(?<![A-Za-z0-9_-])(?<prefix>(?:\\?["'](?i:{{SecretKeyNamePattern}})\\?["']|(?i:{{SecretKeyNamePattern}}))(?![A-Za-z0-9_-])\s*[:=]\s*)(?<value>{{QuotedSecretValuePattern}}|{{BearerSecretValuePattern}}|{{PlainSecretValuePattern}})""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex AuthorizationSchemeSecretPattern = new(
        @"^(?<scheme>[A-Za-z][A-Za-z0-9._~-]*)\s+(?:\[redacted\]|[^\r\n}\]]+)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex BearerTokenPattern = new(
        @"\bBearer\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex OpenAiKeyPattern = new(
        @"\bsk-[A-Za-z0-9._-]+",
        RegexOptions.CultureInvariant);

    private static readonly Regex GitHubTokenPattern = new(
        @"\b(?:gh[pousr]|github_pat)_[A-Za-z0-9_]+",
        RegexOptions.CultureInvariant);

    public static string Redact(string value)
    {
        string redacted = RedactKeyValueSecrets(value);
        redacted = BearerTokenPattern.Replace(redacted, "Bearer [redacted]");
        redacted = OpenAiKeyPattern.Replace(redacted, "[redacted]");
        redacted = GitHubTokenPattern.Replace(redacted, "[redacted]");
        return redacted;
    }

    public static bool IsSecretName(string value)
    {
        return SecretKeyNameRegex.IsMatch(value);
    }

    private static string RedactKeyValueSecrets(string value)
    {
        return KeyValueSecretPattern.Replace(value, match =>
        {
            Group prefix = match.Groups["prefix"];
            Group secretValue = match.Groups["value"];
            if (!prefix.Success || !secretValue.Success)
            {
                return match.Value;
            }

            return prefix.Value + RedactMatchedValue(secretValue.Value);
        });
    }

    private static string RedactMatchedValue(string value)
    {
        if (value.StartsWith("\\\"", StringComparison.Ordinal) &&
            value.EndsWith("\\\"", StringComparison.Ordinal))
        {
            return "\\\"[redacted]\\\"";
        }

        if (value.StartsWith("\\'", StringComparison.Ordinal) &&
            value.EndsWith("\\'", StringComparison.Ordinal))
        {
            return "\\'[redacted]\\'";
        }

        if (value.StartsWith('"') && value.EndsWith('"'))
        {
            return "\"[redacted]\"";
        }

        if (value.StartsWith('\'') && value.EndsWith('\''))
        {
            return "'[redacted]'";
        }

        if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return "Bearer [redacted]";
        }

        Match authorizationScheme = AuthorizationSchemeSecretPattern.Match(value);
        if (authorizationScheme.Success)
        {
            return authorizationScheme.Groups["scheme"].Value + " [redacted]";
        }

        return "[redacted]";
    }
}
