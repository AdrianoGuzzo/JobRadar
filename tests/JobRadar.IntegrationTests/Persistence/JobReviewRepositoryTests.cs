using JobRadar.Domain.Analysis;
using JobRadar.Domain.Jobs;
using JobRadar.Domain.Notifications;
using JobRadar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace JobRadar.IntegrationTests.Persistence;

[Trait("Category", "Integration")]
public sealed class JobReviewRepositoryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Should_Upgrade_Existing_Data_And_Persist_One_Decision_Per_Job()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = new TestDatabase(await postgres.CreateEmptyDatabaseAsync());
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync("20261108185245_InitialCreate", ct);
        var job = IntegrationData.Job();
        var profile = IntegrationData.Profile();
        var match = JobMatch.Create(job, profile, IntegrationData.Analysis(90), IntegrationData.Now);
        var notification = Notification.Create(match, IntegrationData.Now);
        notification.MarkSent("999", IntegrationData.Now);
        context.AddRange(job, profile, match, notification);
        await context.SaveChangesAsync(ct);
        await context.Database.MigrateAsync(ct);

        var repository = new JobReviewRepository(database);
        (await repository.ListApprovedAsync(0, 5, ct)).Should().BeEmpty();
        (await repository.DecideAsync(notification.Id, "888", JobReviewStatus.Approved, "123", TimeProvider.System, ct)).Should().BeNull();
        var approvals = await Task.WhenAll(
            repository.DecideAsync(notification.Id, "999", JobReviewStatus.Approved, "123", TimeProvider.System, ct),
            repository.DecideAsync(notification.Id, "999", JobReviewStatus.Approved, "456", TimeProvider.System, ct));
        approvals.Should().AllBeEquivalentTo(JobReviewStatus.Approved);
        var reloaded = new JobReviewRepository(database);
        (await reloaded.ListApprovedAsync(0, 5, ct)).Should().ContainSingle().Which.Title.Should().Be(job.Title);
        (await reloaded.ListApprovedAsync(5, 5, ct)).Should().BeEmpty();
        (await reloaded.DecideAsync(notification.Id, "999", JobReviewStatus.Discarded, "789", TimeProvider.System, ct))
            .Should().Be(JobReviewStatus.Discarded);
        (await reloaded.ListApprovedAsync(0, 5, ct)).Should().BeEmpty();
        var decision = await context.JobReviews.AsNoTracking().SingleAsync(ct);
        decision.DecidedBy.Should().Be("789");
        decision.DecidedAt.Offset.Should().Be(TimeSpan.Zero);
        await context.Jobs.Where(item => item.Id == job.Id).ExecuteDeleteAsync(ct);
        (await context.JobReviews.CountAsync(ct)).Should().Be(0);
        (await reloaded.DecideAsync(notification.Id, "999", JobReviewStatus.Approved, "123", TimeProvider.System, ct)).Should().BeNull();
    }
}
