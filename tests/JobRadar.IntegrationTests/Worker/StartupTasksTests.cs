using JobRadar.Worker.Startup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobRadar.IntegrationTests.Worker;

[Trait("Category", "Integration")]
public sealed class StartupTasksTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_Create_Schema_And_Import_Profile_On_Empty_Database()
    {
        var database = new TestDatabase(await postgres.CreateEmptyDatabaseAsync());
        await using var provider = WorkerTestHost.Build(WorkerTestHost.Settings(database.ConnectionString));

        await provider.GetRequiredService<StartupTasks>().RunAsync(Ct);

        await using var context = database.CreateContext();
        (await context.Database.GetPendingMigrationsAsync(Ct)).Should().BeEmpty();
        (await context.CandidateProfiles.SingleAsync(Ct)).Content.Name.Should().Be("Candidato de demonstração");
        (await context.JobSources.Select(s => s.Name).ToListAsync(Ct)).Should().Contain("LinkedIn");
    }

    [Fact]
    public async Task Should_Be_Safe_To_Run_On_Every_Start()
    {
        var database = new TestDatabase(await postgres.CreateEmptyDatabaseAsync());
        await using var provider = WorkerTestHost.Build(WorkerTestHost.Settings(database.ConnectionString));

        await provider.GetRequiredService<StartupTasks>().RunAsync(Ct);
        await provider.GetRequiredService<StartupTasks>().RunAsync(Ct);

        await using var context = database.CreateContext();
        (await context.CandidateProfiles.CountAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Should_Stop_With_Clear_Message_When_Claude_Cli_Is_Unusable()
    {
        var database = new TestDatabase(await postgres.CreateEmptyDatabaseAsync());
        var settings = WorkerTestHost.Settings(database.ConnectionString);
        settings["Claude:ExecutablePath"] = Path.Combine(Path.GetTempPath(), "nao-existe-claude.exe");
        await using var provider = WorkerTestHost.Build(settings);

        var act = () => provider.GetRequiredService<StartupTasks>().RunAsync(Ct);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Claude__ExecutablePath*");
    }

    [Fact]
    public async Task Should_Stop_With_Clear_Message_When_Profile_File_Is_Missing()
    {
        var database = new TestDatabase(await postgres.CreateEmptyDatabaseAsync());
        var settings = WorkerTestHost.Settings(database.ConnectionString);
        settings["Profile:Path"] = Path.Combine(Path.GetTempPath(), "nao-existe-profile.json");
        await using var provider = WorkerTestHost.Build(settings);

        var act = () => provider.GetRequiredService<StartupTasks>().RunAsync(Ct);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*profile.example.json*");
    }
}
