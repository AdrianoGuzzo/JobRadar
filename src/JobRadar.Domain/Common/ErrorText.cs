namespace JobRadar.Domain.Common;

/// <summary>Mensagens de erro persistidas têm tamanho limitado.</summary>
public static class ErrorText
{
    public const int MaxLength = 1000;

    public static string Truncate(string? message)
    {
        var text = string.IsNullOrWhiteSpace(message) ? "Erro sem mensagem." : message.Trim();
        return text.Length <= MaxLength ? text : text[..(MaxLength - 1)] + "…";
    }
}
