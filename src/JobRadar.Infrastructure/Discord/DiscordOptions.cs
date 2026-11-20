using System.ComponentModel.DataAnnotations;
using System.Globalization;

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

    public bool InteractionsEnabled { get; set; }

    public ulong GuildId { get; set; }

    public string AllowedUserIds { get; set; } = string.Empty;

    public bool TryGetAllowedUsers(out HashSet<ulong> users)
    {
        users = [];
        foreach (var value in AllowedUserIds.Split(',', StringSplitOptions.TrimEntries))
        {
            if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0)
            {
                return false;
            }

            users.Add(id);
        }

        return users.Count > 0;
    }

    /// <summary>Só para testes: outra URL base da API (ex.: um servidor falso local).</summary>
    public string? ApiBaseUrl { get; set; }

    [Range(1, 300)]
    public int RequestTimeoutSeconds { get; set; } = 30;
}
