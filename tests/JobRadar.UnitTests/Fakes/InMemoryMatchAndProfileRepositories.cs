using JobRadar.Application.Abstractions;
using JobRadar.Domain.Analysis;
using JobRadar.Domain.Profiles;

namespace JobRadar.UnitTests.Fakes;

/// <summary>Um match por vaga e versão do perfil, como o índice único do banco.</summary>
internal sealed class InMemoryJobMatchRepository : IJobMatchRepository
{
    private readonly Lock _gate = new();

    private readonly List<JobMatch> _matches = [];

    public IReadOnlyList<JobMatch> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _matches];
            }
        }
    }

    public Task<bool> ExistsAsync(Guid jobId, string profileVersion, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_matches.Any(match => match.JobId == jobId && match.ProfileVersion == profileVersion));
        }
    }

    public Task<bool> AddIfNewAsync(JobMatch match, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_matches.Any(existing => existing.JobId == match.JobId && existing.ProfileVersion == match.ProfileVersion))
            {
                return Task.FromResult(false);
            }

            _matches.Add(match);
            return Task.FromResult(true);
        }
    }
}

internal sealed class InMemoryCandidateProfileRepository : ICandidateProfileRepository
{
    public CandidateProfile? Profile { get; set; }

    public Task<CandidateProfile?> GetActiveAsync(CancellationToken cancellationToken) => Task.FromResult(Profile);

    public Task SaveAsync(CandidateProfile profile, CancellationToken cancellationToken)
    {
        Profile = profile;
        return Task.CompletedTask;
    }
}
