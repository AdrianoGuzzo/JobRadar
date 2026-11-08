using JobRadar.Domain.Jobs;

namespace JobRadar.Application.Abstractions;

/// <summary>Uma vaga como a fonte a entregou, já extraída e normalizada nos campos.</summary>
public sealed record JobPosting(
    string Title,
    string Url,
    string? ExternalId,
    string? Company,
    string? Location,
    WorkMode WorkMode,
    string? Description);

/// <summary>
/// O que a fonte encontrou desde o último checkpoint, mais o novo checkpoint.
/// Ele só é gravado depois que as vagas foram salvas: nada se perde se o
/// processo cair no meio.
/// </summary>
public sealed record JobSourceBatch(IReadOnlyList<JobPosting> Postings, string? Checkpoint);

/// <summary>
/// Uma fonte autorizada de vagas (alertas por e-mail, APIs públicas...).
/// Nunca scraping de plataformas que o proíbem.
/// </summary>
public interface IJobSource
{
    JobSource Source { get; }

    Task<JobSourceBatch> FetchAsync(string? checkpoint, CancellationToken cancellationToken);
}
