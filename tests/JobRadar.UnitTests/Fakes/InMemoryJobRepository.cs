using JobRadar.Application.Abstractions;
using JobRadar.Domain.Jobs;

namespace JobRadar.UnitTests.Fakes;

/// <summary>Repositório em memória com as mesmas garantias de unicidade do banco (ADR-001).</summary>
internal sealed class InMemoryJobRepository : IJobRepository
{
    private readonly Lock _gate = new();

    private readonly List<Job> _jobs = [];

    public IReadOnlyList<Job> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _jobs];
            }
        }
    }

    public Task<bool> AddIfNewAsync(Job job, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var duplicate = _jobs.Any(existing =>
                existing.Fingerprint == job.Fingerprint
                || (job.ExternalId is not null && existing.SourceId == job.SourceId && existing.ExternalId == job.ExternalId));

            if (duplicate)
            {
                return Task.FromResult(false);
            }

            _jobs.Add(job);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<Job>> ClaimForAnalysisAsync(DateTimeOffset now, TimeSpan lease, int maxCount, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var claimed = _jobs.Where(job => job.IsDueForAnalysis(now)).OrderBy(job => job.FoundAt).Take(maxCount).ToList();
            claimed.ForEach(job => job.StartAnalysis(now, lease));
            return Task.FromResult<IReadOnlyList<Job>>(claimed);
        }
    }

    public Task UpdateAsync(Job job, CancellationToken cancellationToken) => Task.CompletedTask;
}
