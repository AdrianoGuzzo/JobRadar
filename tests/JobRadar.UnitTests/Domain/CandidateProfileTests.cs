using JobRadar.Domain.Common;
using JobRadar.Domain.Jobs;
using JobRadar.Domain.Profiles;

namespace JobRadar.UnitTests.Domain;

public sealed class CandidateProfileTests
{
    [Fact]
    public void Should_Keep_Version_When_Content_Is_Equal()
    {
        var first = CandidateProfile.Create(SampleContent());
        var second = CandidateProfile.Create(SampleContent());

        second.Version.Should().Be(first.Version).And.NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Should_Change_Version_When_Content_Changes()
    {
        var profile = CandidateProfile.Create(SampleContent());
        var previous = profile.Version;

        var changed = profile.Update(SampleContent() with { DesiredTechnologies = ["Kafka", "Docker", "Kubernetes"] });

        changed.Should().BeTrue();
        profile.Version.Should().NotBe(previous);
        profile.Content.DesiredTechnologies.Should().Contain("Kubernetes");
    }

    [Fact]
    public void Should_Not_Change_Version_When_Update_Has_Same_Content()
    {
        var profile = CandidateProfile.Create(SampleContent());
        var previous = profile.Version;

        var changed = profile.Update(SampleContent());

        changed.Should().BeFalse();
        profile.Version.Should().Be(previous);
    }

    [Fact]
    public void Should_Reject_Profile_Without_Name()
    {
        var act = () => CandidateProfile.Create(SampleContent() with { Name = " " });

        act.Should().Throw<DomainException>().WithMessage("*nome*");
    }

    internal static ProfileContent SampleContent() => new(
        Name: "Candidato de demonstração",
        Headline: "Desenvolvedor Full Stack / Backend",
        YearsOfExperience: 10,
        DesiredRoles: ["Desenvolvedor Backend .NET", "Desenvolvedor Full Stack"],
        Seniority: "Sênior",
        RequiredTechnologies: ["C#", ".NET", "ASP.NET Core"],
        DesiredTechnologies: ["Kafka", "Docker"],
        WorkModes: [WorkMode.Remote, WorkMode.Hybrid],
        Locations: ["Brasil"],
        SalaryExpectation: null,
        Languages: ["Português (nativo)", "Inglês (intermediário)"],
        Experience: ["Sistemas corporativos", "Agronegócio"],
        AdditionalPreferences: []);
}
