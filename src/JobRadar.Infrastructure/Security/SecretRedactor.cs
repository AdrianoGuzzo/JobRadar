using System.Text.RegularExpressions;

namespace JobRadar.Infrastructure.Security;

/// <summary>
/// Remove credenciais de textos que vêm de fora (stderr do Claude, respostas
/// HTTP) antes de irem para logs ou para o banco.
/// </summary>
public static partial class SecretRedactor
{
    public const string Mask = "[REDACTED]";

    public static string Redact(string? text, params IEnumerable<string?> knownSecrets)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        foreach (var secret in knownSecrets.Where(secret => secret is { Length: >= 6 }))
        {
            text = text.Replace(secret!, Mask, StringComparison.Ordinal);
        }

        text = AnthropicKey().Replace(text, Mask);
        text = DiscordToken().Replace(text, Mask);
        return AuthorizationHeader().Replace(text, "$1 " + Mask);
    }

    [GeneratedRegex(@"sk-ant-[A-Za-z0-9_\-]{8,}")]
    private static partial Regex AnthropicKey();

    [GeneratedRegex(@"[MNO][A-Za-z\d_\-]{23,27}\.[A-Za-z\d_\-]{6}\.[A-Za-z\d_\-]{27,}")]
    private static partial Regex DiscordToken();

    [GeneratedRegex(@"(?i)\b(Bearer|Bot)\s+[A-Za-z0-9._\-]{16,}")]
    private static partial Regex AuthorizationHeader();
}
