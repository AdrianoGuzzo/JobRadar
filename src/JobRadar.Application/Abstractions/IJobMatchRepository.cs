using JobRadar.Domain.Analysis;
using JobRadar.Domain.Profiles;

namespace JobRadar.Application.Abstractions;

public interface IJobMatchRepository
{
    Task<bool> ExistsAsync(Guid jobId, string profileVersion, CancellationToken cancellationToken);

    /// <summary>Grava o match se não há outro para a mesma vaga e versão do perfil.</summary>
    Task<bool> AddIfNewAsync(JobMatch match, CancellationToken cancellationToken);
}

public interface ICandidateProfileRepository
{
    /// <summary>O perfil usado nas análises (o MVP tem um único usuário).</summary>
    Task<CandidateProfile?> GetActiveAsync(CancellationToken cancellationToken);

    Task SaveAsync(CandidateProfile profile, CancellationToken cancellationToken);
}
