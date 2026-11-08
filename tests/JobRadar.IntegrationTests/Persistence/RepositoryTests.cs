using JobRadar.Domain.Analysis;
using JobRadar.Domain.Common;
using JobRadar.Domain.Jobs;
using JobRadar.Domain.Notifications;
using JobRadar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobRadar.IntegrationTests.Persistence;

[Trait("Category", "Integration")]
public sealed class JobRepositoryTests(PostgresFixture postgres)
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_Add_New_Job()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var repository = new JobRepository(database);

        var added = await repository.AddIfNewAsync(IntegrationData.Job(), Ct);

        added.Should().BeTrue();
        (await CountJobs(database)).Should().Be(1);
    }

    [Fact]
    public async Task Should_Not_Add_Duplicate_By_ExternalId()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var repository = new JobRepository(database);
        await repository.AddIfNewAsync(IntegrationData.Job(externalId: "100", title: "Dev .NET"), Ct);

        var added = await repository.AddIfNewAsync(IntegrationData.Job(externalId: "100", title: "Título editado"), Ct);

        added.Should().BeFalse();
        (await CountJobs(database)).Should().Be(1);
    }

    [Fact]
    public async Task Should_Not_Add_Duplicate_By_Fingerprint()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var repository = new JobRepository(database);
        await repository.AddIfNewAsync(IntegrationData.Job(externalId: "100", title: "Dev .NET Sênior"), Ct);

        var added = await repository.AddIfNewAsync(IntegrationData.Job(externalId: "200", title: "dev .net senior"), Ct);

        added.Should().BeFalse();
    }

    [Fact]
    public async Task Should_Claim_Due_Jobs_And_Lease_Them()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var repository = new JobRepository(database);
        var job = IntegrationData.Job();
        await repository.AddIfNewAsync(job, Ct);

        var first = await repository.ClaimForAnalysisAsync(IntegrationData.Now, Lease, 10, Ct);
        var second = await repository.ClaimForAnalysisAsync(IntegrationData.Now.AddMinutes(1), Lease, 10, Ct);

        first.Should().ContainSingle().Which.Id.Should().Be(job.Id);
        first[0].AnalysisStatus.Should().Be(AnalysisStatus.Analyzing);
        second.Should().BeEmpty("a vaga está reservada ao primeiro worker");
        (await LoadJob(database, job.Id)).AnalysisStatus.Should().Be(AnalysisStatus.Analyzing);
    }

    [Fact]
    public async Task Should_Reclaim_Job_When_Lease_Expired()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var repository = new JobRepository(database);
        await repository.AddIfNewAsync(IntegrationData.Job(), Ct);
        await repository.ClaimForAnalysisAsync(IntegrationData.Now, Lease, 10, Ct);

        var reclaimed = await repository.ClaimForAnalysisAsync(IntegrationData.Now.Add(Lease).AddSeconds(1), Lease, 10, Ct);

        reclaimed.Should().ContainSingle();
    }

    [Fact]
    public async Task Should_Claim_Retry_Scheduled_Job_Only_When_Due_And_Persist_Updates()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var repository = new JobRepository(database);
        await repository.AddIfNewAsync(IntegrationData.Job(), Ct);
        var job = (await repository.ClaimForAnalysisAsync(IntegrationData.Now, Lease, 10, Ct)).Single();
        job.RecordAnalysisFailure(FailureKind.Transient, "timeout", RetryPolicy.Default, IntegrationData.Now);
        await repository.UpdateAsync(job, Ct);

        var early = await repository.ClaimForAnalysisAsync(IntegrationData.Now.AddSeconds(30), Lease, 10, Ct);
        var due = await repository.ClaimForAnalysisAsync(IntegrationData.Now.AddMinutes(1), Lease, 10, Ct);

        early.Should().BeEmpty();
        due.Should().ContainSingle().Which.AnalysisAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Should_Respect_Claim_Limit_And_Order_By_Discovery()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var repository = new JobRepository(database);
        var older = IntegrationData.Job(title: "Primeira");
        var newer = Job.Create(JobSource.LinkedIn, "Segunda", "https://example.com/2", IntegrationData.Now.AddMinutes(5));
        await repository.AddIfNewAsync(newer, Ct);
        await repository.AddIfNewAsync(older, Ct);

        var claimed = await repository.ClaimForAnalysisAsync(IntegrationData.Now.AddMinutes(10), Lease, 1, Ct);

        claimed.Should().ContainSingle().Which.Title.Should().Be("Primeira");
    }

    private static async Task<int> CountJobs(TestDatabase database)
    {
        await using var context = database.CreateContext();
        return await context.Jobs.CountAsync(Ct);
    }

    private static async Task<Job> LoadJob(TestDatabase database, Guid id)
    {
        await using var context = database.CreateContext();
        return await context.Jobs.SingleAsync(job => job.Id == id, Ct);
    }
}

