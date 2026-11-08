using JobRadar.Domain.Jobs;
using Microsoft.EntityFrameworkCore;

namespace JobRadar.IntegrationTests.Persistence;

[Trait("Category", "Integration")]
public sealed class MigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Should_Apply_Migrations_To_Empty_Database()
    {
        var database = new TestDatabase(await postgres.CreateEmptyDatabaseAsync());
        await using var context = database.CreateContext();

        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        (await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Should().NotBeEmpty();
        (await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task Should_Have_No_Model_Changes_Missing_From_Migrations()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        await using var context = database.CreateContext();

        context.Database.HasPendingModelChanges().Should().BeFalse("toda mudança no modelo precisa de uma migration");
    }

    [Fact]
    public async Task Should_Seed_Job_Sources()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        await using var context = database.CreateContext();

        var sources = await context.JobSources.ToListAsync(TestContext.Current.CancellationToken);

        sources.Should().ContainSingle(source => source.Id == JobSource.LinkedIn.Id && source.Name == "LinkedIn");
    }
}
