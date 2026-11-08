using JobRadar.Domain.Jobs;
using JobRadar.Domain.Profiles;

namespace JobRadar.Domain.Analysis;

/// <summary>
/// Uma análise de compatibilidade registrada. É histórico: nunca é alterada, e
/// há no máximo uma por vaga e versão do perfil (ADR-002).
/// </summary>
public sealed class JobMatch
{
    private JobMatch()
    {
        ProfileVersion = string.Empty;
        Summary = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid JobId { get; private set; }

    public Guid CandidateProfileId { get; private set; }

    public string ProfileVersion { get; private set; }

    public int Score { get; private set; }

    public string? JobTitle { get; private set; }

    public string? Seniority { get; private set; }

    public List<string> Strengths { get; private set; } = [];

    public List<string> Gaps { get; private set; } = [];

    public List<TechnologyMatch> TechnologyMatches { get; private set; } = [];

    public Recommendation Recommendation { get; private set; }

    public string Summary { get; private set; }

    /// <summary>
    /// O que faltou para uma análise completa: os campos que a fonte não trouxe
    /// mais o que a análise apontou.
    /// </summary>
    public List<string> MissingInformation { get; private set; } = [];

    public DateTimeOffset AnalyzedAt { get; private set; }

    public bool HasInsufficientInformation => MissingInformation.Count > 0;

    public static JobMatch Create(Job job, CandidateProfile profile, JobAnalysis analysis, DateTimeOffset analyzedAt) => new()
    {
        Id = Guid.CreateVersion7(analyzedAt),
        JobId = job.Id,
        CandidateProfileId = profile.Id,
        ProfileVersion = profile.Version,
        Score = analysis.Score.Value,
        JobTitle = analysis.JobTitle,
        Seniority = analysis.Seniority,
        Strengths = [.. analysis.Strengths],
        Gaps = [.. analysis.Gaps],
        TechnologyMatches = [.. analysis.TechnologyMatches],
        Recommendation = analysis.Recommendation,
        Summary = analysis.Summary,
        MissingInformation = [.. job.MissingInformation().Concat(analysis.MissingInformation).Distinct(StringComparer.OrdinalIgnoreCase)],
        AnalyzedAt = analyzedAt,
    };
}
