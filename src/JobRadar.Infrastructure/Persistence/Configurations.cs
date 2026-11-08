using System.Text.Json;
using System.Text.Json.Serialization;
using JobRadar.Domain.Analysis;
using JobRadar.Domain.Jobs;
using JobRadar.Domain.Notifications;
using JobRadar.Domain.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobRadar.Infrastructure.Persistence;

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static PropertyBuilder<T> HasJsonbConversion<T>(this PropertyBuilder<T> property, ValueComparer<T>? comparer = null)
        where T : class
    {
        property
            .HasColumnType("jsonb")
            .HasConversion(
                value => JsonSerializer.Serialize(value, Options),
                json => JsonSerializer.Deserialize<T>(json, Options)!);

        if (comparer is not null)
        {
            property.Metadata.SetValueComparer(comparer);
        }

        return property;
    }
}

internal sealed class JobSourceConfiguration : IEntityTypeConfiguration<JobSource>
{
    public void Configure(EntityTypeBuilder<JobSource> builder)
    {
        builder.HasKey(source => source.Id);
        builder.Property(source => source.Id).ValueGeneratedNever();
        builder.Property(source => source.Name).HasMaxLength(100);
        builder.HasData(JobSource.All.Select(source => new { source.Id, source.Name }));
    }
}

internal sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).ValueGeneratedNever();
        builder.HasOne<JobSource>().WithMany().HasForeignKey(job => job.SourceId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(job => job.ExternalId).HasMaxLength(200);
        builder.Property(job => job.Title).HasMaxLength(500);
        builder.Property(job => job.Company).HasMaxLength(300);
        builder.Property(job => job.Location).HasMaxLength(300);
        builder.Property(job => job.Url).HasMaxLength(2048);
        builder.Property(job => job.Fingerprint).HasMaxLength(64);
        builder.Property(job => job.WorkMode).HasConversion<string>().HasMaxLength(20);
        builder.Property(job => job.AnalysisStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(job => job.LastAnalysisError).HasMaxLength(1000);

        // ADR-001: a mesma vaga nunca entra duas vezes.
        builder.HasIndex(job => new { job.SourceId, job.ExternalId }).IsUnique().HasFilter("\"ExternalId\" IS NOT NULL");
        builder.HasIndex(job => job.Fingerprint).IsUnique();
        builder.HasIndex(job => new { job.AnalysisStatus, job.FoundAt });
    }
}

internal sealed class CandidateProfileConfiguration : IEntityTypeConfiguration<CandidateProfile>
{
    public void Configure(EntityTypeBuilder<CandidateProfile> builder)
    {
        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.Id).ValueGeneratedNever();
        builder.Property(profile => profile.Version).HasMaxLength(64);
        builder.Property(profile => profile.Content).HasJsonbConversion();
    }
}

internal sealed class JobMatchConfiguration : IEntityTypeConfiguration<JobMatch>
{
    public void Configure(EntityTypeBuilder<JobMatch> builder)
    {
        builder.HasKey(match => match.Id);
        builder.Property(match => match.Id).ValueGeneratedNever();
        builder.HasOne<Job>().WithMany().HasForeignKey(match => match.JobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CandidateProfile>().WithMany().HasForeignKey(match => match.CandidateProfileId).OnDelete(DeleteBehavior.Cascade);

        builder.Property(match => match.ProfileVersion).HasMaxLength(64);
        builder.Property(match => match.JobTitle).HasMaxLength(500);
        builder.Property(match => match.Seniority).HasMaxLength(100);
        builder.Property(match => match.Recommendation).HasConversion<string>().HasMaxLength(20);
        builder.Property(match => match.TechnologyMatches).HasJsonbConversion(new ValueComparer<List<TechnologyMatch>>(
            (left, right) => left!.SequenceEqual(right!),
            list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
            list => list.ToList()));

        // ADR-002: uma análise por vaga e versão do perfil.
        builder.HasIndex(match => new { match.JobId, match.ProfileVersion }).IsUnique();
        builder.HasIndex(match => new { match.CandidateProfileId, match.Score });
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Id).ValueGeneratedNever();
        builder.HasOne<Job>().WithMany().HasForeignKey(notification => notification.JobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<JobMatch>().WithMany().HasForeignKey(notification => notification.JobMatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CandidateProfile>().WithMany().HasForeignKey(notification => notification.CandidateProfileId).OnDelete(DeleteBehavior.Cascade);

        builder.Property(notification => notification.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(notification => notification.LastError).HasMaxLength(1000);
        builder.Property(notification => notification.ExternalMessageId).HasMaxLength(100);

        // ADR-002 / regra 1: nunca duas notificações da mesma vaga para o mesmo usuário.
        builder.HasIndex(notification => new { notification.JobId, notification.CandidateProfileId }).IsUnique();
        builder.HasIndex(notification => new { notification.Status, notification.CreatedAt });
    }
}

internal sealed class JobSearchConfigurationConfiguration : IEntityTypeConfiguration<JobSearchConfiguration>
{
    public void Configure(EntityTypeBuilder<JobSearchConfiguration> builder)
    {
        builder.HasKey(configuration => configuration.SourceId);
        builder.Property(configuration => configuration.SourceId).ValueGeneratedNever();
        builder.HasOne<JobSource>().WithMany().HasForeignKey(configuration => configuration.SourceId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(configuration => configuration.Checkpoint).HasMaxLength(200);
    }
}
