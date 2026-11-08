using JobRadar.Domain.Jobs;

namespace JobRadar.Application.Abstractions;

public interface IJobRepository
{
    /// <summary>Grava a vaga se ela ainda não existe (id externo ou fingerprint); <c>false</c> se é repetida.</summary>
    Task<bool> AddIfNewAsync(Job job, CancellationToken cancellationToken);

    /// <summary>
    /// Reivindica até <paramref name="maxCount"/> vagas devidas para análise,
    /// colocando-as em análise com lease. Duas chamadas concorrentes nunca
    /// recebem a mesma vaga.
    /// </summary>
    Task<IReadOnlyList<Job>> ClaimForAnalysisAsync(DateTimeOffset now, TimeSpan lease, int maxCount, CancellationToken cancellationToken);

    Task UpdateAsync(Job job, CancellationToken cancellationToken);
}

public interface IJobSearchConfigurationRepository
{
    Task<JobSearchConfiguration> GetOrCreateAsync(JobSource source, CancellationToken cancellationToken);

    Task SaveAsync(JobSearchConfiguration configuration, CancellationToken cancellationToken);
}
