using JobRadar.Application.Abstractions;
using JobRadar.Domain.Profiles;
using Microsoft.Extensions.Logging;

namespace JobRadar.Application.Profiles;

public enum ProfileSyncResult
{
    Created,
    Updated,
    Unchanged,
}

/// <summary>
/// Leva o perfil do arquivo de configuração para o banco. O candidato continua o
/// mesmo (mesmo Id); só a versão muda quando o conteúdo muda, e as próximas
/// análises usam a nova versão.
/// </summary>
public sealed partial class SyncCandidateProfile(ICandidateProfileRepository profiles, ILogger<SyncCandidateProfile> logger)
{
    public async Task<ProfileSyncResult> ExecuteAsync(ProfileContent content, CancellationToken cancellationToken)
    {
        var current = await profiles.GetActiveAsync(cancellationToken);

        if (current is null)
        {
            var created = CandidateProfile.Create(content);
            await profiles.SaveAsync(created, cancellationToken);
            LogSynced(logger, ProfileSyncResult.Created, created.Version);
            return ProfileSyncResult.Created;
        }

        if (!current.Update(content))
        {
            LogSynced(logger, ProfileSyncResult.Unchanged, current.Version);
            return ProfileSyncResult.Unchanged;
        }

        await profiles.SaveAsync(current, cancellationToken);
        LogSynced(logger, ProfileSyncResult.Updated, current.Version);
        return ProfileSyncResult.Updated;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Perfil do candidato: {Result} (versão {Version}).")]
    private static partial void LogSynced(ILogger logger, ProfileSyncResult result, string version);
}
