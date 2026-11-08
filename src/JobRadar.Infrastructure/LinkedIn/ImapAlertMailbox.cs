using System.ComponentModel.DataAnnotations;
using System.Globalization;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace JobRadar.Infrastructure.LinkedIn;

/// <summary>Configuração da caixa IMAP dos alertas (seção <c>Imap</c>, variáveis <c>Imap__*</c>).</summary>
public sealed class ImapOptions
{
    public const string Section = "Imap";

    [Required]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 993;

    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.SslOnConnect;

    [Required]
    public string User { get; set; } = string.Empty;

    /// <summary>Senha de app. Segredo: só por variável de ambiente.</summary>
    [Required]
    public string Password { get; set; } = string.Empty;

    /// <summary>A pasta (ou marcador do Gmail) para onde um filtro move os alertas do LinkedIn.</summary>
    [Required]
    public string Folder { get; set; } = "INBOX";

    /// <summary>Na primeira leitura, até quantos dias para trás buscar.</summary>
    [Range(1, 90)]
    public int LookbackDays { get; set; } = 7;

    [Range(1, 500)]
    public int MaxMessagesPerRun { get; set; } = 50;

    [Range(5, 600)]
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// Lê a pasta de alertas por IMAP, só para leitura: não marca como lido, não
/// move nem apaga. O checkpoint é <c>UIDVALIDITY:último UID lido</c>; se o
/// servidor trocar o UIDVALIDITY, a leitura recomeça pela janela de lookback (a
/// deduplicação de vagas cuida das repetidas).
/// </summary>
public sealed partial class ImapAlertMailbox(IOptions<ImapOptions> options, TimeProvider timeProvider, ILogger<ImapAlertMailbox> logger) : IAlertMailbox
{
    private readonly ImapOptions _options = options.Value;

    public async Task<MailboxBatch> FetchAsync(string? checkpoint, CancellationToken cancellationToken)
    {
        using var client = new ImapClient { Timeout = _options.TimeoutSeconds * 1000 };
        await client.ConnectAsync(_options.Host, _options.Port, _options.Security, cancellationToken);
        await client.AuthenticateAsync(_options.User, _options.Password, cancellationToken);

        var folder = await OpenFolderAsync(client, cancellationToken);
        var validity = folder.UidValidity;
        var lastUid = ParseCheckpoint(checkpoint, validity);

        IList<UniqueId> found = lastUid is { } last
            ? await folder.SearchAsync(SearchQuery.Uids(new UniqueIdRange(new UniqueId(validity, last + 1), UniqueId.MaxValue)), cancellationToken)
            : await folder.SearchAsync(SearchQuery.DeliveredAfter(timeProvider.GetUtcNow().UtcDateTime.Date.AddDays(-_options.LookbackDays)), cancellationToken);

        var uids = SelectNew(found, lastUid, _options.MaxMessagesPerRun);
        var messages = new List<MimeMessage>(uids.Count);

        foreach (var uid in uids)
        {
            // GetMessage usa BODY.PEEK[]: a mensagem continua não lida.
            messages.Add(await folder.GetMessageAsync(uid, cancellationToken));
        }

        var newLastUid = uids.Count > 0 ? uids[^1].Id : lastUid ?? (folder.UidNext is { } next ? next.Id - 1 : 0);

        await client.DisconnectAsync(quit: true, cancellationToken);
        LogFetched(logger, messages.Count, uids.Count < found.Count);

        return new MailboxBatch(messages, string.Create(CultureInfo.InvariantCulture, $"{validity}:{newLastUid}"));
    }

    /// <summary>O último UID lido, se o checkpoint for desta mesma pasta (mesmo UIDVALIDITY).</summary>
    public static uint? ParseCheckpoint(string? checkpoint, uint validity)
    {
        var parts = checkpoint?.Split(':');

        return parts is [var v, var u]
            && uint.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var savedValidity)
            && uint.TryParse(u, NumberStyles.None, CultureInfo.InvariantCulture, out var uid)
            && savedValidity == validity
                ? uid
                : null;
    }

    /// <summary>Os UIDs ainda não lidos, em ordem, até o limite. "N:*" sempre inclui a última mensagem, mesmo com UID menor que N.</summary>
    public static List<UniqueId> SelectNew(IList<UniqueId> found, uint? lastUid, int max) =>
        [.. found.Where(uid => lastUid is null || uid.Id > lastUid).OrderBy(uid => uid.Id).Take(max)];

    private async Task<IMailFolder> OpenFolderAsync(ImapClient client, CancellationToken cancellationToken)
    {
        IMailFolder folder;

        try
        {
            folder = _options.Folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
                ? client.Inbox
                : await client.GetFolderAsync(_options.Folder, cancellationToken);
        }
        catch (FolderNotFoundException exception)
        {
            throw new InvalidOperationException($"A pasta IMAP \"{_options.Folder}\" não existe. Verifique Imap__Folder.", exception);
        }

        await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        return folder;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "IMAP: {Count} e-mails novos lidos (mais pendentes para a próxima execução: {HasMore}).")]
    private static partial void LogFetched(ILogger logger, int count, bool hasMore);
}
