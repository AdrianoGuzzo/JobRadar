using JobRadar.Application.Abstractions;
using JobRadar.Application.Analysis;
using JobRadar.Domain.Analysis;
using JobRadar.Domain.Common;
using JobRadar.Domain.Jobs;
using JobRadar.Domain.Profiles;
using JobRadar.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace JobRadar.UnitTests.Application;

public sealed class AnalyzePendingJobsTests
{
    private readonly InMemoryJobRepository _jobs = new();

    private readonly InMemoryJobMatchRepository _matches = new();

    private readonly InMemoryCandidateProfileRepository _profiles = new() { Profile = TestData.Profile() };

    private readonly FakeTimeProvider _time = new(TestData.Now);

    private readonly RetryPolicy _retry = new(maxAttempts: 3, baseDelay: TimeSpan.FromMinutes(1), maxDelay: TimeSpan.FromHours(1));

    private CandidateProfile Profile => _profiles.Profile!;

    [Fact]
    public async Task Should_Analyze_Pending_Job_And_Record_Match()
    {
        var job = await AddJob("Dev .NET");
        var analyzer = new FakeJobAnalyzer().For("Dev .NET", () => TestData.Analysis(score: 88));

        var result = await UseCase(analyzer).ExecuteAsync(CancellationToken.None);

        result.Analyzed.Should().Be(1);
        analyzer.Received.Should().Equal((Profile.Id, job.Id));
        var match = _matches.All.Should().ContainSingle().Subject;
        match.Score.Should().Be(88);
        match.ProfileVersion.Should().Be(Profile.Version);
        job.AnalysisStatus.Should().Be(AnalysisStatus.Analyzed);
    }

    [Fact]
    public async Task Should_Not_Reanalyze_When_Match_Exists_For_Profile_Version()
    {
        var job = await AddJob("Dev .NET");
        await _matches.AddIfNewAsync(JobMatch.Create(job, Profile, TestData.Analysis(), TestData.Now), CancellationToken.None);
        var analyzer = new FakeJobAnalyzer();

        await UseCase(analyzer).ExecuteAsync(CancellationToken.None);

        analyzer.Calls.Should().Be(0);
        job.AnalysisStatus.Should().Be(AnalysisStatus.Analyzed);
        _matches.All.Should().ContainSingle();
    }

    [Fact]
    public async Task Should_Not_Reanalyze_Already_Analyzed_Job_On_Next_Run()
    {
        await AddJob("Dev .NET");
        var analyzer = new FakeJobAnalyzer();
        var useCase = UseCase(analyzer);

        await useCase.ExecuteAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromHours(1));
        await useCase.ExecuteAsync(CancellationToken.None);

        analyzer.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Should_Keep_Job_When_Analyzer_Fails()
    {
        var job = await AddJob("Dev .NET");
        var analyzer = new FakeJobAnalyzer().For("Dev .NET", () => throw new JobAnalysisException(FailureKind.Transient, "Claude excedeu o tempo limite."));

        var result = await UseCase(analyzer).ExecuteAsync(CancellationToken.None);

        result.Failed.Should().Be(1);
        _jobs.All.Should().ContainSingle();
        _matches.All.Should().BeEmpty();
        job.AnalysisStatus.Should().Be(AnalysisStatus.RetryScheduled);
        job.LastAnalysisError.Should().Contain("tempo limite");
    }

    [Fact]
    public async Task Should_Retry_Job_After_Backoff()
    {
        var job = await AddJob("Dev .NET");
        var analyzer = new FakeJobAnalyzer()
            .For("Dev .NET", () => throw new JobAnalysisException(FailureKind.Transient, "rate limit"))
            .For("Dev .NET", () => TestData.Analysis());
        var useCase = UseCase(analyzer);

        await useCase.ExecuteAsync(CancellationToken.None);
        await useCase.ExecuteAsync(CancellationToken.None);
        analyzer.Calls.Should().Be(1, "a nova tentativa só vence depois do backoff");

        _time.Advance(TimeSpan.FromMinutes(1));
        await useCase.ExecuteAsync(CancellationToken.None);

        analyzer.Calls.Should().Be(2);
        job.AnalysisStatus.Should().Be(AnalysisStatus.Analyzed);
    }

    [Fact]
    public async Task Should_Mark_Job_Failed_On_Permanent_Analyzer_Failure()
    {
        var job = await AddJob("Dev .NET");
        var analyzer = new FakeJobAnalyzer().For("Dev .NET", () => throw new JobAnalysisException(FailureKind.Permanent, "Claude não autenticado."));

        await UseCase(analyzer).ExecuteAsync(CancellationToken.None);

        job.AnalysisStatus.Should().Be(AnalysisStatus.Failed);
        _jobs.All.Should().ContainSingle();
    }

    [Fact]
    public async Task Should_Treat_Unexpected_Exception_As_Transient_Failure()
    {
        var job = await AddJob("Dev .NET");
        var analyzer = new FakeJobAnalyzer().For("Dev .NET", () => throw new InvalidOperationException("bug inesperado"));

        await UseCase(analyzer).ExecuteAsync(CancellationToken.None);

        job.AnalysisStatus.Should().Be(AnalysisStatus.RetryScheduled);
    }

    [Fact]
    public async Task Should_Continue_Other_Jobs_When_One_Fails()
    {
        await AddJob("Dev .NET");
        var other = await AddJob("Dev React");
        var analyzer = new FakeJobAnalyzer().For("Dev .NET", () => throw new JobAnalysisException(FailureKind.Transient, "timeout"));

        await UseCase(analyzer).ExecuteAsync(CancellationToken.None);

        other.AnalysisStatus.Should().Be(AnalysisStatus.Analyzed);
        _matches.All.Should().ContainSingle(match => match.JobId == other.Id);
    }

    [Fact]
    public async Task Should_Do_Nothing_When_No_Profile_Exists()
    {
        var job = await AddJob("Dev .NET");
        _profiles.Profile = null;
        var analyzer = new FakeJobAnalyzer();

        var result = await UseCase(analyzer).ExecuteAsync(CancellationToken.None);

        result.ProfileMissing.Should().BeTrue();
        analyzer.Calls.Should().Be(0);
        job.AnalysisStatus.Should().Be(AnalysisStatus.Pending);
    }

    [Fact]
    public async Task Should_Limit_Parallel_Analyses()
    {
        for (var i = 0; i < 6; i++)
        {
            await AddJob($"Vaga {i}");
        }

        var analyzer = new FakeJobAnalyzer { Delay = TimeSpan.FromMilliseconds(100) };

        await UseCase(analyzer, maxConcurrency: 2).ExecuteAsync(CancellationToken.None);

        analyzer.Calls.Should().Be(6);
        analyzer.MaxConcurrent.Should().BeInRange(1, 2);
    }

    private async Task<Job> AddJob(string title)
    {
        var job = Job.Create(JobSource.LinkedIn, title, $"https://example.com/{Guid.NewGuid():N}", TestData.Now);
        await _jobs.AddIfNewAsync(job, CancellationToken.None);
        return job;
    }

    private AnalyzePendingJobs UseCase(FakeJobAnalyzer analyzer, int maxConcurrency = 2) => new(
        _jobs,
        _matches,
        _profiles,
        analyzer,
        _retry,
        new AnalyzePendingJobsOptions { BatchSize = 20, MaxConcurrency = maxConcurrency, Lease = TimeSpan.FromMinutes(10) },
        _time,
        NullLogger<AnalyzePendingJobs>.Instance);
}
