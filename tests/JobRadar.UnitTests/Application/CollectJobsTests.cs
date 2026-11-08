using JobRadar.Application.Abstractions;
using JobRadar.Application.Jobs;
using JobRadar.Domain.Jobs;
using JobRadar.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace JobRadar.UnitTests.Application;

public sealed class CollectJobsTests
{
    private static readonly JobSource OtherSource = new(99, "Outra fonte");

    private readonly InMemoryJobRepository _jobs = new();

    private readonly InMemoryJobSearchConfigurationRepository _configurations = new();

    private readonly FakeTimeProvider _time = new(TestData.Now);

    [Fact]
    public async Task Should_Store_New_Jobs_From_Source()
    {
        var source = new FakeJobSource(JobSource.LinkedIn).Returns("cp-1", Posting("1", "Dev .NET"), Posting("2", "Dev React"));

        var result = await UseCase(source).ExecuteAsync(CancellationToken.None);

        result.Found.Should().Be(2);
        result.Added.Should().Be(2);
        _jobs.All.Select(job => job.Title).Should().BeEquivalentTo(["Dev .NET", "Dev React"]);
        _jobs.All.Should().AllSatisfy(job => job.FoundAt.Should().Be(TestData.Now));
    }

    [Fact]
    public async Task Should_Not_Store_Duplicate_By_ExternalId()
    {
        var source = new FakeJobSource(JobSource.LinkedIn).Returns(
            "cp-1",
            Posting("1", "Dev .NET", url: "https://www.linkedin.com/jobs/view/1/?trackingId=a"),
            Posting("1", "Desenvolvedor .NET (editado)", url: "https://www.linkedin.com/jobs/view/1/?trackingId=b"));

        var result = await UseCase(source).ExecuteAsync(CancellationToken.None);

        result.Added.Should().Be(1);
        _jobs.All.Should().ContainSingle();
    }

    [Fact]
    public async Task Should_Not_Store_Duplicate_By_Fingerprint()
    {
        var source = new FakeJobSource(JobSource.LinkedIn).Returns(
            "cp-1",
            Posting(null, "Dev .NET Sênior", url: "https://example.com/a"),
            Posting(null, "dev .net senior", url: "https://example.com/b"));

        await UseCase(source).ExecuteAsync(CancellationToken.None);

        _jobs.All.Should().ContainSingle();
    }

    [Fact]
    public async Task Should_Not_Store_Job_Already_Collected_In_Previous_Run()
    {
        var source = new FakeJobSource(JobSource.LinkedIn).Returns("cp-1", Posting("1", "Dev .NET")).Returns("cp-2", Posting("1", "Dev .NET"));
        var useCase = UseCase(source);

        await useCase.ExecuteAsync(CancellationToken.None);
        var second = await useCase.ExecuteAsync(CancellationToken.None);

        second.Added.Should().Be(0);
        _jobs.All.Should().ContainSingle();
    }

    [Fact]
    public async Task Should_Continue_When_One_Source_Fails()
    {
        var failing = new FakeJobSource(OtherSource).Throws(new InvalidOperationException("IMAP fora do ar"));
        var working = new FakeJobSource(JobSource.LinkedIn).Returns("cp-1", Posting("1", "Dev .NET"));

        var result = await UseCase(failing, working).ExecuteAsync(CancellationToken.None);

        result.FailedSources.Should().Equal("Outra fonte");
        _jobs.All.Should().ContainSingle();
    }

    [Fact]
    public async Task Should_Pass_Previous_Checkpoint_And_Record_Last_Check()
    {
        var source = new FakeJobSource(JobSource.LinkedIn).Returns("cp-1").Returns("cp-2");
        var useCase = UseCase(source);

        await useCase.ExecuteAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(15));
        await useCase.ExecuteAsync(CancellationToken.None);

        source.ReceivedCheckpoints.Should().Equal(null, "cp-1");
        var configuration = _configurations.Items[JobSource.LinkedIn.Id];
        configuration.Checkpoint.Should().Be("cp-2");
        configuration.LastCheckedAt.Should().Be(TestData.Now.AddMinutes(15));
    }

    [Fact]
    public async Task Should_Not_Advance_Checkpoint_When_Source_Fails()
    {
        var source = new FakeJobSource(JobSource.LinkedIn).Returns("cp-1").Throws(new TimeoutException());
        var useCase = UseCase(source);

        await useCase.ExecuteAsync(CancellationToken.None);
        await useCase.ExecuteAsync(CancellationToken.None);

        _configurations.Items[JobSource.LinkedIn.Id].Checkpoint.Should().Be("cp-1");
    }

    [Fact]
    public async Task Should_Skip_Invalid_Posting_Without_Losing_Others()
    {
        var source = new FakeJobSource(JobSource.LinkedIn).Returns(
            "cp-1",
            Posting("1", "Dev .NET", url: "javascript:alert(1)"),
            Posting("2", "Dev React"));

        var result = await UseCase(source).ExecuteAsync(CancellationToken.None);

        result.Skipped.Should().Be(1);
        _jobs.All.Should().ContainSingle(job => job.Title == "Dev React");
    }

    private CollectJobs UseCase(params IJobSource[] sources) =>
        new(sources, _jobs, _configurations, _time, NullLogger<CollectJobs>.Instance);

    private static JobPosting Posting(string? externalId, string title, string? url = null) => new(
        Title: title,
        Url: url ?? $"https://www.linkedin.com/jobs/view/{externalId}/",
        ExternalId: externalId,
        Company: "Agro Tech",
        Location: "Brasil",
        WorkMode: WorkMode.Remote,
        Description: null);
}
