using JobRadar.Application.Abstractions;
using JobRadar.Domain.Jobs;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace JobRadar.Infrastructure.LinkedIn;

/// <summary>Mensagens novas da caixa de alertas e o checkpoint até onde foram lidas.</summary>
public sealed record MailboxBatch(IReadOnlyList<MimeMessage> Messages, string? Checkpoint);

/// <summary>A caixa de e-mail onde chegam os alertas (IMAP em produção).</summary>
public interface IAlertMailbox
{
    Task<MailboxBatch> FetchAsync(string? checkpoint, CancellationToken cancellationToken);
}

/// <summary>
/// Fonte de vagas do LinkedIn pelos alertas que o próprio LinkedIn envia por
/// e-mail: nada de scraping nem de acesso ao site.
/// </summary>
public sealed partial class LinkedInEmailJobSource(IAlertMailbox mailbox, ILogger<LinkedInEmailJobSource> logger) : IJobSource
{
    public JobSource Source => JobSource.LinkedIn;

    public async Task<JobSourceBatch> FetchAsync(string? checkpoint, CancellationToken cancellationToken)
    {
        var batch = await mailbox.FetchAsync(checkpoint, cancellationToken);
        var postings = new List<JobPosting>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ignored = 0;

        foreach (var message in batch.Messages)
        {
            if (!LinkedInAlertParser.IsLinkedInAlert(message))
            {
                ignored++;
                continue;
            }

            var found = LinkedInAlertParser.Parse(message);

            if (found.Count == 0)
            {
                LogNoJobsInAlert(logger, message.Date);
            }

            postings.AddRange(found.Where(posting => posting.ExternalId is null || seen.Add(posting.ExternalId)));
        }

        LogFetched(logger, batch.Messages.Count, ignored, postings.Count);
        return new JobSourceBatch(postings, batch.Checkpoint);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Alertas lidos: {Messages} e-mails ({Ignored} de outros remetentes), {Postings} vagas.")]
    private static partial void LogFetched(ILogger logger, int messages, int ignored, int postings);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Alerta do LinkedIn de {Date} sem vagas reconhecíveis. O layout pode ter mudado.")]
    private static partial void LogNoJobsInAlert(ILogger logger, DateTimeOffset date);
}
