using JobRadar.Application.Abstractions;
using JobRadar.Domain.Analysis;
using JobRadar.Domain.Common;
using JobRadar.Domain.Jobs;
using JobRadar.Domain.Profiles;
using Microsoft.Extensions.Logging;

namespace JobRadar.Application.Analysis;

public sealed class AnalyzePendingJobsOptions
{
    /// <summary>Quantas vagas cada execução reivindica.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>Quantas análises rodam ao mesmo tempo nesta execução.</summary>
    public int MaxConcurrency { get; set; } = 2;

    /// <summary>Por quanto tempo a vaga fica reservada a esta execução.</summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(15);
}

public sealed record AnalyzePendingJobsResult(int Claimed, int Analyzed, int Failed, bool ProfileMissing);

/// <summary>
/// Analisa as vagas pendentes contra o perfil atual. Cada vaga é analisada uma
/// vez por versão do perfil; uma falha do analisador nunca perde a vaga, só
/// agenda nova tentativa ou a marca como falha (ADR-002).
/// </summary>
public sealed partial class AnalyzePendingJobs(
    IJobRepository jobs,
    IJobMatchRepository matches,
    ICandidateProfileRepository profiles,
    IJobAnalyzer analyzer,
    RetryPolicy retryPolicy,
    AnalyzePendingJobsOptions options,
    TimeProvider time,
    ILogger<AnalyzePendingJobs> logger)
{
    public async Task<AnalyzePendingJobsResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var profile = await profiles.GetActiveAsync(cancellationToken);

        if (profile is null)
        {
            LogNoProfile(logger);
            return new AnalyzePendingJobsResult(0, 0, 0, ProfileMissing: true);
        }

        var claimed = await jobs.ClaimForAnalysisAsync(time.GetUtcNow(), options.Lease, options.BatchSize, cancellationToken);
        var analyzed = 0;
        var failed = 0;

        var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.MaxConcurrency), CancellationToken = cancellationToken };

        await Parallel.ForEachAsync(claimed, parallel, async (job, token) =>
        {
            if (await AnalyzeAsync(profile, job, token))
            {
                Interlocked.Increment(ref analyzed);
            }
            else
            {
                Interlocked.Increment(ref failed);
            }
        });

        return new AnalyzePendingJobsResult(claimed.Count, analyzed, failed, ProfileMissing: false);
    }

    private async Task<bool> AnalyzeAsync(CandidateProfile profile, Job job, CancellationToken cancellationToken)
    {
        var succeeded = true;

        if (await matches.ExistsAsync(job.Id, profile.Version, cancellationToken))
        {
            job.MarkAnalyzed();
        }
        else
        {
            try
            {
                var analysis = await analyzer.AnalyzeAsync(profile, job, cancellationToken);
                await matches.AddIfNewAsync(JobMatch.Create(job, profile, analysis, time.GetUtcNow()), cancellationToken);
                job.MarkAnalyzed();
                LogAnalyzed(logger, job.Id, analysis.Score.Value);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                var (kind, message) = exception is JobAnalysisException analysisException
                    ? (analysisException.Kind, analysisException.Message)
                    : (FailureKind.Transient, $"Erro inesperado na análise ({exception.GetType().Name}).");

                job.RecordAnalysisFailure(kind, message, retryPolicy, time.GetUtcNow());
                LogAnalysisFailed(logger, exception, job.Id, kind, job.AnalysisAttempts);
                succeeded = false;
            }
        }

        await jobs.UpdateAsync(job, cancellationToken);
        return succeeded;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nenhum perfil de candidato cadastrado; a análise de vagas está parada.")]
    private static partial void LogNoProfile(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Vaga {JobId} analisada: score {Score}.")]
    private static partial void LogAnalyzed(ILogger logger, Guid jobId, int score);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao analisar a vaga {JobId} ({Kind}, tentativa {Attempt}).")]
    private static partial void LogAnalysisFailed(ILogger logger, Exception exception, Guid jobId, FailureKind kind, int attempt);
}
