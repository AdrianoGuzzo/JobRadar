using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace JobRadar.Domain.Jobs;

/// <summary>
/// Identidade de uma vaga pelo conteúdo: título, empresa e localização
/// normalizados (ADR-001). Cobre vagas sem identificador externo e a mesma
/// vaga vinda de fontes diferentes.
/// </summary>
public static class JobFingerprint
{
    public static string Compute(string title, string? company, string? location)
    {
        var canonical = string.Join('|', Normalize(title), Normalize(company), Normalize(location));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>Minúsculas, sem acentos e com os espaços colapsados.</summary>
    internal static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}
