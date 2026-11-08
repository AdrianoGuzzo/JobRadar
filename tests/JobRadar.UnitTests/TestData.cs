using JobRadar.Domain.Analysis;
using JobRadar.Domain.Jobs;
using JobRadar.Domain.Profiles;
using JobRadar.UnitTests.Domain;

namespace JobRadar.UnitTests;

/// <summary>Objetos de domínio válidos para os testes, com o mínimo de ruído.</summary>
internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    public static Job Job(string title = "Desenvolvedor .NET Sênior", string? externalId = "4012345678", string? description = "APIs REST em ASP.NET Core") =>
        global::JobRadar.Domain.Jobs.Job.Create(
            JobSource.LinkedIn,
            title,
            $"https://www.linkedin.com/jobs/view/{externalId ?? "0"}/",
            Now,
            externalId: externalId,
            company: "Agro Tech",
            location: "Brasil",
            workMode: WorkMode.Remote,
            description: description);

    public static CandidateProfile Profile() => CandidateProfile.Create(CandidateProfileTests.SampleContent());

    public static JobAnalysis Analysis(int score = 90, IReadOnlyList<string>? missingInformation = null) => new(
        Score: new CompatibilityScore(score),
        JobTitle: "Senior .NET Developer",
        Seniority: "Senior",
        Strengths: ["Experiência com C# e .NET"],
        Gaps: ["Inglês avançado exigido"],
        TechnologyMatches: [new TechnologyMatch(".NET", TechnologyMatchLevel.Excellent)],
        Recommendation: Recommendation.HighMatch,
        Summary: "Alta compatibilidade com o perfil profissional.",
        MissingInformation: missingInformation ?? []);

    public static JobMatch Match(int score = 90) => JobMatch.Create(Job(), Profile(), Analysis(score), Now);
}
