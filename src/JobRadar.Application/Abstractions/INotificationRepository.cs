using JobRadar.Domain.Analysis;
using JobRadar.Domain.Jobs;
using JobRadar.Domain.Notifications;

namespace JobRadar.Application.Abstractions;

/// <summary>Uma notificação reivindicada, com o que é preciso para montar a mensagem.</summary>
public sealed record NotificationWork(Notification Notification, Job Job, JobMatch Match, string SourceName);

public interface INotificationRepository
{
    /// <summary>Matches do perfil com score suficiente que ainda não têm notificação.</summary>
    Task<IReadOnlyList<JobMatch>> GetMatchesAwaitingNotificationAsync(Guid candidateProfileId, int minimumScore, CancellationToken cancellationToken);

    /// <summary>Grava a notificação se não há outra para a mesma vaga e perfil.</summary>
    Task<bool> AddIfNewAsync(Notification notification, CancellationToken cancellationToken);

    /// <summary>
    /// Reivindica até <paramref name="maxCount"/> notificações devidas, colocando-as
    /// em envio com lease. Duas chamadas concorrentes nunca recebem a mesma.
    /// </summary>
    Task<IReadOnlyList<NotificationWork>> ClaimDueAsync(DateTimeOffset now, TimeSpan lease, int maxCount, CancellationToken cancellationToken);

    Task UpdateAsync(Notification notification, CancellationToken cancellationToken);
}
