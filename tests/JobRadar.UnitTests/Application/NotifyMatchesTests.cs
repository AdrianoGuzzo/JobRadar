using JobRadar.Application.Abstractions;
using JobRadar.Application.Notifications;
using JobRadar.Domain.Analysis;
using JobRadar.Domain.Common;
using JobRadar.Domain.Jobs;
using JobRadar.Domain.Notifications;
using JobRadar.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace JobRadar.UnitTests.Application;

public sealed class NotifyMatchesTests
{
    private readonly InMemoryJobRepository _jobs = new();

    private readonly InMemoryJobMatchRepository _matches = new();

    private readonly InMemoryCandidateProfileRepository _profiles = new() { Profile = TestData.Profile() };

    private readonly InMemoryNotificationRepository _notifications;

    private readonly FakeTimeProvider _time = new(TestData.Now);

    private readonly RetryPolicy _retry = new(maxAttempts: 3, baseDelay: TimeSpan.FromMinutes(1), maxDelay: TimeSpan.FromHours(1));

    public NotifyMatchesTests()
    {
        _notifications = new InMemoryNotificationRepository(_jobs, _matches);
    }

    [Fact]
    public async Task Should_Notify_Match_At_Or_Above_Threshold()
    {
        var job = await AddAnalyzedJob(score: 92, description: "APIs REST em .NET");
        var discord = new FakeDiscordNotificationService();

        var result = await UseCase(discord).ExecuteAsync(CancellationToken.None);

        result.Sent.Should().Be(1);
        var message = discord.Sent.Should().ContainSingle().Subject;
        message.Title.Should().Be(job.Title);
        message.Company.Should().Be("Agro Tech");
        message.Score.Should().Be(92);
        message.WorkMode.Should().Be(WorkMode.Remote);
        message.Location.Should().Be("Brasil");
        message.SourceName.Should().Be("LinkedIn");
        message.Strengths.Should().Equal("Experiência com C# e .NET");
        message.Gaps.Should().Equal("Inglês avançado exigido");
        message.Url.Should().Be(job.Url);
        _notifications.All.Should().ContainSingle().Which.Status.Should().Be(NotificationStatus.Sent);
    }

    [Fact]
    public async Task Should_Not_Notify_Match_Below_Threshold()
    {
        await AddAnalyzedJob(score: 74);
        var discord = new FakeDiscordNotificationService();

        await UseCase(discord, minimumScore: 75).ExecuteAsync(CancellationToken.None);

        discord.Attempts.Should().Be(0);
        _notifications.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Should_Not_Notify_When_Job_Was_Already_Notified()
    {
        await AddAnalyzedJob(score: 90);
        var discord = new FakeDiscordNotificationService();
        var useCase = UseCase(discord);

        await useCase.ExecuteAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromHours(1));
        var second = await useCase.ExecuteAsync(CancellationToken.None);

        second.Sent.Should().Be(0);
        discord.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task Should_Keep_Match_When_Discord_Fails()
    {
        await AddAnalyzedJob(score: 90);
        var discord = new FakeDiscordNotificationService().FailNext(new NotificationDeliveryException(FailureKind.Transient, "HTTP 503"));

        var result = await UseCase(discord).ExecuteAsync(CancellationToken.None);

        result.Failed.Should().Be(1);
        _matches.All.Should().ContainSingle();
        var notification = _notifications.All.Should().ContainSingle().Subject;
        notification.Status.Should().Be(NotificationStatus.RetryScheduled);
        notification.LastError.Should().Be("HTTP 503");
    }

    [Fact]
    public async Task Should_Retry_Failed_Notification_After_Backoff()
    {
        await AddAnalyzedJob(score: 90);
        var discord = new FakeDiscordNotificationService().FailNext(new NotificationDeliveryException(FailureKind.Transient, "HTTP 429"));
        var useCase = UseCase(discord);

        await useCase.ExecuteAsync(CancellationToken.None);
        await useCase.ExecuteAsync(CancellationToken.None);
        discord.Attempts.Should().Be(1, "a nova tentativa só vence depois do backoff");

        _time.Advance(TimeSpan.FromMinutes(1));
        await useCase.ExecuteAsync(CancellationToken.None);

        discord.Sent.Should().ContainSingle();
        _notifications.All.Single().Status.Should().Be(NotificationStatus.Sent);
    }

    [Fact]
    public async Task Should_Give_Up_On_Permanent_Discord_Failure()
    {
        await AddAnalyzedJob(score: 90);
        var discord = new FakeDiscordNotificationService().FailNext(new NotificationDeliveryException(FailureKind.Permanent, "HTTP 401: token inválido"));

        await UseCase(discord).ExecuteAsync(CancellationToken.None);

        _notifications.All.Single().Status.Should().Be(NotificationStatus.Failed);
        _matches.All.Should().ContainSingle();
    }

    [Fact]
    public async Task Should_Treat_Unexpected_Exception_As_Transient_Failure()
    {
        await AddAnalyzedJob(score: 90);
        var discord = new FakeDiscordNotificationService().FailNext(new HttpRequestException("conexão recusada"));

        await UseCase(discord).ExecuteAsync(CancellationToken.None);

        _notifications.All.Single().Status.Should().Be(NotificationStatus.RetryScheduled);
    }

    [Fact]
    public async Task Should_Flag_Insufficient_Information_In_Notification()
    {
        await AddAnalyzedJob(score: 90, description: null);
        var discord = new FakeDiscordNotificationService();

        await UseCase(discord).ExecuteAsync(CancellationToken.None);

        discord.Sent.Single().MissingInformation.Should().Contain("descrição");
    }

    [Fact]
    public async Task Should_Not_Duplicate_When_Two_Runs_Overlap()
    {
        await AddAnalyzedJob(score: 90);
        var discord = new FakeDiscordNotificationService { Delay = TimeSpan.FromMilliseconds(200) };

        await Task.WhenAll(UseCase(discord).ExecuteAsync(CancellationToken.None), UseCase(discord).ExecuteAsync(CancellationToken.None));

        discord.Attempts.Should().Be(1);
        _notifications.All.Should().ContainSingle();
    }

    [Fact]
    public async Task Should_Do_Nothing_When_No_Profile_Exists()
    {
        await AddAnalyzedJob(score: 90);
        _profiles.Profile = null;
        var discord = new FakeDiscordNotificationService();

        await UseCase(discord).ExecuteAsync(CancellationToken.None);

        discord.Attempts.Should().Be(0);
    }

    private async Task<Job> AddAnalyzedJob(int score, string? description = "APIs REST em .NET")
    {
        var job = TestData.Job(externalId: Guid.NewGuid().ToString("N"), description: description);
        await _jobs.AddIfNewAsync(job, CancellationToken.None);
        job.MarkAnalyzed();
        await _matches.AddIfNewAsync(JobMatch.Create(job, _profiles.Profile!, TestData.Analysis(score), TestData.Now), CancellationToken.None);
        return job;
    }

    private NotifyMatches UseCase(FakeDiscordNotificationService discord, int minimumScore = 75) => new(
        _notifications,
        _profiles,
        discord,
        new NotificationPolicy(minimumScore),
        _retry,
        new NotifyMatchesOptions { BatchSize = 20, Lease = TimeSpan.FromMinutes(2) },
        _time,
        NullLogger<NotifyMatches>.Instance);
}
