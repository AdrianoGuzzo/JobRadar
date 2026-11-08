using Hangfire;
using Hangfire.Common;
using JobRadar.Worker.Jobs;

namespace JobRadar.IntegrationTests.Worker;

[Trait("Category", "Integration")]
public sealed class RecurringJobRegistrarTests
{
    [Fact]
    public void Should_Register_The_Three_Jobs_With_Configured_Schedules()
    {
        var manager = new FakeRecurringJobManager();
        var options = new MonitoringOptions { CollectCron = "*/30 * * * *", AnalyzeCron = "*/5 * * * *", NotifyCron = "*/2 * * * *" };

        RecurringJobRegistrar.Register(manager, options);

        manager.Registered.Should().BeEquivalentTo(
        [
            ("jobradar:collect", nameof(RecurringJobs.CollectAsync), "*/30 * * * *"),
            ("jobradar:analyze", nameof(RecurringJobs.AnalyzeAsync), "*/5 * * * *"),
            ("jobradar:notify", nameof(RecurringJobs.NotifyAsync), "*/2 * * * *"),
        ]);
    }

    [Fact]
    public void Should_Have_Sensible_Default_Schedules()
    {
        var options = new MonitoringOptions();

        options.CollectCron.Should().Be("*/15 * * * *");
        options.AnalyzeCron.Should().Be("*/2 * * * *");
        options.NotifyCron.Should().Be("* * * * *");
    }

    private sealed class FakeRecurringJobManager : IRecurringJobManager
    {
        public List<(string Id, string Method, string Cron)> Registered { get; } = [];

        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options) =>
            Registered.Add((recurringJobId, job.Method.Name, cronExpression));

        public void Trigger(string recurringJobId) => throw new NotSupportedException();

        public void RemoveIfExists(string recurringJobId) => throw new NotSupportedException();
    }
}
