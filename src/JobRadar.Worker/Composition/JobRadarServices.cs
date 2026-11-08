using System.ComponentModel.DataAnnotations;
using JobRadar.Application.Abstractions;
using JobRadar.Application.Analysis;
using JobRadar.Application.Jobs;
using JobRadar.Application.Notifications;
using JobRadar.Application.Profiles;
using JobRadar.Domain.Common;
using JobRadar.Domain.Notifications;
using JobRadar.Infrastructure.Claude;
using JobRadar.Infrastructure.Discord;
using JobRadar.Infrastructure.LinkedIn;
using JobRadar.Infrastructure.Persistence;
using JobRadar.Worker.Jobs;
using JobRadar.Worker.Startup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace JobRadar.Worker.Composition;

/// <summary>Limite de notificação (seção <c>Notification</c>).</summary>
public sealed class NotificationOptions
{
    public const string Section = "Notification";

    [Range(0, 100)]
    public int MinimumScore { get; set; } = NotificationPolicy.DefaultMinimumScore;
}

public static class JobRadarServices
{
    /// <summary>
    /// Tudo o que o Worker precisa, menos o host (logging, Hangfire). Cada opção é
    /// validada no startup: faltando segredo ou valor fora da faixa, o Worker não sobe.
    /// </summary>
    public static IServiceCollection AddJobRadarCore(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("JobRadar");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Defina a conexão com o PostgreSQL em ConnectionStrings__JobRadar.");
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContextFactory<JobRadarDbContext>(options => options.UseJobRadarPostgres(connectionString));

        AddValidatedOptions<ClaudeCliOptions>(services, configuration, ClaudeCliOptions.Section);
        AddValidatedOptions<DiscordOptions>(services, configuration, DiscordOptions.Section);
        AddValidatedOptions<ImapOptions>(services, configuration, ImapOptions.Section);
        AddValidatedOptions<MonitoringOptions>(services, configuration, MonitoringOptions.Section);
        AddValidatedOptions<NotificationOptions>(services, configuration, NotificationOptions.Section);
        AddValidatedOptions<ProfileOptions>(services, configuration, ProfileOptions.Section);
        services.AddOptions<AnalyzePendingJobsOptions>().Bind(configuration.GetSection("Analysis"));
        services.AddOptions<NotifyMatchesOptions>().Bind(configuration.GetSection(NotificationOptions.Section));

        services.AddSingleton(RetryPolicy.Default);
        services.AddSingleton(provider => new NotificationPolicy(provider.GetRequiredService<IOptions<NotificationOptions>>().Value.MinimumScore));
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<AnalyzePendingJobsOptions>>().Value);
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<NotifyMatchesOptions>>().Value);

        services.AddSingleton<IJobRepository, JobRepository>();
        services.AddSingleton<IJobSearchConfigurationRepository, JobSearchConfigurationRepository>();
        services.AddSingleton<ICandidateProfileRepository, CandidateProfileRepository>();
        services.AddSingleton<IJobMatchRepository, JobMatchRepository>();
        services.AddSingleton<INotificationRepository, NotificationRepository>();

        // Um único analisador: o semáforo dele é o limite global de processos do Claude.
        services.AddSingleton<ClaudeCodeJobAnalyzer>();
        services.AddSingleton<IJobAnalyzer>(provider => provider.GetRequiredService<ClaudeCodeJobAnalyzer>());
        services.AddSingleton<ClaudeCliVersionCheck>();

        services.AddSingleton<DiscordNotificationService>();
        services.AddSingleton<IDiscordNotificationService>(provider => provider.GetRequiredService<DiscordNotificationService>());

        services.AddSingleton<IAlertMailbox, ImapAlertMailbox>();
        services.AddSingleton<IJobSource, LinkedInEmailJobSource>();

        services.AddTransient<CollectJobs>();
        services.AddTransient<AnalyzePendingJobs>();
        services.AddTransient<NotifyMatches>();
        services.AddTransient<SyncCandidateProfile>();
        services.AddTransient<StartupTasks>();
        services.AddTransient<RecurringJobs>();

        return services;
    }

    private static void AddValidatedOptions<TOptions>(IServiceCollection services, IConfiguration configuration, string section)
        where TOptions : class =>
        services.AddOptions<TOptions>().Bind(configuration.GetSection(section)).ValidateDataAnnotations().ValidateOnStart();
}
