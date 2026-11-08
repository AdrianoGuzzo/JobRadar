using JobRadar.Application.Abstractions;
using JobRadar.Domain.Common;
using JobRadar.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace JobRadar.Application.Notifications;

public sealed class NotifyMatchesOptions
{
    public int BatchSize { get; set; } = 20;

    /// <summary>Por quanto tempo a notificação fica reservada ao envio em curso.</summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(2);
}

public sealed record NotifyMatchesResult(int Created, int Sent, int Failed);

/// <summary>
/// Notifica as análises com score suficiente. A notificação é gravada antes do
/// envio e é única por vaga e perfil; uma falha no Discord nunca toca no
/// match, só agenda nova tentativa (ADR-002).
/// </summary>
public sealed partial class NotifyMatches(
    INotificationRepository notifications,
    ICandidateProfileRepository profiles,
    IDiscordNotificationService discord,
    NotificationPolicy policy,
    RetryPolicy retryPolicy,
    NotifyMatchesOptions options,
    TimeProvider time,
    ILogger<NotifyMatches> logger)
{
    public async Task<NotifyMatchesResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var profile = await profiles.GetActiveAsync(cancellationToken);

        if (profile is null)
        {
            return new NotifyMatchesResult(0, 0, 0);
        }

        var created = 0;
        var awaiting = await notifications.GetMatchesAwaitingNotificationAsync(profile.Id, policy.MinimumScore, cancellationToken);

        foreach (var match in awaiting.Where(match => policy.ShouldNotify(match, alreadyNotified: false)))
        {
            if (await notifications.AddIfNewAsync(Notification.Create(match, time.GetUtcNow()), cancellationToken))
            {
                created++;
            }
        }

        int sent = 0, failed = 0;
        var claimed = await notifications.ClaimDueAsync(time.GetUtcNow(), options.Lease, options.BatchSize, cancellationToken);

        foreach (var work in claimed)
        {
            if (await SendAsync(work, cancellationToken))
            {
                sent++;
            }
            else
            {
                failed++;
            }
        }

        return new NotifyMatchesResult(created, sent, failed);
    }

    private async Task<bool> SendAsync(NotificationWork work, CancellationToken cancellationToken)
    {
        var notification = work.Notification;
        var succeeded = true;

        try
        {
            var messageId = await discord.SendAsync(JobNotificationMessage.From(work.Job, work.Match, work.SourceName), cancellationToken);
            notification.MarkSent(messageId, time.GetUtcNow());
            LogSent(logger, work.Job.Id, work.Match.Score);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            var (kind, message) = exception is NotificationDeliveryException delivery
                ? (delivery.Kind, delivery.Message)
                : (FailureKind.Transient, $"Erro inesperado no envio ({exception.GetType().Name}).");

            notification.RecordFailure(kind, message, retryPolicy, time.GetUtcNow());
            LogFailed(logger, exception, work.Job.Id, kind, notification.Attempts);
            succeeded = false;
        }

        await notifications.UpdateAsync(notification, cancellationToken);
        return succeeded;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Vaga {JobId} notificada (score {Score}).")]
    private static partial void LogSent(ILogger logger, Guid jobId, int score);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao notificar a vaga {JobId} ({Kind}, tentativa {Attempt}).")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid jobId, FailureKind kind, int attempt);
}
