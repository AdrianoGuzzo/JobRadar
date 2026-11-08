using System.Collections.Concurrent;
using JobRadar.Application.Abstractions;
using JobRadar.Application.Analysis;
using JobRadar.Application.Notifications;
using JobRadar.Domain.Analysis;
using JobRadar.Domain.Common;
using JobRadar.Domain.Jobs;
using JobRadar.Domain.Notifications;
using JobRadar.Domain.Profiles;
using JobRadar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace JobRadar.IntegrationTests.Persistence;

/// <summary>Regras 9 e 10: execuções concorrentes contra o banco real não duplicam trabalho.</summary>
[Trait("Category", "Integration")]
public sealed class ConcurrencyTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_Store_Job_Once_When_Collected_Concurrently()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var repository = new JobRepository(database);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Task.Run(() => repository.AddIfNewAsync(IntegrationData.Job(externalId: "777", title: "Dev .NET"), Ct))));

        results.Count(added => added).Should().Be(1);
    }

    [Fact]
    public async Task Should_Not_Claim_Same_Job_Twice_Concurrently()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var repository = new JobRepository(database);

        for (var i = 0; i < 20; i++)
        {
            await repository.AddIfNewAsync(IntegrationData.Job(title: $"Vaga {i}"), Ct);
        }

        var claims = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            Task.Run(() => repository.ClaimForAnalysisAsync(IntegrationData.Now, TimeSpan.FromMinutes(10), 10, Ct))));

        var claimedIds = claims.SelectMany(claim => claim).Select(job => job.Id).ToList();
        claimedIds.Should().OnlyHaveUniqueItems();
        claimedIds.Should().HaveCount(20);
    }

    [Fact]
    public async Task Should_Not_Analyze_Same_Job_Twice_Concurrently()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var profile = IntegrationData.Profile();
        await new CandidateProfileRepository(database).SaveAsync(profile, Ct);

        for (var i = 0; i < 10; i++)
        {
            await new JobRepository(database).AddIfNewAsync(IntegrationData.Job(title: $"Vaga {i}"), Ct);
        }

        var analyzer = new CountingAnalyzer();

        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => AnalyzeUseCase(database, analyzer).ExecuteAsync(Ct))));

        analyzer.CallsByJob.Values.Should().AllSatisfy(calls => calls.Should().Be(1));
        analyzer.CallsByJob.Should().HaveCount(10);
        await using var context = database.CreateContext();
        (await context.JobMatches.CountAsync(Ct)).Should().Be(10);
        (await context.Jobs.CountAsync(job => job.AnalysisStatus == AnalysisStatus.Analyzed, Ct)).Should().Be(10);
    }

    [Fact]
    public async Task Should_Send_Single_Notification_When_Workers_Run_Concurrently()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var profile = IntegrationData.Profile();
        await new CandidateProfileRepository(database).SaveAsync(profile, Ct);
        var job = IntegrationData.Job();
        await new JobRepository(database).AddIfNewAsync(job, Ct);
        await new JobMatchRepository(database).AddIfNewAsync(JobMatch.Create(job, profile, IntegrationData.Analysis(90), IntegrationData.Now), Ct);
        var discord = new CountingDiscord();

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => NotifyUseCase(database, discord).ExecuteAsync(Ct))));

        discord.Sends.Should().Be(1);
        await using var context = database.CreateContext();
        var notification = await context.Notifications.SingleAsync(Ct);
        notification.Status.Should().Be(NotificationStatus.Sent);
    }

    private static AnalyzePendingJobs AnalyzeUseCase(TestDatabase database, IJobAnalyzer analyzer) => new(
        new JobRepository(database),
        new JobMatchRepository(database),
        new CandidateProfileRepository(database),
        analyzer,
        RetryPolicy.Default,
        new AnalyzePendingJobsOptions { BatchSize = 4, MaxConcurrency = 2 },
        new FakeTimeProvider(IntegrationData.Now),
        NullLogger<AnalyzePendingJobs>.Instance);

    private static NotifyMatches NotifyUseCase(TestDatabase database, IDiscordNotificationService discord) => new(
        new NotificationRepository(database),
        new CandidateProfileRepository(database),
        discord,
        new NotificationPolicy(),
        RetryPolicy.Default,
        new NotifyMatchesOptions(),
        new FakeTimeProvider(IntegrationData.Now),
        NullLogger<NotifyMatches>.Instance);

    private sealed class CountingAnalyzer : IJobAnalyzer
    {
        public ConcurrentDictionary<Guid, int> CallsByJob { get; } = new();

        public async Task<JobAnalysis> AnalyzeAsync(CandidateProfile profile, Job job, CancellationToken cancellationToken)
        {
            CallsByJob.AddOrUpdate(job.Id, 1, (_, calls) => calls + 1);
            await Task.Delay(20, cancellationToken);
            return IntegrationData.Analysis();
        }
    }

    private sealed class CountingDiscord : IDiscordNotificationService
    {
        private int _sends;

        public int Sends => _sends;

        public async Task<string> SendAsync(JobNotificationMessage message, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _sends);
            await Task.Delay(100, cancellationToken);
            return "1";
        }
    }
}
