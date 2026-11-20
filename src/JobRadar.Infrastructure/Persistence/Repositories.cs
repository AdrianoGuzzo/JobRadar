using JobRadar.Application.Abstractions;
using JobRadar.Domain.Analysis;
using JobRadar.Domain.Jobs;
using JobRadar.Domain.Notifications;
using JobRadar.Domain.Profiles;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobRadar.Infrastructure.Persistence;

/// <summary>
/// Cada operação abre o seu contexto e grava na hora: os casos de uso rodam
/// em paralelo, e o estado precisa estar no banco antes da etapa seguinte.
/// </summary>
internal static class UniqueViolation
{
    public static bool Is(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

public sealed class JobRepository(IDbContextFactory<JobRadarDbContext> contexts) : IJobRepository
{
    public async Task<bool> AddIfNewAsync(Job job, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        context.Jobs.Add(job);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (UniqueViolation.Is(exception))
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<Job>> ClaimForAnalysisAsync(DateTimeOffset now, TimeSpan lease, int maxCount, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // SKIP LOCKED: execuções concorrentes pegam vagas diferentes, sem esperar umas pelas outras.
        var jobs = await context.Jobs.FromSql($"""
            SELECT * FROM "Jobs"
            WHERE "AnalysisStatus" = 'Pending'
               OR ("AnalysisStatus" = 'RetryScheduled' AND "NextAnalysisAttemptAt" <= {now})
               OR ("AnalysisStatus" = 'Analyzing' AND "AnalysisLeasedUntil" < {now})
            ORDER BY "FoundAt"
            LIMIT {maxCount}
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);

        foreach (var job in jobs)
        {
            job.StartAnalysis(now, lease);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return jobs;
    }

    public async Task UpdateAsync(Job job, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        context.Jobs.Update(job);
        await context.SaveChangesAsync(cancellationToken);
    }
}

public sealed class JobSearchConfigurationRepository(IDbContextFactory<JobRadarDbContext> contexts) : IJobSearchConfigurationRepository
{
    public async Task<JobSearchConfiguration> GetOrCreateAsync(JobSource source, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var existing = await context.JobSearchConfigurations.AsNoTracking().SingleOrDefaultAsync(c => c.SourceId == source.Id, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var created = JobSearchConfiguration.Create(source);
        context.JobSearchConfigurations.Add(created);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return created;
        }
        catch (DbUpdateException exception) when (UniqueViolation.Is(exception))
        {
            await using var retry = await contexts.CreateDbContextAsync(cancellationToken);
            return await retry.JobSearchConfigurations.AsNoTracking().SingleAsync(c => c.SourceId == source.Id, cancellationToken);
        }
    }

    public async Task SaveAsync(JobSearchConfiguration configuration, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        context.JobSearchConfigurations.Update(configuration);
        await context.SaveChangesAsync(cancellationToken);
    }
}

public sealed class CandidateProfileRepository(IDbContextFactory<JobRadarDbContext> contexts) : ICandidateProfileRepository
{
    public async Task<CandidateProfile?> GetActiveAsync(CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        return await context.CandidateProfiles.AsNoTracking().OrderBy(profile => profile.Id).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task SaveAsync(CandidateProfile profile, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var exists = await context.CandidateProfiles.AnyAsync(p => p.Id == profile.Id, cancellationToken);

        if (exists)
        {
            context.CandidateProfiles.Update(profile);
        }
        else
        {
            context.CandidateProfiles.Add(profile);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}

public sealed class JobMatchRepository(IDbContextFactory<JobRadarDbContext> contexts) : IJobMatchRepository
{
    public async Task<bool> ExistsAsync(Guid jobId, string profileVersion, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        return await context.JobMatches.AnyAsync(match => match.JobId == jobId && match.ProfileVersion == profileVersion, cancellationToken);
    }

    public async Task<bool> AddIfNewAsync(JobMatch match, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        context.JobMatches.Add(match);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (UniqueViolation.Is(exception))
        {
            return false;
        }
    }
}

public sealed class NotificationRepository(IDbContextFactory<JobRadarDbContext> contexts) : INotificationRepository
{
    public async Task<IReadOnlyList<JobMatch>> GetMatchesAwaitingNotificationAsync(Guid candidateProfileId, int minimumScore, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);

        return await context.JobMatches
            .AsNoTracking()
            .Where(match => match.CandidateProfileId == candidateProfileId && match.Score >= minimumScore)
            .Where(match => !context.Notifications.Any(n => n.JobId == match.JobId && n.CandidateProfileId == candidateProfileId))
            .OrderBy(match => match.AnalyzedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> AddIfNewAsync(Notification notification, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        context.Notifications.Add(notification);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (UniqueViolation.Is(exception))
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<NotificationWork>> ClaimDueAsync(DateTimeOffset now, TimeSpan lease, int maxCount, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var notifications = await context.Notifications.FromSql($"""
            SELECT * FROM "Notifications"
            WHERE "Status" = 'Pending'
               OR ("Status" = 'RetryScheduled' AND "NextAttemptAt" <= {now})
               OR ("Status" = 'Sending' AND "LeasedUntil" < {now})
            ORDER BY "CreatedAt"
            LIMIT {maxCount}
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            notification.StartSending(now, lease);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (notifications.Count == 0)
        {
            return [];
        }

        var jobIds = notifications.Select(n => n.JobId).ToList();
        var matchIds = notifications.Select(n => n.JobMatchId).ToList();
        var jobs = await context.Jobs.AsNoTracking().Where(job => jobIds.Contains(job.Id)).ToDictionaryAsync(job => job.Id, cancellationToken);
        var matches = await context.JobMatches.AsNoTracking().Where(match => matchIds.Contains(match.Id)).ToDictionaryAsync(match => match.Id, cancellationToken);
        var sources = await context.JobSources.AsNoTracking().ToDictionaryAsync(source => source.Id, source => source.Name, cancellationToken);

        var reviews = await context.JobReviews.AsNoTracking().Where(review => jobIds.Contains(review.JobId))
            .ToDictionaryAsync(review => review.JobId, review => review.Status, cancellationToken);
        return [.. notifications.Select(n => new NotificationWork(n, jobs[n.JobId], matches[n.JobMatchId],
            sources[jobs[n.JobId].SourceId], reviews.GetValueOrDefault(n.JobId)))];
    }

    public async Task UpdateAsync(Notification notification, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        context.Notifications.Update(notification);
        await context.SaveChangesAsync(cancellationToken);
    }
}
