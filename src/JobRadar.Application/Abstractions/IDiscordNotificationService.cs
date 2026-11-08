using JobRadar.Domain.Analysis;
using JobRadar.Domain.Common;
using JobRadar.Domain.Jobs;

namespace JobRadar.Application.Abstractions;

/// <summary>O conteúdo de uma notificação de vaga, independente de como é formatado.</summary>
public sealed record JobNotificationMessage(
    string Title,
    string? Company,
    int Score,
    WorkMode WorkMode,
    string? Location,
    string SourceName,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Gaps,
    IReadOnlyList<string> MissingInformation,
    string Summary,
    string Url)
{
    public static JobNotificationMessage From(Job job, JobMatch match, string sourceName) => new(
        job.Title,
        job.Company,
        match.Score,
        job.WorkMode,
        job.Location,
        sourceName,
        match.Strengths,
        match.Gaps,
        match.MissingInformation,
        match.Summary,
        job.Url);
}

public interface IDiscordNotificationService
{
    /// <summary>Publica a notificação e devolve o id da mensagem.</summary>
    /// <exception cref="NotificationDeliveryException">O envio falhou.</exception>
    Task<string> SendAsync(JobNotificationMessage message, CancellationToken cancellationToken);
}

public sealed class NotificationDeliveryException(FailureKind kind, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public FailureKind Kind { get; } = kind;
}
