using System.ComponentModel.DataAnnotations;
using Hangfire;
using JobRadar.Application.Analysis;
using JobRadar.Application.Jobs;
using JobRadar.Application.Notifications;

namespace JobRadar.Worker.Jobs;

/// <summary>Agenda das tarefas recorrentes (seção <c>Monitoring</c>, expressões cron em UTC).</summary>
public sealed class MonitoringOptions
{
    public const string Section = "Monitoring";

    /// <summary>Leitura da caixa de alertas.</summary>
    [Required]
    public string CollectCron { get; set; } = "*/15 * * * *";

    /// <summary>Análise das vagas pendentes (o lote e a concorrência limitam o uso do Claude).</summary>
    [Required]
    public string AnalyzeCron { get; set; } = "*/2 * * * *";

    /// <summary>Envio das notificações pendentes e das novas tentativas.</summary>
    [Required]
    public string NotifyCron { get; set; } = "* * * * *";
}

/// <summary>
/// As três tarefas que o Hangfire dispara. O Hangfire é só o gatilho (ADR-004):
/// sem retry dele (o retry é por vaga e por notificação, no banco) e sem duas
/// execuções da mesma tarefa ao mesmo tempo (defesa extra; a garantia real são
/// as reivindicações no banco).
/// </summary>
public sealed class RecurringJobs(CollectJobs collect, AnalyzePendingJobs analyze, NotifyMatches notify)
{
    private const int LockTimeoutSeconds = 10;

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(LockTimeoutSeconds)]
    public Task<CollectJobsResult> CollectAsync(CancellationToken cancellationToken) => collect.ExecuteAsync(cancellationToken);

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(LockTimeoutSeconds)]
    public Task<AnalyzePendingJobsResult> AnalyzeAsync(CancellationToken cancellationToken) => analyze.ExecuteAsync(cancellationToken);

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(LockTimeoutSeconds)]
    public Task<NotifyMatchesResult> NotifyAsync(CancellationToken cancellationToken) => notify.ExecuteAsync(cancellationToken);
}

public static class RecurringJobRegistrar
{
    public const string CollectId = "jobradar:collect";

    public const string AnalyzeId = "jobradar:analyze";

    public const string NotifyId = "jobradar:notify";

    public static void Register(IRecurringJobManager manager, MonitoringOptions options)
    {
        var schedule = new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc };

        manager.AddOrUpdate<RecurringJobs>(CollectId, jobs => jobs.CollectAsync(CancellationToken.None), options.CollectCron, schedule);
        manager.AddOrUpdate<RecurringJobs>(AnalyzeId, jobs => jobs.AnalyzeAsync(CancellationToken.None), options.AnalyzeCron, schedule);
        manager.AddOrUpdate<RecurringJobs>(NotifyId, jobs => jobs.NotifyAsync(CancellationToken.None), options.NotifyCron, schedule);
    }
}
