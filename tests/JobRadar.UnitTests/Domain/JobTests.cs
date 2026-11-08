using JobRadar.Domain.Common;
using JobRadar.Domain.Jobs;

namespace JobRadar.UnitTests.Domain;

public sealed class JobTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Should_Keep_Job_When_Optional_Fields_Are_Missing()
    {
        var job = Job.Create(JobSource.LinkedIn, "Desenvolvedor .NET", "https://www.linkedin.com/jobs/view/1/", Now);

        job.Title.Should().Be("Desenvolvedor .NET");
        job.Company.Should().BeNull();
        job.Location.Should().BeNull();
        job.WorkMode.Should().Be(WorkMode.NotInformed);
        job.Description.Should().BeNull();
    }

    [Fact]
    public void Should_Report_Missing_Information()
    {
        var job = Job.Create(JobSource.LinkedIn, "Desenvolvedor .NET", "https://www.linkedin.com/jobs/view/1/", Now, company: "Agro Tech");

        job.MissingInformation().Should().BeEquivalentTo(["localização", "modalidade", "descrição"]);
    }

    [Fact]
    public void Should_Report_No_Missing_Information_When_All_Fields_Are_Present()
    {
        var job = Job.Create(
            JobSource.LinkedIn,
            "Desenvolvedor .NET",
            "https://www.linkedin.com/jobs/view/1/",
            Now,
            externalId: "1",
            company: "Agro Tech",
            location: "Brasil",
            workMode: WorkMode.Remote,
            description: "APIs em ASP.NET Core");

        job.MissingInformation().Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_Reject_Job_Without_Title(string title)
    {
        var act = () => Job.Create(JobSource.LinkedIn, title, "https://www.linkedin.com/jobs/view/1/", Now);

        act.Should().Throw<DomainException>().WithMessage("*título*");
    }

    [Fact]
    public void Should_Normalize_Url_And_Compute_Fingerprint_On_Create()
    {
        var job = Job.Create(
            JobSource.LinkedIn, "  Desenvolvedor .NET ", "https://www.linkedin.com/jobs/view/1/?trackingId=x", Now, company: "Agro Tech", location: "Brasil");

        job.Title.Should().Be("Desenvolvedor .NET");
        job.Url.Should().Be("https://www.linkedin.com/jobs/view/1/");
        job.Fingerprint.Should().Be(JobFingerprint.Compute("Desenvolvedor .NET", "Agro Tech", "Brasil"));
        job.SourceId.Should().Be(JobSource.LinkedIn.Id);
        job.FoundAt.Should().Be(Now);
    }

    [Fact]
    public void Should_Start_Pending_Analysis()
    {
        var job = Job.Create(JobSource.LinkedIn, "Desenvolvedor .NET", "https://www.linkedin.com/jobs/view/1/", Now);

        job.AnalysisStatus.Should().Be(AnalysisStatus.Pending);
        job.AnalysisAttempts.Should().Be(0);
        job.IsDueForAnalysis(Now).Should().BeTrue();
    }
}
