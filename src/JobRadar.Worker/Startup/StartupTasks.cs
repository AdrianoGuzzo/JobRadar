using System.ComponentModel.DataAnnotations;
using JobRadar.Application.Profiles;
using JobRadar.Infrastructure.Claude;
using JobRadar.Infrastructure.Persistence;
using JobRadar.Infrastructure.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobRadar.Worker.Startup;

/// <summary>Onde está o perfil do candidato (seção <c>Profile</c>).</summary>
public sealed class ProfileOptions
{
    public const string Section = "Profile";

    [Required]
    public string Path { get; set; } = "config/profile.json";
}

/// <summary>
/// O que roda antes de o Worker começar a agendar: aplica as migrations, importa
/// o perfil e confere o Claude Code. Qualquer falha aqui para o Worker com uma
/// mensagem clara, em vez de deixar vagas falharem uma a uma.
/// </summary>
public sealed partial class StartupTasks(
    IDbContextFactory<JobRadarDbContext> contexts,
    SyncCandidateProfile syncProfile,
    ClaudeCliVersionCheck claudeCheck,
    IOptions<ProfileOptions> profileOptions,
    ILogger<StartupTasks> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using (var context = await contexts.CreateDbContextAsync(cancellationToken))
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        LogMigrated(logger);

        var content = ProfileFileReader.Read(profileOptions.Value.Path);
        await syncProfile.ExecuteAsync(content, cancellationToken);

        var claude = await claudeCheck.CheckAsync(cancellationToken);

        if (!claude.IsUsable)
        {
            throw new InvalidOperationException(claude.Problem);
        }

        LogClaudeReady(logger, claude.Version!);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Banco de dados atualizado.")]
    private static partial void LogMigrated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Claude Code {Version} disponível.")]
    private static partial void LogClaudeReady(ILogger logger, Version version);
}
