using JobRadar.Domain.Analysis;
using JobRadar.Domain.Common;

namespace JobRadar.UnitTests.Domain;

public sealed class JobMatchTests
{
    [Fact]
    public void Should_Record_Analysis_For_Job_And_Profile_Version()
    {
        var job = TestData.Job();
        var profile = TestData.Profile();

        var match = JobMatch.Create(job, profile, TestData.Analysis(score: 92), TestData.Now);

        match.JobId.Should().Be(job.Id);
        match.CandidateProfileId.Should().Be(profile.Id);
        match.ProfileVersion.Should().Be(profile.Version);
        match.Score.Should().Be(92);
        match.Recommendation.Should().Be(Recommendation.HighMatch);
        match.Strengths.Should().Equal("Experiência com C# e .NET");
        match.Gaps.Should().Equal("Inglês avançado exigido");
        match.TechnologyMatches.Should().Equal(new TechnologyMatch(".NET", TechnologyMatchLevel.Excellent));
        match.AnalyzedAt.Should().Be(TestData.Now);
    }

    [Fact]
    public void Should_Flag_Insufficient_Information_When_Job_Lacks_Fields_Even_If_Analysis_Does_Not()
    {
        var job = TestData.Job(description: null);

        var match = JobMatch.Create(job, TestData.Profile(), TestData.Analysis(missingInformation: []), TestData.Now);

        match.HasInsufficientInformation.Should().BeTrue();
        match.MissingInformation.Should().Contain("descrição");
    }

    [Fact]
    public void Should_Merge_Missing_Information_Without_Duplicates()
    {
        var job = TestData.Job(description: null);

        var match = JobMatch.Create(job, TestData.Profile(), TestData.Analysis(missingInformation: ["descrição", "faixa salarial"]), TestData.Now);

        match.MissingInformation.Should().Equal("descrição", "faixa salarial");
    }

    [Fact]
    public void Should_Not_Flag_Insufficient_Information_When_Nothing_Is_Missing()
    {
        var match = JobMatch.Create(TestData.Job(), TestData.Profile(), TestData.Analysis(), TestData.Now);

        match.HasInsufficientInformation.Should().BeFalse();
    }

    [Fact]
    public void Should_Reject_Analysis_Without_Summary()
    {
        var act = () => TestData.Analysis() with { Summary = "" };

        act.Should().Throw<DomainException>().WithMessage("*resumo*");
    }
}
