using JobRadar.Application.Abstractions;
using JobRadar.Domain.Common;
using JobRadar.Domain.Jobs;
using Microsoft.Extensions.Logging;

namespace JobRadar.Application.Jobs;

public sealed record CollectJobsResult(int Found, int Added, int Skipped, IReadOnlyList<string> FailedSources);

/// <summary>
/// Coleta as vagas novas de cada fonte, descarta as repetidas (ADR-001) e
/// avança o checkpoint da fonte só depois de gravar. A falha de uma fonte não
/// impede as outras.
/// </summary>
public sealed partial class CollectJobs(
    IEnumerable<IJobSource> sources,
    IJobRepository jobs,
    IJobSearchConfigurationRepository configurations,
    TimeProvider time,
    ILogger<CollectJobs> logger)
{
    public async Task<CollectJobsResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        int found = 0, added = 0, skipped = 0;
        var failed = new List<string>();

        foreach (var source in sources)
        {
            var configuration = await configurations.GetOrCreateAsync(source.Source, cancellationToken);
            JobSourceBatch batch;

            try
            {
                batch = await source.FetchAsync(configuration.Checkpoint, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogSourceFailed(logger, exception, source.Source.Name);
                failed.Add(source.Source.Name);
                continue;
            }

            var now = time.GetUtcNow();

            foreach (var posting in batch.Postings)
            {
                found++;

                if (TryCreate(source.Source, posting, now) is not { } job)
                {
                    skipped++;
                    continue;
                }

                if (await jobs.AddIfNewAsync(job, cancellationToken))
                {
                    added++;
                }
            }

            configuration.RecordCheck(batch.Checkpoint, now);
            await configurations.SaveAsync(configuration, cancellationToken);
            LogSourceCollected(logger, source.Source.Name, batch.Postings.Count);
        }

        return new CollectJobsResult(found, added, skipped, failed);
    }

    private Job? TryCreate(JobSource source, JobPosting posting, DateTimeOffset now)
    {
        try
        {
            return Job.Create(
                source, posting.Title, posting.Url, now, posting.ExternalId, posting.Company, posting.Location, posting.WorkMode, posting.Description);
        }
        catch (DomainException exception)
        {
            LogInvalidPosting(logger, source.Name, exception.Message);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao consultar a fonte {Source}; as outras seguem.")]
    private static partial void LogSourceFailed(ILogger logger, Exception exception, string source);

    [LoggerMessage(Level = LogLevel.Information, Message = "Fonte {Source} consultada: {Count} vaga(s) recebida(s).")]
    private static partial void LogSourceCollected(ILogger logger, string source, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Vaga inválida ignorada na fonte {Source}: {Reason}")]
    private static partial void LogInvalidPosting(ILogger logger, string source, string reason);
}
