using JobRadar.Domain.Analysis;
using JobRadar.Domain.Common;
using JobRadar.Domain.Jobs;
using JobRadar.Domain.Notifications;
using JobRadar.Domain.Profiles;
using Microsoft.EntityFrameworkCore;

namespace JobRadar.IntegrationTests.Persistence;

/// <summary>Cada entidade volta do banco exatamente como foi gravada.</summary>
[Trait("Category", "Integration")]
public sealed class EntityMappingTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_Round_Trip_Job_With_Analysis_State()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var job = IntegrationData.Job();
        job.RecordAnalysisFailure(FailureKind.Transient, "timeout", RetryPolicy.Default, IntegrationData.Now);

        await Save(database, job);

        await using var context = database.CreateContext();
        var loaded = await context.Jobs.SingleAsync(Ct);
        loaded.Should().BeEquivalentTo(job);
    }

    [Fact]
    public async Task Should_Round_Trip_Candidate_Profile_Content()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var profile = IntegrationData.Profile();

        await Save(database, profile);

        await using var context = database.CreateContext();
        var loaded = await context.CandidateProfiles.SingleAsync(Ct);
        loaded.Version.Should().Be(profile.Version);
        loaded.Content.Should().BeEquivalentTo(profile.Content);
    }

    [Fact]
    public async Task Should_Round_Trip_Job_Match_With_Lists()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var job = IntegrationData.Job(description: null);
        var profile = IntegrationData.Profile();
        var match = JobMatch.Create(job, profile, IntegrationData.Analysis(), IntegrationData.Now);

        await Save(database, job, profile, match);

        await using var context = database.CreateContext();
        var loaded = await context.JobMatches.SingleAsync(Ct);
        loaded.Should().BeEquivalentTo(match);
        loaded.HasInsufficientInformation.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Round_Trip_Notification_And_Search_Configuration()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var job = IntegrationData.Job();
        var profile = IntegrationData.Profile();
        var match = JobMatch.Create(job, profile, IntegrationData.Analysis(), IntegrationData.Now);
        var notification = Notification.Create(match, IntegrationData.Now);
        notification.StartSending(IntegrationData.Now, TimeSpan.FromMinutes(2));
        notification.MarkSent("123", IntegrationData.Now);
        var configuration = JobSearchConfiguration.Create(JobSource.LinkedIn);
        configuration.RecordCheck("42:1001", IntegrationData.Now);

        await Save(database, job, profile, match, notification, configuration);

        await using var context = database.CreateContext();
        (await context.Notifications.SingleAsync(Ct)).Should().BeEquivalentTo(notification);
        (await context.JobSearchConfigurations.SingleAsync(Ct)).Should().BeEquivalentTo(configuration);
    }

    private static async Task Save(TestDatabase database, params object[] entities)
    {
        await using var context = database.CreateContext();
        context.AddRange(entities);
        await context.SaveChangesAsync(Ct);
    }
}
