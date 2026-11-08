using JobRadar.Domain.Common;

namespace JobRadar.Domain.Analysis;

public enum Recommendation
{
    HighMatch,
    MediumMatch,
    LowMatch,
}

public enum TechnologyMatchLevel
{
    Excellent,
    Good,
    Partial,
    Missing,
    NotInformed,
}

public sealed record TechnologyMatch(string Technology, TechnologyMatchLevel Match);

/// <summary>
/// O resultado estruturado da análise de compatibilidade (contrato do
/// <c>IJobAnalyzer</c>). Informação ausente fica em <see cref="MissingInformation"/>,
/// nunca inventada.
/// </summary>
public sealed record JobAnalysis
{
    private readonly string _summary = string.Empty;

    public JobAnalysis(
        CompatibilityScore Score,
        string? JobTitle,
        string? Seniority,
        IReadOnlyList<string> Strengths,
        IReadOnlyList<string> Gaps,
        IReadOnlyList<TechnologyMatch> TechnologyMatches,
        Recommendation Recommendation,
        string Summary,
        IReadOnlyList<string> MissingInformation)
    {
        this.Score = Score;
        this.JobTitle = JobTitle;
        this.Seniority = Seniority;
        this.Strengths = Strengths;
        this.Gaps = Gaps;
        this.TechnologyMatches = TechnologyMatches;
        this.Recommendation = Recommendation;
        this.Summary = Summary;
        this.MissingInformation = MissingInformation;
    }

    public CompatibilityScore Score { get; init; }

    public string? JobTitle { get; init; }

    public string? Seniority { get; init; }

    public IReadOnlyList<string> Strengths { get; init; }

    public IReadOnlyList<string> Gaps { get; init; }

    public IReadOnlyList<TechnologyMatch> TechnologyMatches { get; init; }

    public Recommendation Recommendation { get; init; }

    public string Summary
    {
        get => _summary;
        init => _summary = string.IsNullOrWhiteSpace(value)
            ? throw new DomainException("A análise precisa de um resumo.")
            : value.Trim();
    }

    public IReadOnlyList<string> MissingInformation { get; init; }
}
