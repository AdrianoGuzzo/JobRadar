using System.ComponentModel.DataAnnotations;

namespace JobRadar.Infrastructure.Discord;

/// <summary>Configuração do Discord (seção <c>Discord</c>, variáveis <c>Discord__*</c>).</summary>
public sealed class DiscordOptions
{
    public const string Section = "Discord";

    /// <summary>Token do bot. Segredo: só por variável de ambiente, nunca em arquivo versionado.</summary>
    [Required]
    public string BotToken { get; set; } = string.Empty;

    /// <summary>O canal que recebe as notificações.</summary>
    [Range(1, ulong.MaxValue)]
    public ulong ChannelId { get; set; }

    /// <summary>Só para testes: outra URL base da API (ex.: um servidor falso local).</summary>
    public string? ApiBaseUrl { get; set; }

    [Range(1, 300)]
    public int RequestTimeoutSeconds { get; set; } = 30;
}
