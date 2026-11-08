using JobRadar.Application.Abstractions;
using JobRadar.Domain.Jobs;

namespace JobRadar.UnitTests.Fakes;

internal sealed class InMemoryJobSearchConfigurationRepository : IJobSearchConfigurationRepository
{
    public Dictionary<int, JobSearchConfiguration> Items { get; } = [];

    public Task<JobSearchConfiguration> GetOrCreateAsync(JobSource source, CancellationToken cancellationToken)
    {
        if (!Items.TryGetValue(source.Id, out var configuration))
        {
            configuration = JobSearchConfiguration.Create(source);
            Items[source.Id] = configuration;
        }

        return Task.FromResult(configuration);
    }

    public Task SaveAsync(JobSearchConfiguration configuration, CancellationToken cancellationToken) => Task.CompletedTask;
}
