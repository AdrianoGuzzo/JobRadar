using JobRadar.Domain.Analysis;
using JobRadar.Domain.Common;
using JobRadar.Domain.Jobs;
using JobRadar.Domain.Profiles;

namespace JobRadar.Application.Abstractions;

/// <summary>
/// Analisa a compatibilidade de uma vaga com o perfil. A descrição da vaga é
/// conteúdo não confiável: nunca altera o comportamento do analisador.
/// </summary>
public interface IJobAnalyzer
{
    /// <exception cref="JobAnalysisException">A análise falhou ou a resposta é inválida.</exception>
    Task<JobAnalysis> AnalyzeAsync(CandidateProfile profile, Job job, CancellationToken cancellationToken);
}

public sealed class JobAnalysisException(FailureKind kind, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public FailureKind Kind { get; } = kind;
}