[Trait("Category", "Integration")]
public sealed class JobMatchAndProfileRepositoryTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_Save_And_Get_Active_Profile()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var repository = new CandidateProfileRepository(database);
        var profile = IntegrationData.Profile();

        (await repository.GetActiveAsync(Ct)).Should().BeNull();
        await repository.SaveAsync(profile, Ct);

        var active = await repository.GetActiveAsync(Ct);
        active!.Id.Should().Be(profile.Id);
        active.Content.Should().BeEquivalentTo(profile.Content);
    }

    [Fact]
    public async Task Should_Update_Existing_Profile()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var repository = new CandidateProfileRepository(database);
        var profile = IntegrationData.Profile();
        await repository.SaveAsync(profile, Ct);

        profile.Update(profile.Content with { DesiredTechnologies = ["Kubernetes"] });
        await repository.SaveAsync(profile, Ct);

        var active = await repository.GetActiveAsync(Ct);
        active!.Version.Should().Be(profile.Version);
        active.Content.DesiredTechnologies.Should().Equal("Kubernetes");
    }

    [Fact]
    public async Task Should_Keep_One_Match_Per_Job_And_Profile_Version()
    {
        var (database, job, profile) = await Seed();
        var repository = new JobMatchRepository(database);

        var first = await repository.AddIfNewAsync(JobMatch.Create(job, profile, IntegrationData.Analysis(80), IntegrationData.Now), Ct);
        var second = await repository.AddIfNewAsync(JobMatch.Create(job, profile, IntegrationData.Analysis(95), IntegrationData.Now), Ct);

        first.Should().BeTrue();
        second.Should().BeFalse();
        (await repository.ExistsAsync(job.Id, profile.Version, Ct)).Should().BeTrue();
        (await repository.ExistsAsync(job.Id, "outra-versao", Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Should_Keep_History_Across_Profile_Versions()
    {
        var (database, job, profile) = await Seed();
        var repository = new JobMatchRepository(database);
        await repository.AddIfNewAsync(JobMatch.Create(job, profile, IntegrationData.Analysis(80), IntegrationData.Now), Ct);

        profile.Update(profile.Content with { DesiredTechnologies = ["Kubernetes"] });
        await new CandidateProfileRepository(database).SaveAsync(profile, Ct);
        var added = await repository.AddIfNewAsync(JobMatch.Create(job, profile, IntegrationData.Analysis(70), IntegrationData.Now), Ct);

        added.Should().BeTrue();
        await using var context = database.CreateContext();
        (await context.JobMatches.CountAsync(Ct)).Should().Be(2);
    }

    [Fact]
    public async Task Should_Create_And_Save_Search_Configuration()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var repository = new JobSearchConfigurationRepository(database);

        var configuration = await repository.GetOrCreateAsync(JobSource.LinkedIn, Ct);
        configuration.RecordCheck("7:300", IntegrationData.Now);
        await repository.SaveAsync(configuration, Ct);

        var reloaded = await repository.GetOrCreateAsync(JobSource.LinkedIn, Ct);
        reloaded.Checkpoint.Should().Be("7:300");
        reloaded.LastCheckedAt.Should().Be(IntegrationData.Now);
    }

    private async Task<(TestDatabase, Job, Domain.Profiles.CandidateProfile)> Seed()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var job = IntegrationData.Job();
        var profile = IntegrationData.Profile();
        await new JobRepository(database).AddIfNewAsync(job, Ct);
        await new CandidateProfileRepository(database).SaveAsync(profile, Ct);
        return (database, job, profile);
    }
}

