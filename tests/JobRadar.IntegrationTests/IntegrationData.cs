using JobRadar.Domain.Analysis;
using JobRadar.Domain.Jobs;
using JobRadar.Domain.Profiles;

namespace JobRadar.IntegrationTests;

internal static class IntegrationData
{
    /// <summary>Precisão de microssegundos, como o timestamptz do PostgreSQL.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    public static Job Job(string? externalId = null, string title = "Desenvolvedor .NET Sênior", string? description = "APIs REST em ASP.NET Core")
    {
        externalId ??= Random.Shared.NextInt64(1_000_000_000, 9_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

        return global::JobRadar.Domain.Jobs.Job.Create(
            JobSource.LinkedIn,
            title,
            $"https://www.linkedin.com/jobs/view/{externalId}/",
            Now,
            externalId: externalId,
            company: "Agro Tech",
            location: "Brasil",
            workMode: WorkMode.Remote,
            description: description);
    }

    public static CandidateProfile Profile() => CandidateProfile.Create(new ProfileContent(
        Name: "Candidato de demonstração",
        Headline: "Desenvolvedor Full Stack / Backend",
        YearsOfExperience: 10,
        DesiredRoles: ["Desenvolvedor Backend .NET"],
        Seniority: "Sênior",
        RequiredTechnologies: ["C#", ".NET"],
        DesiredTechnologies: ["Kafka"],
        WorkModes: [WorkMode.Remote],
        Locations: ["Brasil"],
        SalaryExpectation: "A combinar",
        Languages: ["Português"],
        Experience: ["Agronegócio"],
        AdditionalPreferences: ["Sem viagens"]));

    public static JobAnalysis Analysis(int score = 90) => new(
        Score: new CompatibilityScore(score),
        JobTitle: "Senior .NET Developer",
        Seniority: "Senior",
        Strengths: ["C# / .NET", "APIs REST"],
        Gaps: ["Inglês avançado"],
        TechnologyMatches: [new TechnologyMatch(".NET", TechnologyMatchLevel.Excellent), new TechnologyMatch("Kafka", TechnologyMatchLevel.Partial)],
        Recommendation: Recommendation.HighMatch,
        Summary: "Alta compatibilidade.",
        MissingInformation: ["faixa salarial"]);
}
