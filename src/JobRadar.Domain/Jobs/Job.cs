using JobRadar.Domain.Common;

namespace JobRadar.Domain.Jobs;

/// <summary>
/// Uma oportunidade coletada. Só título e link são obrigatórios: informação
/// ausente nunca descarta a vaga, fica explícita em <see cref="MissingInformation"/>.
/// </summary>
public sealed class Job
{
    private Job()
    {
        Title = string.Empty;
        Url = string.Empty;
        Fingerprint = string.Empty;
    }

    public Guid Id { get; private set; }

    public int SourceId { get; private set; }

    public string? ExternalId { get; private set; }

    public string Title { get; private set; }

    public string? Company { get; private set; }

    public string? Location { get; private set; }

    public WorkMode WorkMode { get; private set; }

    public string Url { get; private set; }

    public string? Description { get; private set; }

    public string Fingerprint { get; private set; }

    public DateTimeOffset FoundAt { get; private set; }

    public AnalysisStatus AnalysisStatus { get; private set; }

    public int AnalysisAttempts { get; private set; }

    public static Job Create(
        JobSource source,
        string title,
        string url,
        DateTimeOffset foundAt,
        string? externalId = null,
        string? company = null,
        string? location = null,
        WorkMode workMode = WorkMode.NotInformed,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("A vaga precisa de um título.");
        }

        var cleanTitle = title.Trim();
        var cleanCompany = Clean(company);
        var cleanLocation = Clean(location);

        return new Job
        {
            Id = Guid.CreateVersion7(foundAt),
            SourceId = source.Id,
            ExternalId = Clean(externalId),
            Title = cleanTitle,
            Company = cleanCompany,
            Location = cleanLocation,
            WorkMode = workMode,
            Url = JobUrl.Normalize(url),
            Description = Clean(description),
            Fingerprint = JobFingerprint.Compute(cleanTitle, cleanCompany, cleanLocation),
            FoundAt = foundAt,
            AnalysisStatus = AnalysisStatus.Pending,
        };
    }

    /// <summary>Os campos que a fonte não informou, com o nome que o usuário lê.</summary>
    public IReadOnlyList<string> MissingInformation()
    {
        var missing = new List<string>();

        if (Company is null)
        {
            missing.Add("empresa");
        }

        if (Location is null)
        {
            missing.Add("localização");
        }

        if (WorkMode == WorkMode.NotInformed)
        {
            missing.Add("modalidade");
        }

        if (Description is null)
        {
            missing.Add("descrição");
        }

        return missing;
    }

    public bool IsDueForAnalysis(DateTimeOffset now) => AnalysisStatus == AnalysisStatus.Pending;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