[Trait("Category", "Integration")]
public sealed class NotificationRepositoryTests(PostgresFixture postgres)
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_List_Only_Matches_Above_Score_Without_Notification()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var profile = IntegrationData.Profile();
        await new CandidateProfileRepository(database).SaveAsync(profile, Ct);
        var high = await SeedMatch(database, profile, score: 90);
        await SeedMatch(database, profile, score: 60);
        var notified = await SeedMatch(database, profile, score: 95);
        var repository = new NotificationRepository(database);
        await repository.AddIfNewAsync(Notification.Create(notified, IntegrationData.Now), Ct);

        var awaiting = await repository.GetMatchesAwaitingNotificationAsync(profile.Id, 75, Ct);

        awaiting.Should().ContainSingle().Which.Id.Should().Be(high.Id);
    }

    [Fact]
    public async Task Should_Not_Add_Second_Notification_For_Same_Job_And_Profile()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var profile = IntegrationData.Profile();
        await new CandidateProfileRepository(database).SaveAsync(profile, Ct);
        var match = await SeedMatch(database, profile, score: 90);
        var repository = new NotificationRepository(database);

        var first = await repository.AddIfNewAsync(Notification.Create(match, IntegrationData.Now), Ct);
        var second = await repository.AddIfNewAsync(Notification.Create(match, IntegrationData.Now), Ct);

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    [Fact]
    public async Task Should_Claim_Due_Notification_With_Job_Match_And_Source()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var profile = IntegrationData.Profile();
        await new CandidateProfileRepository(database).SaveAsync(profile, Ct);
        var match = await SeedMatch(database, profile, score: 90);
        var repository = new NotificationRepository(database);
        await repository.AddIfNewAsync(Notification.Create(match, IntegrationData.Now), Ct);

        var claimed = await repository.ClaimDueAsync(IntegrationData.Now, Lease, 10, Ct);
        var again = await repository.ClaimDueAsync(IntegrationData.Now.AddSeconds(10), Lease, 10, Ct);

        var work = claimed.Should().ContainSingle().Subject;
        work.Notification.Status.Should().Be(NotificationStatus.Sending);
        work.Job.Id.Should().Be(match.JobId);
        work.Match.Id.Should().Be(match.Id);
        work.SourceName.Should().Be("LinkedIn");
        again.Should().BeEmpty();
    }

    [Fact]
    public async Task Should_Persist_Delivery_Result()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        var profile = IntegrationData.Profile();
        await new CandidateProfileRepository(database).SaveAsync(profile, Ct);
        var match = await SeedMatch(database, profile, score: 90);
        var repository = new NotificationRepository(database);
        await repository.AddIfNewAsync(Notification.Create(match, IntegrationData.Now), Ct);
        var work = (await repository.ClaimDueAsync(IntegrationData.Now, Lease, 10, Ct)).Single();

        work.Notification.MarkSent("999", IntegrationData.Now);
        await repository.UpdateAsync(work.Notification, Ct);

        (await repository.ClaimDueAsync(IntegrationData.Now.AddDays(1), Lease, 10, Ct)).Should().BeEmpty();
        await using var context = database.CreateContext();
        (await context.Notifications.SingleAsync(Ct)).ExternalMessageId.Should().Be("999");
    }

    private static async Task<JobMatch> SeedMatch(TestDatabase database, Domain.Profiles.CandidateProfile profile, int score)
    {
        var job = IntegrationData.Job(title: $"Vaga {Guid.NewGuid():N}");
        await new JobRepository(database).AddIfNewAsync(job, Ct);
        var match = JobMatch.Create(job, profile, IntegrationData.Analysis(score), IntegrationData.Now);
        await new JobMatchRepository(database).AddIfNewAsync(match, Ct);
        return match;
    }
}
