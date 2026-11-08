using JobRadar.Domain.Analysis;
using JobRadar.Domain.Common;

namespace JobRadar.Domain.Notifications;

public enum NotificationStatus
{
    Pending,
    Sending,
    RetryScheduled,
    Sent,
    Failed,
}

/// <summary>
/// O aviso de uma vaga ao candidato. Há no máximo um por vaga e perfil; é
/// criado antes do envio e guarda o resultado de cada tentativa (ADR-002).
/// </summary>
public sealed class Notification
{
    private Notification()
    {
    }

    public Guid Id { get; private set; }

    public Guid JobId { get; private set; }

    public Guid JobMatchId { get; private set; }

    public Guid CandidateProfileId { get; private set; }

    public NotificationStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? NextAttemptAt { get; private set; }

    public DateTimeOffset? LeasedUntil { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public string? ExternalMessageId { get; private set; }

    public static Notification Create(JobMatch match, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        JobId = match.JobId,
        JobMatchId = match.Id,
        CandidateProfileId = match.CandidateProfileId,
        Status = NotificationStatus.Pending,
        CreatedAt = now,
    };

    public bool IsDue(DateTimeOffset now) => Status switch
    {
        NotificationStatus.Pending => true,
        NotificationStatus.RetryScheduled => NextAttemptAt <= now,
        NotificationStatus.Sending => LeasedUntil < now,
        _ => false,
    };

    public void StartSending(DateTimeOffset now, TimeSpan lease)
    {
        Status = NotificationStatus.Sending;
        LeasedUntil = now + lease;
    }

    public void MarkSent(string externalMessageId, DateTimeOffset now)
    {
        Status = NotificationStatus.Sent;
        ExternalMessageId = externalMessageId;
        SentAt = now;
        LeasedUntil = null;
        NextAttemptAt = null;
        LastError = null;
    }

    public void RecordFailure(FailureKind kind, string error, RetryPolicy policy, DateTimeOffset now)
    {
        Attempts++;
        LastError = ErrorText.Truncate(error);
        LeasedUntil = null;

        var decision = policy.Decide(Attempts, kind, now);
        Status = decision.ShouldRetry ? NotificationStatus.RetryScheduled : NotificationStatus.Failed;
        NextAttemptAt = decision.NextAttemptAt;
    }
}
