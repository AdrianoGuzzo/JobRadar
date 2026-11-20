using JobRadar.Domain.Analysis;
using JobRadar.Domain.Jobs;
using JobRadar.Domain.Notifications;
using JobRadar.Domain.Profiles;
using Microsoft.EntityFrameworkCore;

namespace JobRadar.Infrastructure.Persistence;

/// <summary>
/// O banco do JobRadar (PostgreSQL). O esquema muda só por migrations; as
/// garantias de unicidade (ADR-001, ADR-002) são índices únicos daqui.
/// </summary>
public sealed class JobRadarDbContext(DbContextOptions<JobRadarDbContext> options) : DbContext(options)
{
    public DbSet<JobSource> JobSources => Set<JobSource>();

    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<JobReview> JobReviews => Set<JobReview>();

    public DbSet<CandidateProfile> CandidateProfiles => Set<CandidateProfile>();

    public DbSet<JobMatch> JobMatches => Set<JobMatch>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<JobSearchConfiguration> JobSearchConfigurations => Set<JobSearchConfiguration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(JobRadarDbContext).Assembly);
}

public static class JobRadarDbContextOptions
{
    public static DbContextOptionsBuilder UseJobRadarPostgres(this DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseNpgsql(connectionString);

    public static DbContextOptionsBuilder<TContext> UseJobRadarPostgres<TContext>(this DbContextOptionsBuilder<TContext> builder, string connectionString)
        where TContext : DbContext
    {
        ((DbContextOptionsBuilder)builder).UseJobRadarPostgres(connectionString);
        return builder;
    }
}
