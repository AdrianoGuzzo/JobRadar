using JobRadar.Application.Jobs;
using JobRadar.Domain.Jobs;
using JobRadar.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobRadar.Infrastructure.Persistence;

internal sealed class JobReviewConfiguration : IEntityTypeConfiguration<JobReview>
{
    public void Configure(EntityTypeBuilder<JobReview> builder)
    {
        builder.HasKey(review => review.JobId);
        builder.Property(review => review.JobId).ValueGeneratedNever();
        builder.Property(review => review.DecidedBy).HasMaxLength(20);
        builder.HasOne<Job>().WithOne().HasForeignKey<JobReview>(review => review.JobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(review => new { review.Status, review.DecidedAt, review.JobId }).IsDescending(false, true, false);
    }
}

public sealed class JobReviewRepository(IDbContextFactory<JobRadarDbContext> contexts) : IJobReviewRepository
{
    public async Task<JobReviewStatus?> DecideAsync(Guid notificationId, string messageId, JobReviewStatus status,
        string userId, TimeProvider time, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var jobIds = await context.Database.SqlQuery<Guid>($"""
            SELECT j."Id" AS "Value"
            FROM "Jobs" j
            JOIN "Notifications" n ON n."JobId" = j."Id"
            WHERE n."Id" = {notificationId} AND n."ExternalMessageId" = {messageId}
            FOR UPDATE OF j
            """).ToListAsync(cancellationToken);
        if (jobIds.Count == 0 || !await context.Notifications.AnyAsync(n =>
                n.Id == notificationId && n.ExternalMessageId == messageId && n.Status == NotificationStatus.Sent, cancellationToken))
        {
            return null;
        }

        var jobId = jobIds[0];
        var review = await context.JobReviews.FindAsync([jobId], cancellationToken);
        if (review is null)
        {
            review = new JobReview(jobId);
            context.JobReviews.Add(review);
        }

        review.Decide(status, userId, time.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return review.Status;
    }

    public async Task<IReadOnlyList<ApprovedJob>> ListApprovedAsync(int offset, int limit, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        return await (
            from review in context.JobReviews
            join job in context.Jobs on review.JobId equals job.Id
            where review.Status == JobReviewStatus.Approved
            orderby review.DecidedAt descending, review.JobId
            select new ApprovedJob(job.Title, job.Company, job.Url))
            .Skip(offset).Take(limit).ToListAsync(cancellationToken);
    }
}
