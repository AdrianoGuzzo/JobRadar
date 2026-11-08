using JobRadar.Application.Abstractions;
using JobRadar.Domain.Analysis;
using JobRadar.Domain.Jobs;
using JobRadar.Domain.Notifications;

namespace JobRadar.UnitTests.Fakes;

/// <summary>Uma notificação por vaga e perfil, com reivindicação atômica, como no banco.</summary>
internal sealed class InMemoryNotificationRepository(InMemoryJobRepository jobs, InMemoryJobMatchRepository matches) : INotificationRepository
{
    private readonly Lock _gate = new();

    private readonly List<Notification> _notifications = [];

    public IReadOnlyList<Notification> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _notifications];
            }
        }
    }

    public Task<IReadOnlyList<JobMatch>> GetMatchesAwaitingNotificationAsync(Guid candidateProfileId, int minimumScore, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<JobMatch> awaiting = [.. matches.All.Where(match =>
                match.CandidateProfileId == candidateProfileId
                && match.Score >= minimumScore
                && !_notifications.Any(n => n.JobId == match.JobId && n.CandidateProfileId == candidateProfileId))];
            return Task.FromResult(awaiting);
        }
    }

    public Task<bool> AddIfNewAsync(Notification notification, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_notifications.Any(n => n.JobId == notification.JobId && n.CandidateProfileId == notification.CandidateProfileId))
            {
                return Task.FromResult(false);
            }

            _notifications.Add(notification);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<NotificationWork>> ClaimDueAsync(DateTimeOffset now, TimeSpan lease, int maxCount, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var claimed = _notifications.Where(n => n.IsDue(now)).OrderBy(n => n.CreatedAt).Take(maxCount).ToList();
            claimed.ForEach(n => n.StartSending(now, lease));

            IReadOnlyList<NotificationWork> work = [.. claimed.Select(n => new NotificationWork(
                n,
                jobs.All.Single(job => job.Id == n.JobId),
                matches.All.Single(match => match.Id == n.JobMatchId),
                JobSource.All.Single(source => source.Id == jobs.All.Single(job => job.Id == n.JobId).SourceId).Name))];
            return Task.FromResult(work);
        }
    }

    public Task UpdateAsync(Notification notification, CancellationToken cancellationToken) => Task.CompletedTask;
}
