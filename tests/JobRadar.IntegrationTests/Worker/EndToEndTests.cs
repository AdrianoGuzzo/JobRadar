using JobRadar.Domain.Jobs;
using JobRadar.Domain.Notifications;
using JobRadar.IntegrationTests.Discord;
using JobRadar.Infrastructure.LinkedIn;
using JobRadar.Worker.Jobs;
using JobRadar.Worker.Startup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using MimeKit;

namespace JobRadar.IntegrationTests.Worker;

/// <summary>
/// O fluxo inteiro pela composição real do Worker: alerta do LinkedIn (fixture
/// .eml) → Postgres → Claude Code (FakeClaude, score 92) → Discord (servidor falso).
/// Só a caixa IMAP é trocada por uma em memória; ela tem teste próprio.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EndToEndTests(PostgresFixture postgres) : IAsyncDisposable
{
    private readonly FakeDiscordServer _discord = new();

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _discord.DisposeAsync();

    [Fact]
    public async Task Should_Notify_Each_Compatible_Job_Exactly_Once()
    {
        var (database, provider) = await StartAsync();
        await using var _ = provider;
        var jobs = provider.GetRequiredService<RecurringJobs>();

        await RunCycleAsync(jobs);
        await RunCycleAsync(jobs);

        _discord.Count("/messages").Should().Be(3);
        await using var context = database.CreateContext();
        (await context.Jobs.CountAsync(Ct)).Should().Be(3);
        (await context.Jobs.AllAsync(job => job.AnalysisStatus == AnalysisStatus.Analyzed, Ct)).Should().BeTrue();
        (await context.JobMatches.CountAsync(Ct)).Should().Be(3);
        (await context.JobMatches.ToListAsync(Ct)).Should().AllSatisfy(match => match.HasInsufficientInformation.Should().BeTrue("o alerta não traz a descrição"));
        (await context.Notifications.CountAsync(n => n.Status == NotificationStatus.Sent, Ct)).Should().Be(3);
        _discord.Requests.Where(r => r.Method == "POST").Should().AllSatisfy(r => r.Body.Should().Contain("Informações não disponíveis"));
    }

    [Fact]
    public async Task Should_Keep_Analysis_When_Discord_Fails_And_Deliver_On_Retry()
    {
        _discord.Respond("/messages", 503, """{"message":"unavailable","code":0}""")
            .Respond("/messages", 503, """{"message":"unavailable","code":0}""")
            .Respond("/messages", 503, """{"message":"unavailable","code":0}""");
        var (database, provider) = await StartAsync();
        await using var _ = provider;
        var jobs = provider.GetRequiredService<RecurringJobs>();

        await RunCycleAsync(jobs);

        await using (var context = database.CreateContext())
        {
            (await context.JobMatches.CountAsync(Ct)).Should().Be(3);
            (await context.Notifications.CountAsync(n => n.Status == NotificationStatus.RetryScheduled, Ct)).Should().Be(3);
        }

        _clock.Advance(TimeSpan.FromMinutes(5));
        await jobs.NotifyAsync(Ct);

        await using (var context = database.CreateContext())
        {
            (await context.Notifications.CountAsync(n => n.Status == NotificationStatus.Sent, Ct)).Should().Be(3);
        }
    }

    [Fact]
    public async Task Should_Store_Analysis_But_Not_Notify_Below_Minimum_Score()
    {
        var (database, provider) = await StartAsync(settings => settings["Notification:MinimumScore"] = "95");
        await using var _ = provider;

        await RunCycleAsync(provider.GetRequiredService<RecurringJobs>());

        _discord.Count("/messages").Should().Be(0);
        await using var context = database.CreateContext();
        (await context.JobMatches.CountAsync(Ct)).Should().Be(3);
        (await context.Notifications.CountAsync(Ct)).Should().Be(0);
    }

    private async Task<(TestDatabase Database, ServiceProvider Provider)> StartAsync(Action<Dictionary<string, string?>>? configure = null)
    {
        var database = new TestDatabase(await postgres.CreateEmptyDatabaseAsync());
        var settings = WorkerTestHost.Settings(database.ConnectionString, _discord.BaseUrl);
        configure?.Invoke(settings);

        var provider = WorkerTestHost.Build(settings, services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IAlertMailbox>(new RepeatingMailbox()));
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(_clock));
        });

        await provider.GetRequiredService<StartupTasks>().RunAsync(Ct);
        return (database, provider);
    }

    private static async Task RunCycleAsync(RecurringJobs jobs)
    {
        await jobs.CollectAsync(Ct);
        await jobs.AnalyzeAsync(Ct);
        await jobs.NotifyAsync(Ct);
    }

    /// <summary>Entrega o mesmo alerta toda vez, como se chegasse de novo.</summary>
    private sealed class RepeatingMailbox : IAlertMailbox
    {
        private int _reads;

        public Task<MailboxBatch> FetchAsync(string? checkpoint, CancellationToken cancellationToken)
        {
            var message = MimeMessage.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LinkedIn", "alerta-html-tres-vagas.eml"), cancellationToken);
            return Task.FromResult(new MailboxBatch([message], $"1:{++_reads}"));
        }
    }
}
