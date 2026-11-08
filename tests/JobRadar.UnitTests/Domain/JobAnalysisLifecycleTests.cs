using JobRadar.Domain.Common;
using JobRadar.Domain.Jobs;

namespace JobRadar.UnitTests.Domain;

public sealed class JobAnalysisLifecycleTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);

    private readonly RetryPolicy _retry = new(maxAttempts: 2, baseDelay: TimeSpan.FromMinutes(1), maxDelay: TimeSpan.FromHours(1));

    [Fact]
    public void Should_Lease_Job_While_Analyzing_And_Release_It_When_Lease_Expires()
    {
        var job = TestData.Job();

        job.StartAnalysis(Now, Lease);

        job.AnalysisStatus.Should().Be(AnalysisStatus.Analyzing);
        job.IsDueForAnalysis(Now.AddMinutes(9)).Should().BeFalse();
        job.IsDueForAnalysis(Now.AddMinutes(11)).Should().BeTrue("um worker que morreu no meio não pode prender a vaga para sempre");
    }

    [Fact]
    public void Should_Not_Be_Due_After_Analyzed()
    {
        var job = TestData.Job();
        job.StartAnalysis(Now, Lease);

        job.MarkAnalyzed();

        job.AnalysisStatus.Should().Be(AnalysisStatus.Analyzed);
        job.IsDueForAnalysis(Now.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void Should_Schedule_Retry_When_Analysis_Fails_Transiently()
    {
        var job = TestData.Job();
        job.StartAnalysis(Now, Lease);

        job.RecordAnalysisFailure(FailureKind.Transient, "timeout", _retry, Now);

        job.AnalysisStatus.Should().Be(AnalysisStatus.RetryScheduled);
        job.AnalysisAttempts.Should().Be(1);
        job.LastAnalysisError.Should().Be("timeout");
        job.IsDueForAnalysis(Now.AddSeconds(59)).Should().BeFalse();
        job.IsDueForAnalysis(Now.AddMinutes(1)).Should().BeTrue();
    }

    [Fact]
    public void Should_Mark_Failed_After_Max_Attempts_Keeping_The_Job()
    {
        var job = TestData.Job();
        job.RecordAnalysisFailure(FailureKind.Transient, "timeout", _retry, Now);

        job.RecordAnalysisFailure(FailureKind.Transient, "timeout", _retry, Now.AddMinutes(1));

        job.AnalysisStatus.Should().Be(AnalysisStatus.Failed);
        job.AnalysisAttempts.Should().Be(2);
        job.IsDueForAnalysis(Now.AddDays(1)).Should().BeFalse();
        job.Title.Should().NotBeEmpty();
    }

    [Fact]
    public void Should_Mark_Failed_On_Permanent_Failure()
    {
        var job = TestData.Job();

        job.RecordAnalysisFailure(FailureKind.Permanent, "não autenticado", _retry, Now);

        job.AnalysisStatus.Should().Be(AnalysisStatus.Failed);
    }

    [Fact]
    public void Should_Truncate_Long_Error_Messages()
    {
        var job = TestData.Job();

        job.RecordAnalysisFailure(FailureKind.Transient, new string('x', 5000), _retry, Now);

        job.LastAnalysisError!.Length.Should().BeLessThanOrEqualTo(1000);
    }
}
